using NotificationHistory.Core.Data;
using NotificationHistory.Core.Models;
using NotificationHistory.Core.Services;
using SQLite;

static class AppearanceTests
{
    public static async Task RunAsync(string directory)
    {
        void Check(bool value, string label) { if (!value) throw new Exception(label); Console.WriteLine("PASS " + label); }
        var path = Path.Combine(directory, "legacy.sqlite3");
        SeedLegacy(path);
        var store = new NotificationDatabase(path);
        Check((await store.ProfilesAsync()).Count == 0 && (await store.GetAsync(1)) is { IsFavorite: true, Body: "Legacy message", CaptureId: "legacy-event" } &&
            await store.SettingAsync("appearance") == "Dark", "Version-1 migration preserves history, favorites, capture IDs and settings");
        using (var migrated = new SQLiteConnection(path)) Check(migrated.ExecuteScalar<int>("PRAGMA user_version") == 2, "Shared appearance schema upgraded to version 2");
        var profile = AppProfile.Default(" whatsapp "); profile.DisplayName = "My WhatsApp"; profile.BackgroundColor = "#abc";
        profile.ImageFile = Guid.NewGuid().ToString("N") + "-icon.png"; profile.IconShape = "Circle";
        await store.SaveProfileAsync(profile);
        var loaded = (await new NotificationDatabase(path).ProfilesAsync()).Single();
        Check(loaded.SourceKey == AppProfile.Key("WHATSAPP") && loaded.DisplayName == "My WhatsApp" && loaded.BackgroundColor == "#AABBCC" &&
            loaded.ImageFile == profile.ImageFile && loaded.IconShape == "Circle", "Per-app image, colors and shape persist and match case/whitespace variants");
        var duplicate = false;
        try { await store.SaveProfileAsync(AppProfile.Default("WhatsApp")); } catch (ArgumentException) { duplicate = true; }
        Check(duplicate && (await store.ProfilesAsync()).Single().DisplayName == "My WhatsApp", "Duplicate source does not overwrite another appearance");
        await store.SaveProfileAsync(AppProfile.Default("Instagram"));
        loaded.SourceName = "Instagram";
        duplicate = false;
        try { await store.SaveProfileAsync(loaded, "WHATSAPP"); } catch (ArgumentException) { duplicate = true; }
        Check(duplicate && (await store.ProfilesAsync()).Count == 2, "Source rename collision rolls back without deleting the old profile");
        loaded.SourceName = "WhatsApp Business"; await store.SaveProfileAsync(loaded, "WHATSAPP");
        Check((await store.ProfilesAsync()).All(p => p.SourceKey != "WHATSAPP") && (await store.GetAsync(1))!.SourceApp == "WhatsApp", "Profile rename preserves original notification sources");
        await store.ClearAsync(); Check((await store.ProfilesAsync()).Count == 2, "Appearance profiles survive clearing notification history");
        Check(!AppearanceColors.TryNormalize("#GGFFFF", out _) && !AppearanceColors.TryNormalize("#FF000000", out _) &&
            AppearanceColors.AutomaticText("#000000") == "#FFFFFF" && AppearanceColors.AutomaticText("#FFFFFF") == "#111111", "Color validation and readable light/dark text selection");
        Check(!AppearanceColors.IsImageFile("../../secret.png") && !AppearanceColors.IsImageFile("https://example.com/icon.png") &&
            AppearanceColors.IsImageFile(Guid.NewGuid().ToString("N") + "-source.png"), "Icon references allow only generated local filenames");
        var portrait = IconCropGeometry.DrawRectangle(100, 200, 300, 3, 0, 150);
        var landscape = IconCropGeometry.DrawRectangle(200, 100, 300, 3, 150, 0);
        Check(portrait == new IconDrawRectangle(0, -256, 512, 1024) && landscape == new IconDrawRectangle(-256, 0, 1024, 512), "Centered portrait and landscape crops map to the correct output pixels");
        var clamped = IconCropGeometry.DrawRectangle(100, 100, 300, 1, -99, 999);
        Check(clamped == new IconDrawRectangle(0, 0, 512, 512), "Crop bounds prevent zooming out to blank space or dragging past the image");
        var invalid = false;
        try { IconCropGeometry.DrawRectangle(double.NaN, 100, 300, 1, 0, 0); } catch (ArgumentException) { invalid = true; }
        Check(invalid, "Invalid crop dimensions rejected");
        var images = new AppearanceStore(store);
        var png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+a1h8AAAAASUVORK5CYII=");
        var photo = AppProfile.Default("Photo app");
        await images.SaveAsync(photo, null, png, png);
        var savedPhoto = (await images.LoadAsync()).Single(p => p.SourceKey == "PHOTO APP");
        var oldIcon = images.ImagePath(savedPhoto.ImageFile)!;
        Check(File.ReadAllBytes(oldIcon).SequenceEqual(png) && images.ImagePath(savedPhoto.OriginalImageFile) is not null,
            "Cropped icon and re-croppable source are stored locally alongside the shared database");
        await images.SaveAsync(savedPhoto, savedPhoto.SourceKey, png, png);
        var updatedPhoto = (await images.LoadAsync()).Single(p => p.SourceKey == "PHOTO APP");
        Check(updatedPhoto.ImageFile != savedPhoto.ImageFile && !File.Exists(oldIcon) && images.ImagePath(updatedPhoto.ImageFile) is not null,
            "Replacing an icon publishes new files and clears the unreferenced old crop");
        var imageDirectory = Path.GetDirectoryName(images.ImagePath(updatedPhoto.ImageFile))!;
        var fileCount = Directory.GetFiles(imageDirectory).Length;
        duplicate = false;
        try { await images.SaveAsync(AppProfile.Default("Photo app"), null, png, png); } catch (ArgumentException) { duplicate = true; }
        Check(duplicate && Directory.GetFiles(imageDirectory).Length == fileCount &&
            (await store.ProfilesAsync()).Single(p => p.SourceKey == "PHOTO APP").ImageFile == updatedPhoto.ImageFile,
            "Failed profile save cleans newly written images and preserves the previous icon");
        var orphan = Path.Combine(imageDirectory, Guid.NewGuid().ToString("N") + "-icon.png"); File.WriteAllBytes(orphan, png);
        var unrelated = Path.Combine(imageDirectory, "keep.txt"); File.WriteAllText(unrelated, "Unrelated user file");
        await images.LoadAsync();
        Check(!File.Exists(orphan) && File.Exists(unrelated), "Interrupted-save image cleanup preserves unrelated files");
        await store.ClearAsync(); Check(images.ImagePath(updatedPhoto.ImageFile) is not null, "Clearing notification history preserves app photos");
        await images.ResetAsync(updatedPhoto.SourceKey);
        Check(images.ImagePath(updatedPhoto.ImageFile) is null && File.Exists(unrelated) && (await store.ProfilesAsync()).All(p => p.SourceKey != "PHOTO APP"),
            "Removing appearance clears its unused image files while preserving unrelated files");
    }
    public static void SeedLegacy(string path)
    {
        using var db = new SQLiteConnection(path);
        using var resource = typeof(NotificationDatabase).Assembly.GetManifestResourceStream("schema.sql")!;
        using var reader = new StreamReader(resource);
        foreach (var statement in reader.ReadToEnd().Split(';').Select(s => s.Trim()).Where(s => s.Length > 0 && !s.StartsWith("CREATE TABLE IF NOT EXISTS AppProfiles") && !s.StartsWith("PRAGMA user_version"))) db.Execute(statement);
        db.Execute("PRAGMA user_version=1");
        db.Execute("INSERT INTO Settings(Key,Value) VALUES('appearance','Dark')");
        db.Execute("INSERT INTO Notifications(SourceApp,Title,Body,ReceivedAt,CreatedAt,IsFavorite,CaptureId) VALUES(?,?,?,?,?,?,?)",
            "WhatsApp", "Legacy sender", "Legacy message", 1000, 1000, 1, "legacy-event");
    }
}
