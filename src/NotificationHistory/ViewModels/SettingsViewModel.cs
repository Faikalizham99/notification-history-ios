using NotificationHistory.Services;
namespace NotificationHistory.ViewModels;

public sealed class SettingsViewModel(AppServices services)
{
    public static int[] RetentionDays { get; } = [0, 7, 30, 60, 90];
    public async Task SetRetentionAsync(int index)
    {
        await services.Database.SetSettingAsync("retention", RetentionDays[index].ToString());
        await services.Database.CleanupAsync(); services.NotifyChanged();
    }
    public async Task SetAppearanceAsync(string theme)
    {
        await services.Database.SetSettingAsync("appearance", theme); await services.ApplyAppearanceAsync();
    }
    public async Task ClearAsync() { await services.Database.ClearAsync(); services.NotifyChanged(); }
}
