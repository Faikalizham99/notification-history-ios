using NotificationHistory.Core.Data;
using NotificationHistory.Core.Models;
using NotificationHistory.Core.Services;

static class AppCleanupTests
{
    public static async Task RunAsync(string directory)
    {
        void Check(bool value, string label) { if (!value) throw new Exception(label); Console.WriteLine("PASS " + label); }
        var db = new NotificationDatabase(Path.Combine(directory, "app-cleanup.sqlite3"));
        var profile = AppProfile.Default("WhatsApp"); profile.OpenAppUrl = "chatapp://";
        profile.ImageFile = Guid.NewGuid().ToString("N") + "-icon.png";
        await db.SaveProfileAsync(profile); await db.SetSettingAsync("appearance", "Dark");
        var favorite = await db.SaveAsync(new() { SourceApp = " whatsapp ", Body = "Keep favorite", ReceivedAt = 1 });
        await db.SetFavoriteAsync(favorite, true);
        await db.SaveAsync(new() { SourceApp = "WhatsApp", Body = "Outside current filters" });
        await db.SaveAsync(new() { SourceApp = "WHATSAPP", Body = "Another date", ReceivedAt = 2 });
        var other = await db.SaveAsync(new() { SourceApp = "WhatsApp Business", Body = "Keep other app" });
        // Capture a new spelling after loading badges: deletion must resolve current sources.
        var before = AppBadgeCatalog.Build(await db.ProfilesAsync(), await db.SourceCountsAsync());
        await db.SaveAsync(new() { SourceApp = "\twhatsapp\t", Body = "Late capture" });
        Check(await db.ClearAppAsync(before.Single(b => b.Profile.SourceKey == "WHATSAPP").Profile.SourceKey) == 3,
            "Per-app cleanup includes current source variants and captures outside filters");
        Check(await db.GetAsync(favorite) is not null && await db.GetAsync(other) is not null &&
            (await db.QueryAsync(new())).Count == 2, "Per-app cleanup keeps old favorites and other apps");
        var saved = (await db.ProfilesAsync()).Single();
        Check(saved.ImageFile == profile.ImageFile && saved.BackgroundColor == profile.BackgroundColor &&
            saved.OpenAppUrl == "chatapp://" && await db.SettingAsync("appearance") == "Dark",
            "Per-app cleanup preserves photos, colors, opening links and global settings");
        Check(await db.ClearAppAsync(" whatsapp ") == 0, "Repeated cleanup is harmless with only favorites left");
        await db.SetFavoriteAsync(favorite, false); await db.ClearAppAsync("WHATSAPP");
        Check(AppBadgeCatalog.Build(await db.ProfilesAsync(), await db.SourceCountsAsync())
            .Single(b => b.Profile.SourceKey == "WHATSAPP").Count == 0, "Configured app badge remains after its history becomes empty");

        await db.SaveAsync(new() { SourceApp = " École " });
        await db.SaveAsync(new() { SourceApp = "école" });
        var schoolFavorite = await db.SaveAsync(new() { SourceApp = "ÉCOLE" }); await db.SetFavoriteAsync(schoolFavorite, true);
        Check(await db.ClearAppAsync(AppProfile.Key("École")) == 2 && await db.GetAsync(schoolFavorite) is not null,
            "Per-app cleanup uses the same Unicode case normalization as app badges");
        await db.SaveAsync(new()); await db.SaveAsync(new() { SourceApp = "" });
        var unknownFavorite = await db.SaveAsync(new() { SourceApp = "\t " }); await db.SetFavoriteAsync(unknownFavorite, true);
        Check(await db.ClearAppAsync("") == 2 && await db.GetAsync(unknownFavorite) is not null && await db.GetAsync(other) is not null,
            "Unknown app cleanup matches null and blank sources while protecting favorites and named apps");
        await db.SaveAsync(new() { SourceApp = "O'Brien_100%" });
        var similar = await db.SaveAsync(new() { SourceApp = "O'BrienX100other" });
        Check(await db.ClearAppAsync(AppProfile.Key("O'Brien_100%")) == 1 && await db.GetAsync(similar) is not null,
            "Per-app cleanup treats quotes and SQL wildcard characters as literal source names");
        Check(await db.ClearAppAsync("Missing app") == 0 && await db.GetAsync(other) is not null,
            "A missing app never clears combined history");
        var rejected = false;
        try { await db.ClearAppAsync(null!); } catch (ArgumentNullException) { rejected = true; }
        Check(rejected && await db.GetAsync(other) is not null, "A null cleanup target is rejected");
    }
}
