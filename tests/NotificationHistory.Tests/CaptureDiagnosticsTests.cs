using System.Text.Json;
using NotificationHistory.Core.Services;

static class CaptureDiagnosticsTests
{
    public static async Task RunAsync(string root)
    {
        void Check(bool value, string label) { if (!value) throw new Exception(label); Console.WriteLine("PASS " + label); }
        var directory = Path.Combine(root, "diagnostics", "CaptureDiagnostics");
        var store = new CaptureDiagnosticsStore(() => Path.Combine(root, "diagnostics", "history.sqlite3"));
        Check((await store.ReadAsync()).Count == 0 && !Directory.Exists(directory), "Reading diagnostics does not create storage or invent a capture attempt");
        Directory.CreateDirectory(directory);
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        string Write(long started, string stage, string outcome, bool saved, string? errorCode = null, int? platformCode = null)
        {
            var id = Guid.NewGuid().ToString("D");
            File.WriteAllText(Path.Combine(directory, id + ".json"), JsonSerializer.Serialize(new {
                schema = 1, id, startedAt = started, updatedAt = started + 50, stage, outcome, saved, errorCode, platformCode,
                // A future/malformed writer must not cause content to appear in copied diagnostics.
                body = "PRIVATE notification message", filename = "PRIVATE.ipa", sourceApp = "PRIVATE app"
            }));
            return id;
        }
        var savedId = Write(now, "completed", "saved", true);
        var failedId = Write(now + 1, "opening_storage", "failed", false, "writer_lock_timeout");
        Write(now + 2, "saving", "failed", false, "sqlite_error", 5);
        Write(now + 3, "requesting_widget_refresh", "failed", true, "unexpected_error");
        Write(now + 4, "saving", "running", false);
        var attempts = await store.ReadAsync();
        Check(attempts.Count == 5 && attempts.Single(a => a.Id == savedId).Status == "Saved" &&
            attempts.Single(a => a.Id == failedId).ErrorDisplay == "Timed out waiting for the history lock",
            "Swift diagnostic JSON preserves save status, stages, and recognizable lock errors");
        Check(attempts.Single(a => a.PlatformCode == 5).ErrorDisplay == "Database busy or locked" &&
            attempts.Any(a => a.Status == "Saved; later failure") && attempts[0].Status == "In progress or interrupted",
            "Diagnostics distinguish storage contention, post-save failure, and uncertain unfinished attempts");
        var report = CaptureDiagnosticsStore.Report(attempts);
        Check(!report.Contains("PRIVATE") && report.Contains("writer_lock_timeout") && report.Contains("Platform code: 5") && report.Contains("+00:00"),
            "Copied diagnostics contain UTC times and error codes without messages, filenames, or source apps");
        var corrupt = Guid.NewGuid().ToString("D"); File.WriteAllText(Path.Combine(directory, corrupt + ".json"), "not json");
        var oversized = Guid.NewGuid().ToString("D"); File.WriteAllText(Path.Combine(directory, oversized + ".json"), new string('x', 5000));
        var mismatch = Guid.NewGuid().ToString("D"); File.WriteAllText(Path.Combine(directory, mismatch + ".json"),
            JsonSerializer.Serialize(new { schema = 1, id = savedId, startedAt = now, updatedAt = now, stage = "saved", outcome = "saved" }));
        Write(now + 5, "unknown", "failed", false, "unexpected_error");
        Write(now + 6, "saving", "failed", false, "PRIVATE custom error");
        Check((await store.ReadAsync()).Count == 5, "Malformed, oversized, mismatched, and unknown diagnostic records are skipped safely");
        for (var i = 0; i < 60; i++) Write(now + 100 + i, "completed", "saved", true);
        attempts = await store.ReadAsync();
        Check(attempts.Count == 50 && attempts[0].StartedAt == now + 159 && attempts[^1].StartedAt == now + 110,
            "Diagnostic history is limited to the newest 50 attempts by start time");
        var keep = Path.Combine(directory, "keep.json"); File.WriteAllText(keep, "Unrelated file");
        var history = Path.Combine(root, "diagnostics", "history.sqlite3"); File.WriteAllText(history, "History fixture");
        await store.ClearAsync();
        Check((await store.ReadAsync()).Count == 0 && File.Exists(keep) && File.ReadAllText(history) == "History fixture",
            "Clearing diagnostics preserves unrelated files and notification history");
    }
}
