using NotificationHistory.Core.Data;
using NotificationHistory.Core.Models;
using NotificationHistory.Core.Services;
using SQLite;

static class AppOpeningTests
{
    public static async Task RunAsync(string directory)
    {
        void Check(bool value, string label) { if (!value) throw new Exception(label); Console.WriteLine("PASS " + label); }
        Check(AppOpeningLinks.Normalize(null) is null && AppOpeningLinks.Normalize("  ") is null &&
            AppOpeningLinks.Normalize(" chatapp:// ") == "chatapp://" &&
            AppOpeningLinks.Normalize("https://example.com/open?a=1&b=2") == "https://example.com/open?a=1&b=2",
            "Opening links accept empty fallback, full custom schemes and HTTPS app links");
        foreach (var invalid in new[] { "com.example.app", "chatapp:", "file:///private/history.sqlite3", "javascript://test",
            "data://test", "notificationhistory://source-app/1", "NOTIFICATIONHISTORY://history", "http://example.com",
            "https://", "https://user:password@example.com", "chatapp://invalid link", "chatapp://x\ny", "chatapp://" + new string('x', 2048) })
        {
            var rejected = false;
            try { AppOpeningLinks.Normalize(invalid); } catch (ArgumentException) { rejected = true; }
            Check(rejected, "Unsupported or malformed opening link is rejected");
        }
        Check(DeepLinks.TryParseSourceApp("notificationhistory://source-app/42", out var id) && id == 42 &&
            DeepLinks.TryParse("notificationhistory://notification/42", out id) && id == 42,
            "Widget source-app links and existing saved-detail links have separate routes");
        foreach (var invalid in new[] { "https://source-app/42", "notificationhistory://source-app/0", "notificationhistory://source-app/-1",
            "notificationhistory://source-app/42/43", "notificationhistory://source-app/9223372036854775808",
            "notificationhistory://source-app/42?url=chatapp://", "notificationhistory://user@source-app/42", "notificationhistory://source-app:80/42" })
            Check(!DeepLinks.TryParseSourceApp(invalid, out _), "Widget source route accepts only a saved notification ID");

        var path = Path.Combine(directory, "app-opening.sqlite3");
        var database = new NotificationDatabase(path);
        var profile = AppProfile.Default("Case App"); profile.OpenAppUrl = " chatapp:// ";
        await database.SaveProfileAsync(profile);
        Check(await new NotificationDatabase(path).AppOpeningUrlAsync(" case app ") == "chatapp://" &&
            (await database.ProfilesAsync()).Single().OpenAppUrl == "chatapp://",
            "Opening URLs persist locally and match normalized notification sources");
        using (var connection = new SQLiteConnection(path))
            Check(connection.ExecuteScalar<int>("PRAGMA user_version") == 2 &&
                !connection.GetTableInfo("AppProfiles").Any(c => c.Name == "OpenAppUrl"),
                "Opening links preserve shared schema version 2 for native extensions");
        profile.OpenAppUrl = ""; await database.SaveProfileAsync(profile, profile.SourceKey);
        Check(await database.AppOpeningUrlAsync("Case App") is null && (await database.ProfilesAsync()).Single().OpenAppUrl is null,
            "Clearing the configured URL restores the saved-detail fallback");
        profile.OpenAppUrl = "chatapp://home"; await database.SaveProfileAsync(profile, profile.SourceKey);
        var oldKey = profile.SourceKey; profile.SourceName = "Renamed App";
        await database.SaveProfileAsync(profile, oldKey);
        Check(await database.AppOpeningUrlAsync("Case App") is null && await database.AppOpeningUrlAsync("Renamed App") == "chatapp://home",
            "Source rename moves the opening URL without leaving an old-source launch setting");
        var other = AppProfile.Default("Other App"); other.OpenAppUrl = "otherapp://"; await database.SaveProfileAsync(other);
        var collision = profile.Copy(); collision.SourceName = "Other App";
        var collisionRejected = false;
        try { await database.SaveProfileAsync(collision, profile.SourceKey); } catch (ArgumentException) { collisionRejected = true; }
        Check(collisionRejected && await database.AppOpeningUrlAsync("Renamed App") == "chatapp://home" &&
            await database.AppOpeningUrlAsync("Other App") == "otherapp://", "Rename collision preserves both opening links");
        await database.SaveAsync(new() { SourceApp = "Renamed App", Body = "Keep profile settings" });
        await database.ClearAsync();
        Check(await database.AppOpeningUrlAsync("Renamed App") == "chatapp://home", "Clearing history preserves app-opening configuration");
        await database.DeleteProfileAsync(profile.SourceKey);
        Check(await database.AppOpeningUrlAsync("Renamed App") is null && await database.AppOpeningUrlAsync("Other App") == "otherapp://",
            "Removing one customization removes only its opening URL");
    }
}
