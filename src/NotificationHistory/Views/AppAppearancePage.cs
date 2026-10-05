using Microsoft.Maui.Controls.Shapes;
using NotificationHistory.Core.Models;
using NotificationHistory.Core.Services;
using NotificationHistory.Services;
using NotificationHistory.ViewModels;
namespace NotificationHistory.Views;

public sealed class AppAppearancePage : ContentPage
{
    private readonly AppServices services;
    private readonly AppProfile draft;
    private readonly string? previousKey;
    private readonly NotificationCardView preview = new();
    private readonly Image heroImage = new() { Aspect = Aspect.AspectFill };
    private readonly Label heroInitial = AppearanceUI.Text("?", 34, true);
    private readonly Border hero = new() { WidthRequest = 96, HeightRequest = 96, StrokeThickness = 0 };
    private readonly Button save = AppearanceUI.Button("Save"), cancel = AppearanceUI.Button("Cancel"),
        changeIcon = AppearanceUI.Button("Change Icon  ›"), cropAgain = AppearanceUI.Button("Crop again"), removeIcon = AppearanceUI.Button("Remove icon");
    private readonly Entry name = new() { FontSize = 23, FontAttributes = FontAttributes.Bold, MaxLength = 80, Placeholder = "Display name" };
    private readonly Entry source = new() { MaxLength = 160, Placeholder = "WhatsApp" };
    private readonly Switch gradient = new() { OnColor = Color.FromArgb("#14B8A6") }, automatic = new() { OnColor = Color.FromArgb("#14B8A6") };
    private readonly Picker shape = new() { ItemsSource = new[] { "Rounded", "Circle" }, Title = "Icon shape" };
    private readonly VerticalStackLayout manualColors = new() { Spacing = 14 };
    private readonly ColorField backgroundField, gradientField, titleField, bodyField, timeField;
    private ImageSource? icon;
    private byte[]? originalBytes, croppedBytes;
    private readonly Label progress = AppearanceUI.Caption("Preparing photo…");
    private bool busy, updating;
    public AppAppearancePage(AppServices services, AppProfile profile, bool saved)
    {
        this.services = services; draft = profile.Copy(); previousKey = saved ? profile.SourceKey : null;
        Title = "App Appearance"; AppearanceUI.Page(this); SafeAreaEdges = new(SafeAreaRegions.All);
        AppearanceUI.Input(name); AppearanceUI.Input(source);
        shape.SetAppThemeColor(Picker.TextColorProperty, Color.FromArgb("#1C1C1E"), Colors.White);
        shape.SetAppThemeColor(Picker.TitleColorProperty, Color.FromArgb("#62626B"), Color.FromArgb("#A4A4AD"));
        shape.SetAppThemeColor(VisualElement.BackgroundColorProperty, Color.FromArgb("#EAEAEE"), Color.FromArgb("#2C2C2E"));
        NavigationPage.SetHasNavigationBar(this, false);
        var imagePath = services.Appearance.ImagePath(draft.ImageFile); icon = imagePath is null ? null : ImageSource.FromFile(imagePath);
        var stack = new VerticalStackLayout { Padding = new Thickness(20, 12, 20, 30), Spacing = 22 };
        cancel.Clicked += async (_, _) => { if (!busy) await Navigation.PopAsync(); };
        save.BackgroundColor = Color.FromArgb("#087E8B"); save.TextColor = Colors.White; save.Clicked += OnSave;
        var heading = new Grid { ColumnDefinitions = { new(new GridLength(96)), new(GridLength.Star) }, ColumnSpacing = 18 };
        heroInitial.HorizontalTextAlignment = TextAlignment.Center; heroInitial.VerticalTextAlignment = TextAlignment.Center;
        heading.Add(hero); name.Text = draft.DisplayName; name.TextChanged += (_, _) => { draft.DisplayName = name.Text ?? ""; Render(); }; heading.Add(name, 1);
        stack.Add(heading); changeIcon.HorizontalOptions = LayoutOptions.Fill;
        changeIcon.Clicked += OnPickIcon; stack.Add(changeIcon); progress.IsVisible = false; stack.Add(progress);
        var iconActions = new Grid { ColumnDefinitions = { new(GridLength.Star), new(GridLength.Star) }, ColumnSpacing = 10 };
        cropAgain.Clicked += OnCropAgain; removeIcon.Clicked += (_, _) => { originalBytes = croppedBytes = null; draft.ImageFile = draft.OriginalImageFile = null; icon = null; Render(); };
        iconActions.Add(cropAgain); iconActions.Add(removeIcon, 1); stack.Add(iconActions);
        shape.SelectedItem = draft.IconShape; shape.SelectedIndexChanged += (_, _) => { draft.IconShape = shape.SelectedItem as string ?? "Rounded"; Render(); };
        stack.Add(AppearanceUI.Panel(shape));
        var sourcePanel = new VerticalStackLayout { Spacing = 6 };
        sourcePanel.Add(AppearanceUI.Caption("SOURCE APP")); source.Text = draft.SourceName; source.TextChanged += (_, _) => { draft.SourceName = source.Text ?? ""; };
        sourcePanel.Add(source); sourcePanel.Add(AppearanceUI.Caption("Match the Source App value in your Save Notification Shortcut.")); stack.Add(AppearanceUI.Panel(sourcePanel));
        var previewPanel = new VerticalStackLayout { Spacing = 10 };
        previewPanel.Add(AppearanceUI.Caption("NOTIFICATION PREVIEW")); previewPanel.Add(preview);
        previewPanel.Add(AppearanceUI.Caption("Your appearance applies to existing and new notifications from this source.")); stack.Add(previewPanel);
        var palette = new HorizontalStackLayout { Spacing = 12 };
        foreach (var (label, first, second) in new[] { ("Green", "#075E54", "#128C7E"), ("Pink", "#8B2868", "#B84376"),
            ("Blue", "#282D80", "#39277F"), ("Orange", "#874019", "#BA601E"), ("Charcoal", "#242426", "#171719") })
        {
            var swatch = AppearanceUI.Button(" "); swatch.WidthRequest = swatch.HeightRequest = 40; swatch.Padding = 0; swatch.BackgroundColor = Color.FromArgb(first);
            SemanticProperties.SetDescription(swatch, label + " color preset");
            swatch.Clicked += (_, _) => { draft.BackgroundColor = first; draft.GradientColor = second; draft.AutoTextColor = true; draft.UseGradient = true; UpdateFields(); }; palette.Add(swatch);
        }
        var colors = new VerticalStackLayout { Spacing = 16 };
        colors.Add(AppearanceUI.Text("Color scheme", 22, true)); colors.Add(AppearanceUI.Caption("Choose a preset, tap a color, or enter a hex value.")); colors.Add(palette);
        backgroundField = new(this, "Background", draft.BackgroundColor, value => { draft.BackgroundColor = value; Render(); }); colors.Add(backgroundField.View);
        gradientField = new(this, "Gradient end", draft.GradientColor, value => { draft.GradientColor = value; Render(); });
        gradient.IsToggled = draft.UseGradient; gradient.Toggled += (_, _) => { draft.UseGradient = gradient.IsToggled; gradientField.View.IsVisible = draft.UseGradient; Render(); };
        colors.Add(ToggleRow("Gradient background", gradient)); colors.Add(gradientField.View);
        automatic.IsToggled = draft.AutoTextColor; automatic.Toggled += (_, _) => { draft.AutoTextColor = automatic.IsToggled; manualColors.IsVisible = !draft.AutoTextColor; Render(); };
        colors.Add(ToggleRow("Automatic readable text", automatic));
        titleField = new(this, "App name & title", draft.TitleColor, value => { draft.TitleColor = value; Render(); });
        bodyField = new(this, "Message", draft.BodyColor, value => { draft.BodyColor = value; Render(); });
        timeField = new(this, "Timestamp", draft.TimestampColor, value => { draft.TimestampColor = value; Render(); });
        manualColors.Add(titleField.View); manualColors.Add(bodyField.View); manualColors.Add(timeField.View); colors.Add(manualColors);
        stack.Add(AppearanceUI.Panel(colors));
        var defaults = AppearanceUI.Button("Reset appearance"); defaults.Clicked += async (_, _) =>
        {
            if (busy || !await DisplayAlertAsync("Reset appearance?", "Restore this app's default colors and remove its custom photo from this draft.", "Reset", "Cancel")) return;
            var preset = AppProfile.Default(draft.SourceName); draft.BackgroundColor = preset.BackgroundColor; draft.GradientColor = preset.GradientColor;
            draft.TitleColor = preset.TitleColor; draft.BodyColor = preset.BodyColor; draft.TimestampColor = preset.TimestampColor;
            draft.UseGradient = draft.AutoTextColor = true; draft.ImageFile = draft.OriginalImageFile = null; originalBytes = croppedBytes = null; icon = null;
            draft.IconShape = "Rounded"; UpdateFields();
        }; stack.Add(defaults);
        if (previousKey is not null)
        {
            var removeAppearance = AppearanceUI.Button("Remove custom appearance"); removeAppearance.SetAppThemeColor(Button.TextColorProperty, Color.FromArgb("#B42323"), Color.FromArgb("#FF8A8A"));
            removeAppearance.Clicked += async (_, _) =>
            {
                if (busy || !await DisplayAlertAsync("Remove custom appearance?", "Restore the default appearance for this source. Your notification history will be kept.", "Remove", "Cancel")) return;
                SetBusy(true);
                try { await services.Appearance.ResetAsync(previousKey); services.NotifyChanged(); await Navigation.PopAsync(); }
                catch { await DisplayAlertAsync("Unable to remove appearance", "Please try again.", "OK"); }
                finally { SetBusy(false); }
            }; stack.Add(removeAppearance);
        }
        stack.Add(AppearanceUI.Caption("Photos and appearance settings stay on your iPhone."));
        var layout = new Grid { RowDefinitions = { new(GridLength.Auto), new(GridLength.Star) } };
        var header = AppearanceUI.Header(cancel, "App Appearance", save); header.Margin = new Thickness(20, 12, 20, 0);
        layout.Add(header); layout.Add(new ScrollView { Content = stack }, 0, 1); Content = layout; UpdateFields();
    }
    private static Grid ToggleRow(string title, Switch toggle)
    {
        var row = new Grid { ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto) }, ColumnSpacing = 12 };
        var label = AppearanceUI.Text(title); label.VerticalOptions = LayoutOptions.Center; row.Add(label); row.Add(toggle, 1); return row;
    }
    private void UpdateFields()
    {
        updating = true;
        backgroundField.Set(draft.BackgroundColor); gradientField.Set(draft.GradientColor); titleField.Set(draft.TitleColor); bodyField.Set(draft.BodyColor); timeField.Set(draft.TimestampColor);
        gradient.IsToggled = draft.UseGradient; automatic.IsToggled = draft.AutoTextColor; shape.SelectedItem = draft.IconShape;
        gradientField.View.IsVisible = draft.UseGradient; manualColors.IsVisible = !draft.AutoTextColor;
        updating = false; Render();
    }
    private void Render()
    {
        if (updating) return;
        var previewProfile = draft.Copy(); if (string.IsNullOrWhiteSpace(previewProfile.DisplayName)) previewProfile.DisplayName = "Your app";
        var example = new NotificationEntry { SourceApp = draft.SourceName, Title = "Sample notification",
            Body = "Your messages will appear here. Make this card feel like your app.", ReceivedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() };
        preview.BindingContext = new NotificationCardModel(example, previewProfile, icon);
        hero.BackgroundColor = Color.FromArgb(draft.BackgroundColor); hero.StrokeShape = new RoundRectangle { CornerRadius = draft.IconShape == "Circle" ? 48 : 24 };
        heroInitial.Text = new NotificationCardModel(example, previewProfile, null).Initial;
        heroInitial.TextColor = Color.FromArgb(AppearanceColors.AutomaticText(draft.BackgroundColor));
        heroImage.Source = icon; hero.Content = icon is null ? heroInitial : heroImage;
        cropAgain.IsEnabled = removeIcon.IsEnabled = !busy && icon is not null;
    }
    private void SetBusy(bool value, string message = "Saving…")
    { busy = value; progress.Text = message; progress.IsVisible = value; save.IsEnabled = cancel.IsEnabled = changeIcon.IsEnabled = !value; Render(); }
    private async void OnPickIcon(object? sender, EventArgs e)
    {
        if (busy) return; SetBusy(true, "Preparing photo…");
        try
        {
            var result = await AppearancePicker.PickIconAsync(draft.IconShape);
            if (result is not null) { originalBytes = result.Original; SetCrop(result.Cropped); }
        }
        catch (Exception error) { await DisplayAlertAsync("Photo unavailable", error is ArgumentException ? error.Message : "Could not open this photo. Try another image.", "OK"); }
        finally { SetBusy(false); }
    }
    private async void OnCropAgain(object? sender, EventArgs e)
    {
        if (busy) return; SetBusy(true, "Preparing crop…");
        try
        {
            var path = services.Appearance.ImagePath(draft.OriginalImageFile);
            var original = originalBytes ?? (path is null ? null : await File.ReadAllBytesAsync(path));
            if (original is null) { await DisplayAlertAsync("Choose a photo", "Choose the photo again to adjust its crop.", "OK"); return; }
            var result = await AppearancePicker.CropExistingAsync(original, draft.IconShape);
            if (result is not null) { originalBytes = original; SetCrop(result); }
        }
        catch { await DisplayAlertAsync("Crop unavailable", "Choose the photo again and try another crop.", "OK"); }
        finally { SetBusy(false); }
    }
    private void SetCrop(byte[] bytes)
    { croppedBytes = bytes; icon = ImageSource.FromStream(() => new MemoryStream(bytes, false)); Render(); }
    private async void OnSave(object? sender, EventArgs e)
    {
        if (busy) return; SetBusy(true);
        try
        {
            backgroundField.Apply(); if (draft.UseGradient) gradientField.Apply();
            if (!draft.AutoTextColor) { titleField.Apply(); bodyField.Apply(); timeField.Apply(); }
            draft.SourceName = source.Text ?? ""; draft.DisplayName = name.Text ?? "";
            await services.Appearance.SaveAsync(draft, previousKey, originalBytes, croppedBytes);
            services.NotifyChanged(); await Navigation.PopAsync();
        }
        catch (Exception error) { await DisplayAlertAsync("Could not save appearance", error is ArgumentException ? error.Message : "Check device storage and try again. Your existing appearance is unchanged.", "OK"); }
        finally { SetBusy(false); }
    }
    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        if (!Navigation.NavigationStack.Contains(this)) { originalBytes = croppedBytes = null; icon = null; heroImage.Source = null; preview.BindingContext = null; }
    }
    private sealed class ColorField
    {
        private readonly Entry hex;
        private readonly Button swatch;
        private readonly Action<string> changed;
        private string current;
        public Grid View { get; }
        public ColorField(ContentPage owner, string title, string initial, Action<string> changed)
        {
            this.changed = changed; current = initial;
            View = new Grid { ColumnDefinitions = { new(GridLength.Star), new(new GridLength(98)), new(new GridLength(44)) }, ColumnSpacing = 8 };
            var label = AppearanceUI.Text(title, 14); label.VerticalOptions = LayoutOptions.Center; View.Add(label);
            hex = new Entry { Text = initial, MaxLength = 7, FontSize = 14, FontFamily = "Menlo", HorizontalTextAlignment = TextAlignment.End };
            AppearanceUI.Input(hex);
            hex.TextChanged += (_, _) =>
            {
                if (AppearanceColors.TryNormalize(hex.Text, out var value)) { current = value; swatch!.BackgroundColor = Color.FromArgb(value); AppearanceUI.Input(hex); changed(value); }
                else hex.SetAppThemeColor(InputView.TextColorProperty, Color.FromArgb("#B42323"), Color.FromArgb("#FF8A8A"));
            }; View.Add(hex, 1);
            swatch = AppearanceUI.Button(" "); swatch.HeightRequest = 40; swatch.CornerRadius = 20; swatch.Padding = 0; swatch.BackgroundColor = Color.FromArgb(initial);
            SemanticProperties.SetDescription(swatch, "Choose " + title.ToLowerInvariant() + " color");
            swatch.Clicked += async (_, _) =>
            {
                try { await owner.Navigation.PushModalAsync(new ColorWheelPage(title, current, value => hex.Text = value)); }
                catch { await AppServices.AlertAsync("Color picker unavailable", "Please try again."); }
            }; View.Add(swatch, 2);
        }
        public void Set(string value) { current = value; hex.Text = value; swatch.BackgroundColor = Color.FromArgb(value); }
        public void Apply()
        { if (!AppearanceColors.TryNormalize(hex.Text, out var value)) throw new ArgumentException("Complete the hex color values before saving."); changed(value); }
    }
}
