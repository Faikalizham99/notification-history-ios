using System.ComponentModel;
using Microsoft.Maui.Controls.Shapes;
using NotificationHistory.Core.Services;
using NotificationHistory.ViewModels;
namespace NotificationHistory.Views;

public sealed class AppBadgeView : ContentView
{
    private AppBadgeModel? model;
    protected override void OnBindingContextChanged()
    {
        if (model is not null) model.PropertyChanged -= OnModelChanged;
        base.OnBindingContextChanged();
        model = BindingContext as AppBadgeModel;
        if (model is not null) model.PropertyChanged += OnModelChanged;
        Render();
    }
    private void OnModelChanged(object? sender, PropertyChangedEventArgs e) => Render();
    private void Render()
    {
        if (model is null) { Content = null; return; }
        var profile = model.Data.Profile;
        var foreground = Color.FromArgb(AppearanceColors.AutomaticText(profile.BackgroundColor,
            profile.UseGradient ? profile.GradientColor : null));
        Brush background = profile.UseGradient
            ? new LinearGradientBrush(new GradientStopCollection { new(Color.FromArgb(profile.BackgroundColor), 0), new(Color.FromArgb(profile.GradientColor), 1) }, new(0, 0), new(1, 1))
            : new SolidColorBrush(Color.FromArgb(profile.BackgroundColor));
        var icon = new Border { WidthRequest = 32, HeightRequest = 32, StrokeThickness = 0,
            BackgroundColor = Color.FromArgb("#26FFFFFF"), StrokeShape = new RoundRectangle { CornerRadius = profile.IconShape == "Circle" ? 16 : 8 } };
        icon.Content = model.Icon is not null ? new Image { Source = model.Icon, Aspect = Aspect.AspectFill }
            : new Label { Text = model.Initial, FontSize = 18, FontAttributes = FontAttributes.Bold, TextColor = foreground,
                HorizontalTextAlignment = TextAlignment.Center, VerticalTextAlignment = TextAlignment.Center };
        var contents = new HorizontalStackLayout { Spacing = 8, VerticalOptions = LayoutOptions.Center };
        contents.Add(icon);
        contents.Add(new Label { Text = model.Data.Count.ToString("N0"), FontSize = 17, FontAttributes = FontAttributes.Bold,
            TextColor = foreground, VerticalOptions = LayoutOptions.Center });
        contents.Add(new Label { Text = model.IsSelected ? "✓" : "", WidthRequest = 16, FontSize = 16,
            FontAttributes = FontAttributes.Bold, TextColor = foreground, VerticalOptions = LayoutOptions.Center });
        var pill = new Border { Background = background, StrokeThickness = 0, Padding = new Thickness(12, 8),
            StrokeShape = new RoundRectangle { CornerRadius = 24 }, Content = contents };
        var outline = new Border { Padding = 3, StrokeThickness = 2, MinimumHeightRequest = 58,
            StrokeShape = new RoundRectangle { CornerRadius = 29 }, Content = pill, InputTransparent = true };
        outline.SetAppTheme<Brush>(Border.StrokeProperty,
            new SolidColorBrush(Color.FromArgb(model.IsSelected ? "#086B75" : "#B7B7C2")),
            new SolidColorBrush(Color.FromArgb(model.IsSelected ? "#6BE1E8" : "#686873")));
        Content = outline;
        SemanticProperties.SetDescription(this, $"{profile.DisplayName}, {model.Data.Count:N0} saved notifications{(model.IsSelected ? ", selected" : "")}");
        SemanticProperties.SetHint(this, "Tap to show this app's history.");
    }
}
