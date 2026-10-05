using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using NotificationHistory.Core.Models;
namespace NotificationHistory.Core.Services;

// Independent of SQLite so a database failure can still be inspected.
public sealed class CaptureDiagnosticsStore(Func<string> databasePath)
{
    private string DirectoryPath => Path.Combine(Path.GetDirectoryName(databasePath())!, "CaptureDiagnostics");
    private static bool IsRecord(string file) => Path.GetExtension(file) == ".json" && Guid.TryParseExact(Path.GetFileNameWithoutExtension(file), "D", out _);
    public Task<List<CaptureAttempt>> ReadAsync() => Task.Run(() =>
    {
        var directory = DirectoryPath;
        if (!Directory.Exists(directory)) return new List<CaptureAttempt>();
        var records = new List<CaptureAttempt>();
        foreach (var path in Directory.EnumerateFiles(directory).Where(IsRecord)
            .OrderByDescending(File.GetLastWriteTimeUtc).Take(100))
        {
            try
            {
                if (new FileInfo(path).Length is 0 or > 4096) continue;
                var record = JsonSerializer.Deserialize(File.ReadAllText(path), CaptureDiagnosticsJsonContext.Default.CaptureAttempt);
                if (record is null || record.Schema != 1 || record.Id != Path.GetFileNameWithoutExtension(path) ||
                    record.StartedAt is <= 0 or > 253402300799999L || record.UpdatedAt < record.StartedAt || record.UpdatedAt > 253402300799999L ||
                    record.Stage is not ("started" or "opening_storage" or "storage_opened" or "saving" or "saved" or "requesting_widget_refresh" or "completed") ||
                    record.Outcome is not ("running" or "saved" or "failed") ||
                    record.ErrorCode is not null && record.ErrorDisplay is null) continue;
                records.Add(record);
            }
            // Atomic replacements, clearing, and unavailable files need not break the page.
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (JsonException) { }
        }
        return records.OrderByDescending(r => r.StartedAt).ThenBy(r => r.Id, StringComparer.Ordinal).Take(50).ToList();
    });
    public Task ClearAsync() => Task.Run(() =>
    {
        var directory = DirectoryPath;
        if (!Directory.Exists(directory)) return;
        foreach (var path in Directory.EnumerateFiles(directory).Where(IsRecord)) File.Delete(path);
    });
    public static string Report(IEnumerable<CaptureAttempt> attempts)
    {
        var text = new StringBuilder("Notification History capture diagnostics\nTimes are UTC. No notification content is included.\n");
        foreach (var attempt in attempts)
        {
            text.AppendLine();
            text.AppendLine($"Attempt: {attempt.Id}");
            text.AppendLine($"Started: {DateTimeOffset.FromUnixTimeMilliseconds(attempt.StartedAt):O}");
            text.AppendLine($"Updated: {DateTimeOffset.FromUnixTimeMilliseconds(attempt.UpdatedAt):O}");
            text.AppendLine($"Status: {attempt.Status}; Stage: {attempt.StageDisplay}");
            if (attempt.ErrorCode is not null) text.AppendLine($"Error: {attempt.ErrorCode}; Platform code: {attempt.PlatformCode?.ToString() ?? "none"}");
        }
        return text.ToString();
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(CaptureAttempt))]
internal partial class CaptureDiagnosticsJsonContext : JsonSerializerContext;
