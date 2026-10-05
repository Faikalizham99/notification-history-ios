using Microsoft.Maui.Controls.Shapes;
using NotificationHistory.Core.Models;
using NotificationHistory.Services;
namespace NotificationHistory.Views;

public sealed class AppsPage : ContentPage
{
    private readonly AppServices services;
    private readonly VerticalStackLayout rows = new() { Spacing = 12 };
    private readonly ActivityIndicator loading = new() { Color = Color.FromArgb("#087E8B") };
    private bool busy;
    public AppsPage(AppServices services)
    {
        this.services = services; Title = "Apps"; AppearanceUI.Page(this);
        SafeAreaEdges = new(SafeAreaRegions.All); NavigationPage.SetHasNavigationBar(this, false);
        var back = AppearanceUI.Button("‹ Back"); back.Clicked += async (_, _) => await Navigation.PopAsync();
        var add = AppearanceUI.Button("+ Add"); add.Clicked += OnAdd;
        var stack = new VerticalStackLayout { Padding = new Thickness(20, 12, 20, 24), Spacing = 20 };
        stack.Add(AppearanceUI.Text("Make it your own", 28, true));
        stack.Add(AppearanceUI.Caption("Choose a photo and colors for each app. Apps from your history appear here automatically."));
        stack.Add(loading); stack.Add(rows);
        var layout = new Grid { RowDefinitions = { new(GridLength.Auto), new(GridLength.Star) } };
        var header = AppearanceUI.Header(back, "Apps", add); header.Margin = new Thickness(20, 12, 20, 0);
        layout.Add(header); layout.Add(new ScrollView { Content = stack }, 0, 1); Content = layout;
    }
    protected override async void OnAppearing()
    {
        base.OnAppearing(); if (busy) return; busy = true; loading.IsRunning = loading.IsVisible = true;
        try
        {
            var saved = (await services.Appearance.LoadAsync()).ToDictionary(p => p.SourceKey, StringComparer.Ordinal);
            var profiles = new Dictionary<string, AppProfile>(saved, StringComparer.Ordinal);
            foreach (var source in await services.Database.SourcesAsync())
                profiles.TryAdd(AppProfile.Key(source), AppProfile.Default(source));
            rows.Clear();
            if (profiles.Count == 0) rows.Add(AppearanceUI.Caption("No apps yet. Add your first app, or capture a notification through your Shortcut."));
            foreach (var profile in profiles.Values.OrderBy(p => p.DisplayName, StringComparer.CurrentCultureIgnoreCase))
            {
                var grid = new Grid { ColumnDefinitions = { new(new GridLength(64)), new(GridLength.Star), new(GridLength.Auto) }, ColumnSpacing = 14 };
                var image = services.Appearance.ImagePath(profile.ImageFile);
                var icon = new Border { HeightRequest = 64, WidthRequest = 64, StrokeThickness = 0,
                    BackgroundColor = Color.FromArgb(profile.BackgroundColor), StrokeShape = new RoundRectangle { CornerRadius = profile.IconShape == "Circle" ? 32 : 17 } };
                icon.Content = image is null ? AppearanceUI.Text(new System.Globalization.StringInfo(profile.DisplayName).SubstringByTextElements(0, 1).ToUpperInvariant(), 28, true)
                    : new Image { Source = image, Aspect = Aspect.AspectFill };
                if (icon.Content is Label initial) { initial.HorizontalTextAlignment = TextAlignment.Center; initial.VerticalTextAlignment = TextAlignment.Center;
                    initial.TextColor = Color.FromArgb(NotificationHistory.Core.Services.AppearanceColors.AutomaticText(profile.BackgroundColor)); }
                grid.Add(icon);
                var labels = new VerticalStackLayout { VerticalOptions = LayoutOptions.Center, Spacing = 4 };
                var name = AppearanceUI.Text(profile.DisplayName, 19, true); name.MaxLines = 1; name.LineBreakMode = LineBreakMode.TailTruncation;
                labels.Add(name); labels.Add(AppearanceUI.Caption(saved.ContainsKey(profile.SourceKey) ? "Custom appearance" : "Choose photo & colors"));
                grid.Add(labels, 1); var chevron = AppearanceUI.Caption("›"); chevron.VerticalOptions = LayoutOptions.Center; chevron.FontSize = 24; grid.Add(chevron, 2);
                var row = AppearanceUI.ValueBox(grid, new Thickness(16));
                var tap = new TapGestureRecognizer(); tap.Tapped += async (_, _) => await OpenAsync(profile, saved.ContainsKey(profile.SourceKey)); row.GestureRecognizers.Add(tap);
                SemanticProperties.SetDescription(row, "Edit appearance for " + profile.DisplayName); rows.Add(row);
            }
        }
        catch (Exception error) { await AppServices.StorageAlertAsync(error, "Apps unavailable", "Could not read your apps. Try again."); }
        finally { busy = false; loading.IsRunning = loading.IsVisible = false; }
    }
    private async void OnAdd(object? sender, EventArgs e)
    {
        var source = await DisplayPromptAsync("Add app", "Enter the Source App name you use in Save Notification.", "Continue", "Cancel", "WhatsApp", 160);
        if (!string.IsNullOrWhiteSpace(source))
        {
            try
            {
                var existing = (await services.Appearance.LoadAsync()).FirstOrDefault(p => p.SourceKey == AppProfile.Key(source));
                await OpenAsync(existing ?? AppProfile.Default(source), existing is not null);
            }
            catch { await AppServices.AlertAsync("Unable to add app", "Please try again."); }
        }
    }
    private async Task OpenAsync(AppProfile profile, bool saved)
    { if (!busy) await Navigation.PushAsync(new AppAppearancePage(services, profile, saved)); }
}
