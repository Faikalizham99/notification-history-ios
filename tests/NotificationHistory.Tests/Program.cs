using NotificationHistory.Core.Data;
using NotificationHistory.Core.Models;
using NotificationHistory.Core.Services;
if (args.Length == 2)
{
    var shared = new NotificationDatabase(args[1]);
    switch (args[0])
    {
        case "--seed-shared":
            await shared.SaveAsync(new() { SourceApp = "Managed 👋", Title = "Ali", Body = "Bro tomorrow jadi? 明天见\n100% _" });
            await shared.SaveAsync(new()); break;
        case "--burst-shared":
            for (var i = 0; i < 64; i++) await shared.SaveAsync(new() { SourceApp = "Managed burst", Title = i.ToString() });
            break;
        case "--verify-shared":
            var native = (await shared.QueryAsync(new(Source: "Native 🐈"))).Single();
            Check((await shared.GetAsync(native.Id))!.Body == "Swift → C# 明天见\nRM25.00", "Managed reads native Unicode write");
            Check((await shared.QueryAsync(new(Source: "Native burst"), limit: 100)).Count == 64, "Native concurrent writes retained");
            Check((await shared.QueryAsync(new(Source: "Managed burst"), limit: 100)).Count == 64, "Managed concurrent writes retained");
            Check(await shared.IntegrityAsync() == "ok", "Cross-process database integrity"); break;
        default: throw new ArgumentException("Unknown test mode");
    }
    return;
}
var directory = Path.Combine(Path.GetTempPath(), "NotificationHistory-tests-" + Guid.NewGuid());
Directory.CreateDirectory(directory);
try
{
    var path = Path.Combine(directory, "history.sqlite3"); var db = new NotificationDatabase(path);
    var now = DateTimeOffset.UtcNow;
    var body = "Bro tomorrow jadi? 👋 明天见\n100% _ literal ' quote" + new string('x', 30000);
    var id = await db.SaveAsync(new() { SourceApp = "App & 🐈", Title = "Ali", Body = body });
    if (OperatingSystem.IsMacOS())
    {
        Check(File.Exists(path + ".lock"), "Darwin shared writer lock created");
        Check(File.GetUnixFileMode(path + ".lock") == (UnixFileMode.UserRead | UnixFileMode.UserWrite),
            "Darwin shared writer lock uses owner-only permissions");
    }
    Check((await db.GetAsync(id))?.Body == body, "Unicode, multiline and long body round trip");
    var partial = await db.SaveAsync(new() { SourceApp = "Some App" });
    Check((await db.GetAsync(partial))?.Title is null, "Partial fields accepted");
    Check((await db.QueryAsync(new(Search: "100% _ literal"))).Count == 1, "LIKE wildcards escaped");
    Check((await db.QueryAsync(new(Search: "明天见"))).Count == 1, "Chinese substring search");
    Check((await db.QueryAsync(new(Search: "' OR 1=1 --"))).Count == 0, "Parameterized search");
    await db.SetFavoriteAsync(id, true);
    Check((await db.QueryAsync(new(Favorites: true, Source: "App & 🐈"))).Single().Id == id, "Combined favorites/source filters");
    Check((await db.SourcesAsync()).Contains("App & 🐈"), "Source selector preserves symbols");
    await db.MarkReadAsync(id); Check((await db.GetAsync(id))!.IsRead, "Read state persisted");
    var repeated = new NotificationEntry { Title = "Identical" }; var first = await db.SaveAsync(repeated);
    var second = await db.SaveAsync(new() { Title = "Identical" }); Check(first != second, "Repeated text preserved");
    var tokenId = await db.SaveAsync(new() { CaptureId = "event-1" });
    Check(tokenId == await db.SaveAsync(new() { CaptureId = "event-1" }), "Explicit event token idempotence");
    var connections = Enumerable.Range(0, 4).Select(_ => new NotificationDatabase(path)).ToArray();
    await Task.WhenAll(connections.Select(async (connection, index) =>
    {
        for (var i = 0; i < 40; i++) await connection.SaveAsync(new() { SourceApp = "Burst", Title = $"{index}/{i}", ReceivedAt = now.ToUnixTimeMilliseconds() });
    }));
    var found = new List<NotificationEntry>(); PageCursor? cursor = null;
    do
    {
        var page = await db.QueryAsync(new(Source: "Burst"), cursor, 17); if (page.Count == 0) break;
        found.AddRange(page); cursor = new(page[^1].ReceivedAt, page[^1].Id);
    } while (true);
    Check(found.Count == 160 && found.Select(x => x.Id).Distinct().Count() == 160, "Concurrent writes and stable keyset pagination");
    await db.SaveAsync(new() { SourceApp = "Old", ReceivedAt = now.AddDays(-8).ToUnixTimeMilliseconds() });
    await db.SetSettingAsync("retention", "7"); Check(await db.CleanupAsync(now) == 1, "Retention cutoff");
    var start = new DateTimeOffset(now.LocalDateTime.Date).ToUnixTimeMilliseconds();
    var end = new DateTimeOffset(now.LocalDateTime.Date.AddDays(1)).ToUnixTimeMilliseconds();
    Check((await db.QueryAsync(new(From: start, To: end), limit: 200)).Count >= 160, "Local day range");
    Check(DeepLinks.TryParse("notificationhistory://notification/123", out var parsed) && parsed == 123 &&
     !DeepLinks.TryParse("notificationhistory://notification/-1", out _) && !DeepLinks.TryParse("https://notification/123", out _), "Deep link validation");
    Check(await db.IntegrityAsync() == "ok", "Database integrity after concurrent access");
    await db.DeleteAsync(id); Check(await db.GetAsync(id) is null, "Delete");
    await db.ClearAsync(); Check((await db.QueryAsync(new())).Count == 0, "Clear history");
    Check(await db.SettingAsync("retention") == "7", "Settings survive clear");
    await db.SetSettingAsync("retention", "0");
    using (var fixture = new SQLite.SQLiteConnection(path))
    {
        fixture.RunInTransaction(() =>
        {
            for (var i = 0; i < 10_000; i++)
                fixture.Execute("INSERT INTO Notifications(SourceApp,Title,Body,ReceivedAt,CreatedAt) VALUES(?,?,?,?,?)",
                    "Volume", "Record " + i, "Synthetic searchable message " + i, now.ToUnixTimeMilliseconds() + i, now.ToUnixTimeMilliseconds());
        });
    }
    var volumeTimer = System.Diagnostics.Stopwatch.StartNew();
    var volumeIds = new HashSet<long>(); cursor = null;
    while (true)
    {
        var page = await db.QueryAsync(new(Source: "Volume"), cursor);
        if (page.Count == 0) break;
        foreach (var row in page) CheckNoOutput(volumeIds.Add(row.Id), "Duplicate pagination row");
        cursor = new(page[^1].ReceivedAt, page[^1].Id);
    }
    Check(volumeIds.Count == 10_000, "Incremental traversal of 10,000 records");
    Check((await db.QueryAsync(new(Search: "message 9999"))).Count == 1, "Substring search across 10,000 records");
    Console.WriteLine($"Volume traversal/search: {volumeTimer.ElapsedMilliseconds} ms on this host.");
    using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
    var cancelledCorrectly = false;
    try { await db.QueryAsync(new(), token: cancelled.Token); } catch (OperationCanceledException) { cancelledCorrectly = true; }
    Check(cancelledCorrectly, "Cancelled query does not perform database work");
    using (var future = new SQLite.SQLiteConnection(path)) future.Execute("PRAGMA user_version=2");
    var futureRejected = false;
    try { await new NotificationDatabase(path).QueryAsync(new()); } catch (InvalidOperationException) { futureRejected = true; }
    Check(futureRejected, "Newer schema rejected without destructive recovery");
    Console.WriteLine("All persistence checks passed.");
}
finally { Directory.Delete(directory, true); }
static void Check(bool condition, string message) { if (!condition) throw new Exception(message); Console.WriteLine("PASS " + message); }
static void CheckNoOutput(bool condition, string message) { if (!condition) throw new Exception(message); }
