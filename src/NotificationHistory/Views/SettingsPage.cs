using NotificationHistory.Services;
using NotificationHistory.ViewModels;
namespace NotificationHistory.Views;

public sealed class SettingsPage : ContentPage
{
    private readonly AppServices services;
    private readonly SettingsViewModel vm;
    private readonly Picker retention = new() { Title = "Keep history", ItemsSource = new[] { "Never delete", "7 days", "30 days", "60 days", "90 days" } };
    private readonly Picker appearance = new() { Title = "Appearance", ItemsSource = new[] { "System", "Light", "Dark" } };
    private bool ready;
    public SettingsPage(AppServices services)
    {
        this.services = services; vm = new(services); BindingContext = vm; Title = "Settings";
        var stack = new VerticalStackLayout { Padding = 24, Spacing = 18 };
        stack.Add(new Label { Text = "Storage", FontSize = 24, FontAttributes = FontAttributes.Bold });
        stack.Add(AppearanceUI.Caption("Keep history")); stack.Add(AppearanceUI.Selector(retention));
        stack.Add(new Label { Text = "Favorites are kept during cleanup and when clearing history. Cleanup runs when history opens and whenever a notification is saved.", FontSize = 14 });
        retention.SelectedIndexChanged += async (_, _) => { if (ready && retention.SelectedIndex >= 0) await Run(() => vm.SetRetentionAsync(retention.SelectedIndex)); };
        var clear = new Button { Text = "Clear non-favorites", BackgroundColor = Color.FromArgb("#C43C35") };
        clear.Clicked += async (_, _) => await Run(async () => { if (await AppServices.ConfirmAsync("Clear non-favorites?", "All non-favorite notifications will be permanently removed. Your favorites will be kept.")) await vm.ClearAsync(); }); stack.Add(clear);
        stack.Add(new Label { Text = "Appearance", FontSize = 24, FontAttributes = FontAttributes.Bold });
        stack.Add(AppearanceUI.Caption("Theme")); stack.Add(AppearanceUI.Selector(appearance));
        var apps = new Button { Text = "App photos and colors" };
        apps.Clicked += async (_, _) => await Navigation.PushAsync(new AppsPage(services)); stack.Add(apps);
        var setup = new Button { Text = "Shortcut setup" };
        setup.Clicked += async (_, _) => await Navigation.PushAsync(new OnboardingPage(services)); stack.Add(setup);
        var diagnostics = new Button { Text = "Capture diagnostics" };
        diagnostics.Clicked += async (_, _) => await Navigation.PushAsync(new CaptureDiagnosticsPage(services)); stack.Add(diagnostics);
        appearance.SelectedIndexChanged += async (_, _) => { if (ready && appearance.SelectedItem is string theme) await Run(() => vm.SetAppearanceAsync(theme)); };
        stack.Add(new Label { Text = "Private by design", FontSize = 24, FontAttributes = FontAttributes.Bold });
        stack.Add(new Label { Text = "Your history stays on this device. No cloud sync, analytics, advertising, or external API uploads. Shared history is excluded from device backups. Notification previews may be visible on your Home Screen widget. Copying content places it on the system clipboard, where Universal Clipboard may share it if enabled.", FontSize = 15 });
#if DEBUG
        var debug = new Button { Text = "Development tools" }; debug.Clicked += async (_, _) => await Navigation.PushAsync(new DebugPage(services)); stack.Add(debug);
#endif
        Content = new ScrollView { Content = stack };
    }
    protected override async void OnAppearing()
    {
        base.OnAppearing(); ready = false;
        await Run(async () =>
        {
            var days = await services.Database.SettingAsync("retention");
            retention.SelectedIndex = int.TryParse(days, out var number) ? Math.Max(0, Array.IndexOf(SettingsViewModel.RetentionDays, number)) : 0;
            appearance.SelectedItem = await services.Database.SettingAsync("appearance") ?? "System";
        }); ready = true;
    }
    private static async Task Run(Func<Task> action) { try { await action(); } catch (Exception error) { await AppServices.StorageAlertAsync(error, "Settings unavailable", "Your changes could not be saved. Please try again."); } }
}
