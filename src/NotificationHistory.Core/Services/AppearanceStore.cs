using NotificationHistory.Core.Data;
using NotificationHistory.Core.Models;
using NotificationHistory.Core.Services;
namespace NotificationHistory.Core.Services;

public sealed class AppearanceStore(NotificationDatabase database)
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private string DirectoryPath => Path.Combine(Path.GetDirectoryName(database.Path)!, "Icons");
    public string? ImagePath(string? file) => file is not null && AppearanceColors.IsImageFile(file) &&
        File.Exists(Path.Combine(DirectoryPath, file)) ? Path.Combine(DirectoryPath, file) : null;
    public async Task<List<AppProfile>> LoadAsync()
    {
        await gate.WaitAsync();
        try { var profiles = await database.ProfilesAsync(); CleanUnused(profiles); return profiles; }
        finally { gate.Release(); }
    }
    public async Task SaveAsync(AppProfile draft, string? previousKey, byte[]? original, byte[]? cropped)
    {
        await gate.WaitAsync();
        var created = new List<string>();
        try
        {
            var profile = draft.Copy(); profile.Validate();
            if (cropped is not null)
            {
                if (cropped.Length is 0 or > 4_194_304 || original is null || original.Length is 0 or > 25_165_824)
                    throw new ArgumentException("Choose a smaller image and try again.");
                Directory.CreateDirectory(DirectoryPath);
                var id = Guid.NewGuid().ToString("N");
                profile.ImageFile = id + "-icon.png"; profile.OriginalImageFile = id + "-source.png";
                foreach (var (file, bytes) in new[] { (profile.ImageFile, cropped), (profile.OriginalImageFile, original) })
                {
                    var path = Path.Combine(DirectoryPath, file); created.Add(path);
                    await File.WriteAllBytesAsync(path, bytes);
                }
            }
            await database.SaveProfileAsync(profile, previousKey);
            created.Clear();
        }
        finally
        {
            foreach (var path in created) TryDelete(path);
            gate.Release();
        }
    }
    public async Task ResetAsync(string key)
    {
        await gate.WaitAsync();
        try { await database.DeleteProfileAsync(key); CleanUnused(await database.ProfilesAsync()); }
        finally { gate.Release(); }
    }
    private void CleanUnused(IEnumerable<AppProfile> profiles)
    {
        try
        {
            if (!Directory.Exists(DirectoryPath)) return;
            var used = profiles.SelectMany(p => new[] { p.ImageFile, p.OriginalImageFile }).Where(f => f is not null).ToHashSet(StringComparer.Ordinal);
            foreach (var path in Directory.EnumerateFiles(DirectoryPath))
                if (AppearanceColors.IsImageFile(Path.GetFileName(path)) && !used.Contains(Path.GetFileName(path))) TryDelete(path);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
    private static void TryDelete(string path) { try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
}
