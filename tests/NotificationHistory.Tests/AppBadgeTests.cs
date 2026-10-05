using NotificationHistory.Core.Data;
using NotificationHistory.Core.Models;
using NotificationHistory.Core.Services;

static class AppBadgeTests
{
    public static async Task RunAsync(string directory)
    {
        void Check(bool value, string label) { if (!value) throw new Exception(label); Console.WriteLine("PASS " + label); }
        var db = new NotificationDatabase(Path.Combine(directory, "badges.sqlite3"));
        Check(AppBadgeCatalog.Build([], await db.SourceCountsAsync()).Count == 0, "Empty history has no invented app badges");
        var whatsapp = AppProfile.Default("WhatsApp"); whatsapp.DisplayName = "A WhatsApp"; whatsapp.BackgroundColor = "#ABCDEF";
        whatsapp.ImageFile = Guid.NewGuid().ToString("N") + "-icon.png";
        var unused = AppProfile.Default("New app"); unused.DisplayName = "Z New app";
        var school = AppProfile.Default("École"); school.DisplayName = "School";
        await db.SaveProfileAsync(whatsapp); await db.SaveProfileAsync(unused); await db.SaveProfileAsync(school);
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        long favorite = 0;
        for (var i = 0; i < 73; i++)
        {
            var id = await db.SaveAsync(new() { SourceApp = i % 2 == 0 ? "WhatsApp" : " whatsapp ", Body = "hello", ReceivedAt = now });
            if (i == 0) favorite = id;
        }
        await db.SetFavoriteAsync(favorite, true);
        await db.SaveAsync(new() { SourceApp = " école " }); await db.SaveAsync(new() { SourceApp = "ÉCOLE" });
        await db.SaveAsync(new() { SourceApp = "Instagram" });
        await db.SaveAsync(new()); await db.SaveAsync(new() { SourceApp = "" }); await db.SaveAsync(new() { SourceApp = "  " });
        var counts = await db.SourceCountsAsync();
        var badges = AppBadgeCatalog.Build(await db.ProfilesAsync(), counts);
        var wa = badges.Single(b => b.Profile.SourceKey == "WHATSAPP");
        Check(badges.Count == 5 && wa.Count == 73 && wa.Sources.Count == 2,
            "Badges merge case/whitespace variants and count beyond a history page");
        Check(wa.Profile.DisplayName == "A WhatsApp" && wa.Profile.ImageFile == whatsapp.ImageFile && wa.Profile.BackgroundColor == "#ABCDEF",
            "Badges use the configured display name, local photo and color");
        Check(badges[0] == wa && AppBadgeCatalog.Build(await db.ProfilesAsync(), counts.AsEnumerable().Reverse()).Select(b => b.Profile.SourceKey)
            .SequenceEqual(badges.Select(b => b.Profile.SourceKey)), "Alphabetical badge order is stable regardless of count query order");
        var schoolBadge = badges.Single(b => b.Profile.SourceKey == AppProfile.Key("École"));
        Check(schoolBadge.Count == 2 && (await db.QueryAsync(new(Sources: schoolBadge.Sources))).Count == 2,
            "Unicode app-name variants share one badge and filter together");
        var unknown = badges.Single(b => b.Profile.SourceKey == "");
        Check(unknown.Count == 3 && unknown.Profile.DisplayName == "Unknown app" && (await db.QueryAsync(new(Sources: unknown.Sources))).Count == 3,
            "Missing and blank source names remain accessible through the Unknown app badge");
        Check(badges.Single(b => b.Profile.SourceKey == "INSTAGRAM").Profile.BackgroundColor == AppProfile.Default("Instagram").BackgroundColor,
            "Discovered apps use their default appearance before customization");
        var zero = badges.Single(b => b.Profile.SourceKey == unused.SourceKey);
        Check(zero.Count == 0 && (await db.QueryAsync(new(Sources: zero.Sources))).Count == 0,
            "Configured apps with zero captures show empty history rather than all apps");
        Check((await db.QueryAsync(new(Search: "hello", Favorites: true, From: now, To: now + 1, Sources: wa.Sources))).Single().Id == favorite &&
            wa.Count == 73, "Search, favorites and date filters combine within an app without changing its total badge count");
        var ids = new HashSet<long>(); PageCursor? cursor = null;
        while (true)
        {
            var page = await db.QueryAsync(new(Sources: wa.Sources), cursor, 17);
            if (page.Count == 0) break;
            foreach (var row in page) if (!ids.Add(row.Id)) throw new Exception("Duplicate badge-filter page row");
            cursor = new(page[^1].ReceivedAt, page[^1].Id);
        }
        Check(ids.Count == 73, "App badge filter preserves stable paging across source variants");
        await db.DeleteAsync(favorite);
        Check(AppBadgeCatalog.Build(await db.ProfilesAsync(), await db.SourceCountsAsync()).Single(b => b.Profile.SourceKey == "WHATSAPP").Count == 72,
            "Deleting a notification updates the app's badge count");
        await db.SaveAsync(new() { SourceApp = "Expired app", ReceivedAt = now - 8 * 86400000L });
        await db.SetSettingAsync("retention", "7"); await db.CleanupAsync(DateTimeOffset.FromUnixTimeMilliseconds(now));
        Check((await db.SourceCountsAsync()).All(c => c.SourceApp != "Expired app"), "Retention removes expired sources from badge counts");
        await db.ClearAsync();
        badges = AppBadgeCatalog.Build(await db.ProfilesAsync(), await db.SourceCountsAsync());
        Check(badges.Count == 3 && badges.All(b => b.Count == 0 && b.Sources.Count == 0),
            "Clear history preserves configured badges with zero counts and removes discovered-only badges");
    }
}
