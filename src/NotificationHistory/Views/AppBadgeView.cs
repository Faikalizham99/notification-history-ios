using System.ComponentModel;
using Microsoft.Maui.Controls.Shapes;
using NotificationHistory.Core.Services;
using NotificationHistory.ViewModels;
#if IOS
using UIKit;
#endif
namespace NotificationHistory.Views;

public sealed class AppBadgeView : ContentView
{
    private AppBadgeModel? model;
    public event EventHandler? Tapped;
    public event EventHandler? LongPressed;
#if IOS
    private UIView? gestureView;
    private UITapGestureRecognizer? tap;
    private UILongPressGestureRecognizer? hold;
    protected override void OnHandlerChanged()
    {
        base.OnHandlerChanged();
        if (Handler?.PlatformView is not UIView nativeView) return;
        gestureView = nativeView;
        hold = new UILongPressGestureRecognizer(gesture =>
        {
            if (gesture.State == UIGestureRecognizerState.Began) LongPressed?.Invoke(this, EventArgs.Empty);
        }) { MinimumPressDuration = 0.6, AllowableMovement = 10 };
        tap = new UITapGestureRecognizer(() => Tapped?.Invoke(this, EventArgs.Empty));
        // Releasing the hold must not count as the second tap that asks to delete.
        tap.RequireGestureRecognizerToFail(hold);
        nativeView.AddGestureRecognizer(hold); nativeView.AddGestureRecognizer(tap);
    }
    protected override void OnHandlerChanging(HandlerChangingEventArgs args)
    {
        if (tap is not null) { gestureView?.RemoveGestureRecognizer(tap); tap.Dispose(); tap = null; }
        if (hold is not null) { gestureView?.RemoveGestureRecognizer(hold); hold.Dispose(); hold = null; }
        gestureView = null;
        base.OnHandlerChanging(args);
    }
#else
    public AppBadgeView()
    {
        var tap = new TapGestureRecognizer(); tap.Tapped += (_, _) => Tapped?.Invoke(this, EventArgs.Empty);
        GestureRecognizers.Add(tap);
    }
#endif
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
        // Retain the count's layout space while armed so the badge row does not
        // shift or clamp its scroll position when the count changes to X.
        var count = new Grid { MinimumWidthRequest = 20, VerticalOptions = LayoutOptions.Center, InputTransparent = true };
        count.Add(new Label { Text = model.Data.Count.ToString("N0"), FontSize = 17, FontAttributes = FontAttributes.Bold,
            Opacity = model.IsClearArmed ? 0 : 1, TextColor = foreground, VerticalOptions = LayoutOptions.Center });
        count.Add(new Label { Text = "×", FontSize = 22, FontAttributes = FontAttributes.Bold, IsVisible = model.IsClearArmed,
            TextColor = Colors.White, HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center });
        contents.Add(count);
        contents.Add(new Label { Text = !model.IsClearArmed && model.IsSelected ? "✓" : "", WidthRequest = 16, FontSize = 16,
            FontAttributes = FontAttributes.Bold, TextColor = foreground, VerticalOptions = LayoutOptions.Center });
        var pill = new Border { Background = model.IsClearArmed ? new SolidColorBrush(Color.FromArgb("#B42323")) : background,
            StrokeThickness = 0, Padding = new Thickness(12, 8),
            StrokeShape = new RoundRectangle { CornerRadius = 24 }, Content = contents };
        var outline = new Border { Padding = 3, StrokeThickness = 2, MinimumHeightRequest = 58,
            StrokeShape = new RoundRectangle { CornerRadius = 29 }, Content = pill, InputTransparent = true };
        outline.SetAppTheme<Brush>(Border.StrokeProperty,
            new SolidColorBrush(Color.FromArgb(model.IsClearArmed ? "#B42323" : model.IsSelected ? "#086B75" : "#B7B7C2")),
            new SolidColorBrush(Color.FromArgb(model.IsClearArmed ? "#FF8A8A" : model.IsSelected ? "#6BE1E8" : "#686873")));
        Content = outline;
        SemanticProperties.SetDescription(this, model.IsClearArmed ? $"Clear {profile.DisplayName} history, favorites kept" :
            $"{profile.DisplayName}, {model.Data.Count:N0} saved notifications{(model.IsSelected ? ", selected" : "")}");
        SemanticProperties.SetHint(this, model.IsClearArmed ? "Tap to confirm clearing non-favorites. Cancel to keep history." :
            "Tap to show this app's history. Long-press to clear its non-favorites.");
    }
}
