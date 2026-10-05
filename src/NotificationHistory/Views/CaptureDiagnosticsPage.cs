using NotificationHistory.Core.Models;
using NotificationHistory.Core.Services;
using NotificationHistory.Services;
namespace NotificationHistory.Views;

public sealed class CaptureDiagnosticsPage : ContentPage
{
    private readonly CaptureDiagnosticsStore store;
    private readonly VerticalStackLayout rows = new() { Spacing = 12 };
    private readonly Label status = AppearanceUI.Caption("");
    private readonly Button refresh = new() { Text = "Refresh" }, copy = new() { Text = "Copy diagnostics" }, clear = AppearanceUI.Button("Clear diagnostics");
    private List<CaptureAttempt> attempts = [];
    private bool busy;
    public CaptureDiagnosticsPage(AppServices services)
    {
        store = new(() => services.Database.Path); Title = "Capture diagnostics"; AppearanceUI.Page(this);
        var stack = new VerticalStackLayout { Padding = 24, Spacing = 16 };
        stack.Add(AppearanceUI.Text("Check a missed capture", 25, true));
        stack.Add(AppearanceUI.Text("After a Shortcuts error, refresh and compare its time with these attempts. A saved attempt means the database write completed."));
        stack.Add(AppearanceUI.Caption("No matching attempt can mean the action never reached our code, its inputs could not resolve, or diagnostics were unavailable. An unfinished attempt may still be running or may have been interrupted."));
        stack.Add(AppearanceUI.Caption("Only times, execution stages, and error codes are kept locally. Message content, filenames, sender names, and source apps are excluded. Shows the latest 50 attempts."));
        refresh.Clicked += async (_, _) => await LoadAsync();
        copy.Clicked += async (_, _) =>
        {
            try { await Clipboard.SetTextAsync(CaptureDiagnosticsStore.Report(attempts)); SemanticScreenReader.Announce("Diagnostics copied"); }
            catch { await AppServices.AlertAsync("Unable to copy", "Please try again."); }
        };
        clear.Clicked += async (_, _) =>
        {
            if (busy || !await DisplayAlertAsync("Clear diagnostics?", "This removes capture diagnostic records. Your notification history remains available. Active attempts may write a new diagnostic record.", "Clear", "Cancel")) return;
            try { await store.ClearAsync(); await LoadAsync(); }
            catch { await AppServices.AlertAsync("Unable to clear diagnostics", "Please try again."); }
        };
        stack.Add(refresh); stack.Add(copy); stack.Add(clear); stack.Add(status); stack.Add(rows);
        Content = new ScrollView { Content = stack };
    }
    protected override async void OnAppearing() { base.OnAppearing(); await LoadAsync(); }
    private async Task LoadAsync()
    {
        if (busy) return;
        busy = true; refresh.IsEnabled = copy.IsEnabled = clear.IsEnabled = false;
        try
        {
            attempts = await store.ReadAsync(); rows.Clear();
            status.Text = attempts.Count == 0 ? "No recorded attempts yet. Run Save Notification, then refresh." : $"{attempts.Count} recent attempts · Times follow your device time zone";
            foreach (var attempt in attempts)
            {
                var row = new VerticalStackLayout { Spacing = 6 };
                var heading = AppearanceUI.Text(attempt.Status, 18, true);
                if (attempt.Outcome == "failed") heading.SetAppThemeColor(Label.TextColorProperty, Color.FromArgb("#A42B25"), Color.FromArgb("#FF9A94"));
                row.Add(heading);
                var started = DateTimeOffset.FromUnixTimeMilliseconds(attempt.StartedAt).ToLocalTime();
                row.Add(AppearanceUI.Text(started.ToString("d MMM yyyy") + " · " + NotificationTimes.Detail(started), 14));
                row.Add(AppearanceUI.Text("Last stage: " + attempt.StageDisplay));
                if (attempt.ErrorDisplay is not null)
                    row.Add(AppearanceUI.Text(attempt.ErrorDisplay + (attempt.PlatformCode is int code ? $" (code {code})" : "")));
                row.Add(AppearanceUI.Caption("Attempt " + attempt.Id[..8]));
                rows.Add(AppearanceUI.Panel(row));
            }
        }
        catch (Exception error)
        {
            attempts = []; rows.Clear(); status.Text = "Diagnostics could not be read. This does not indicate whether the action ran.";
            await AppServices.StorageAlertAsync(error, "Diagnostics unavailable", "Check shared-storage access and try again.");
        }
        finally { busy = false; refresh.IsEnabled = clear.IsEnabled = true; copy.IsEnabled = attempts.Count > 0; }
    }
}
