using Microsoft.Maui.Controls.Shapes;
using NotificationHistory.Core.Models;
using NotificationHistory.Core.Services;
using NotificationHistory.Services;
using NotificationHistory.ViewModels;
namespace NotificationHistory.Views;

// This is used unchanged by both history rows and the live appearance preview.
public sealed class NotificationCardView : ContentView
{
    private Label? message;

    protected override void OnBindingContextChanged()
    {
        base.OnBindingContextChanged();
        if (message is not null)
        {
            message.SizeChanged -= OnMessageLayoutChanged;
            message.HandlerChanged -= OnMessageLayoutChanged;
        }
        message = null;
        if (BindingContext is not NotificationCardModel card) { Content = null; return; }
        var profile = card.Profile;
        var automatic = AppearanceColors.AutomaticText(profile.BackgroundColor, profile.UseGradient ? profile.GradientColor : null);
        var titleColor = Color.FromArgb(profile.AutoTextColor ? automatic : profile.TitleColor);
        var bodyColor = Color.FromArgb(profile.AutoTextColor ? automatic : profile.BodyColor);
        var timeColor = Color.FromArgb(profile.AutoTextColor ? automatic : profile.TimestampColor);
        Brush background = profile.UseGradient
            ? new LinearGradientBrush(new GradientStopCollection { new(Color.FromArgb(profile.BackgroundColor), 0), new(Color.FromArgb(profile.GradientColor), 1) }, new(0, 0), new(1, 1))
            : new SolidColorBrush(Color.FromArgb(profile.BackgroundColor));
        var grid = new Grid { ColumnDefinitions = { new(new GridLength(60)), new(GridLength.Star) }, ColumnSpacing = 12 };
        var icon = new Border { WidthRequest = 60, HeightRequest = 60, VerticalOptions = LayoutOptions.Start,
            BackgroundColor = Color.FromArgb("#26FFFFFF"), StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = profile.IconShape == "Circle" ? 30 : 16 } };
        icon.Content = card.Icon is not null ? new Image { Source = card.Icon, Aspect = Aspect.AspectFill }
            : new Label { Text = card.Initial, FontSize = 27, FontAttributes = FontAttributes.Bold,
                TextColor = titleColor, HorizontalTextAlignment = TextAlignment.Center, VerticalTextAlignment = TextAlignment.Center };
        SemanticProperties.SetDescription(icon, profile.DisplayName + " icon");
        grid.Add(icon);
        var text = new VerticalStackLayout { Spacing = 4 };
        var heading = new Grid { ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto) }, ColumnSpacing = 8 };
        heading.Add(new Label { Text = profile.DisplayName.ToUpperInvariant(), FontSize = 15, TextColor = titleColor,
            MaxLines = 1, LineBreakMode = LineBreakMode.TailTruncation, AutomationId = "CardAppName" });
        heading.Add(new Label { Text = card.Entry.FavoriteDisplay + (card.Entry.IsFavorite ? " " : "") + card.Entry.TimeDisplay,
            FontSize = 12, TextColor = timeColor, VerticalOptions = LayoutOptions.Center, AutomationId = "CardTime" }, 1);
        text.Add(heading);
        text.Add(new Label { Text = card.Entry.TitleDisplay, FontSize = 17, FontAttributes = FontAttributes.Bold,
            TextColor = titleColor, MaxLines = 2, LineBreakMode = LineBreakMode.TailTruncation, AutomationId = "CardTitle" });
        var hasSubtitle = !string.IsNullOrWhiteSpace(card.Entry.Subtitle);
        if (hasSubtitle)
        {
            var subtitle = new Label { Text = card.Entry.Subtitle, FontSize = 13, FontAttributes = FontAttributes.Bold,
                TextColor = bodyColor, MaxLines = 2, LineBreakMode = LineBreakMode.TailTruncation, AutomationId = "CardSubtitle" };
            SemanticProperties.SetDescription(subtitle, "Subtitle: " + card.Entry.Subtitle);
            text.Add(new Border { Content = subtitle, HorizontalOptions = LayoutOptions.Start, Padding = new Thickness(8, 3),
                BackgroundColor = bodyColor.WithAlpha(.10f), Stroke = new SolidColorBrush(bodyColor.WithAlpha(.25f)),
                StrokeThickness = 1, StrokeShape = new RoundRectangle { CornerRadius = 8 } });
        }
        // Preview falls back to the subtitle for other consumers; don't repeat it on this card.
        if (!string.IsNullOrWhiteSpace(card.Entry.Body) || !hasSubtitle)
        {
            message = new Label { FontSize = 16, TextColor = bodyColor, AutomationId = "CardMessage" };
            ApplyMessageState(card);
            message.SizeChanged += OnMessageLayoutChanged;
            message.HandlerChanged += OnMessageLayoutChanged;
            text.Add(message);
        }
        grid.Add(text, 1);
        Content = new Border { Padding = 14, StrokeThickness = 0, Background = background,
            StrokeShape = new RoundRectangle { CornerRadius = 20 }, Content = grid };
    }

    // Route body and detail taps through the existing card gesture. Separate child
    // gestures can compete with the card tap and swipe-to-delete on iOS.
    public async Task<bool> TryToggleBodyAsync(TappedEventArgs tap, Func<long, Task<NotificationEntry?>> load)
    {
        if (message is not { } label || BindingContext is not NotificationCardModel card ||
            string.IsNullOrWhiteSpace(card.Entry.Body)) return false;
        var position = tap.GetPosition(label);
        if (position is not { } point || point.X < 0 || point.Y < 0 ||
            point.X > label.Width || point.Y > label.Height ||
            card.ExpandedBody is null && !IsMessageTruncated(card)) return false;

        if (card.ExpandedBody is not null)
        {
            card.ExpandedBody = null;
            ApplyMessageState(card);
            SemanticScreenReader.Announce("Message collapsed");
            return true;
        }

        var fullEntry = card.Entry.Id > 0 ? await load(card.Entry.Id) : card.Entry;
        // A filter change or reload may recycle the view while the read is pending.
        if (!ReferenceEquals(BindingContext, card) || !ReferenceEquals(message, label)) return true;
        if (fullEntry is null)
        {
            await AppServices.AlertAsync("Notification unavailable", "It may have been deleted or removed by retention.");
            return true;
        }
        if (string.IsNullOrWhiteSpace(fullEntry.Body)) return true;
        card.ExpandedBody = fullEntry.Body;
        ApplyMessageState(card);
        SemanticScreenReader.Announce("Message expanded");
        return true;
    }

    private bool IsMessageTruncated(NotificationCardModel card)
    {
        if (message is null || string.IsNullOrWhiteSpace(card.Entry.Body)) return false;
        // This detects our shortened preview, not a literal ellipsis in the sender's message.
        if (!string.Equals(card.Entry.Body, card.Entry.Preview, StringComparison.Ordinal)) return true;
#if IOS
        if (message.Width > 0 && message.Height > 0 && message.Handler?.PlatformView is UIKit.UILabel native)
        {
            var fullBounds = native.TextRectForBounds(new CoreGraphics.CGRect(0, 0, message.Width, 100000), IntPtr.Zero);
            return fullBounds.Height > message.Height + 0.5;
        }
#endif
        return false;
    }

    private void ApplyMessageState(NotificationCardModel card)
    {
        if (message is null) return;
        var expanded = card.ExpandedBody is not null;
        message.Text = card.ExpandedBody ?? card.Entry.Preview;
        message.MaxLines = expanded ? -1 : 3;
        message.LineBreakMode = expanded ? LineBreakMode.WordWrap : LineBreakMode.TailTruncation;
        UpdateMessageHint(card);
    }

    private void OnMessageLayoutChanged(object? sender, EventArgs e)
    {
        if (BindingContext is NotificationCardModel card) UpdateMessageHint(card);
    }

    private void UpdateMessageHint(NotificationCardModel card)
    {
        if (message is not null)
            SemanticProperties.SetHint(message, card.ExpandedBody is not null ? "Tap message to collapse." :
                IsMessageTruncated(card) ? "Tap message to expand." : "");
    }
}
