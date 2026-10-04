using NotificationHistory.Core.Data;
using NotificationHistory.Core.Services;
using NotificationHistory.Views;
namespace NotificationHistory.Services;

public sealed class AppServices(NotificationDatabase database)
{
    public NotificationDatabase Database { get; } = database;
    public event EventHandler? Changed;
    private string? pendingLink;
    private bool navigationReady;
    public void MarkNavigationReady() => navigationReady = true;
    public void NotifyChanged()
    {
        Changed?.Invoke(this, EventArgs.Empty);
#if IOS
        WidgetRefresh.Reload();
#endif
    }
    public async Task RefreshAsync()
    {
        try { await Database.CleanupAsync(); NotifyChanged(); }
        catch (Exception error) { await StorageAlertAsync(error, "Storage unavailable", "History could not be refreshed. Check device storage and App Group signing."); }
    }
    public static Task AlertAsync(string title, string message) => Application.Current!.Windows[0].Page!.DisplayAlertAsync(title, message, "OK");
    public static Task StorageAlertAsync(Exception error, string title, string fallback) =>
        error is SharedStorageConfigurationException
            ? AlertAsync("Shared storage signing", error.Message)
            : AlertAsync(title, fallback);
    public static Task<bool> ConfirmAsync(string title, string message) => Application.Current!.Windows[0].Page!.DisplayAlertAsync(title, message, "Delete", "Cancel");
    public async Task ApplyAppearanceAsync()
    {
        var theme = await Database.SettingAsync("appearance");
        Application.Current!.UserAppTheme = theme switch { "Light" => AppTheme.Light, "Dark" => AppTheme.Dark, _ => AppTheme.Unspecified };
    }
    public void OpenLink(string link) { pendingLink = link; MainThread.BeginInvokeOnMainThread(async () => await ConsumeLinkAsync()); }
    public async Task ConsumeLinkAsync()
    {
        if (!navigationReady || pendingLink is null || Application.Current?.Windows.Count is not > 0) return;
        var link = pendingLink; pendingLink = null;
        if (Uri.TryCreate(link, UriKind.Absolute, out var home) && home.Scheme == "notificationhistory" && home.Host == "history")
        { await Application.Current!.Windows[0].Page!.Navigation.PopToRootAsync(); return; }
        if (!DeepLinks.TryParse(link, out var id)) { await AlertAsync("Invalid link", "This notification link is invalid."); return; }
        await OpenDetailAsync(id);
    }
    public async Task OpenDetailAsync(long id)
    {
        try
        {
            var entry = await Database.GetAsync(id);
            if (entry is null) { await AlertAsync("Notification unavailable", "It may have been deleted or removed by retention."); return; }
            await Application.Current!.Windows[0].Page!.Navigation.PushAsync(new NotificationDetailPage(this, entry));
        }
        catch { await AlertAsync("Unable to open", "Shared history is currently unavailable. Please try again."); }
    }
}
