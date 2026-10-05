using Microsoft.Maui.Controls.Shapes;
using NotificationHistory.Core.Services;
using NotificationHistory.ViewModels;
namespace NotificationHistory.Views;

// This is used unchanged by both history rows and the live appearance preview.
public sealed class NotificationCardView : ContentView
{
    protected override void OnBindingContextChanged()
    {
        base.OnBindingContextChanged();
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
            text.Add(new Label { Text = card.Entry.Preview, FontSize = 16, TextColor = bodyColor, MaxLines = 3,
                LineBreakMode = LineBreakMode.TailTruncation, AutomationId = "CardMessage" });
        grid.Add(text, 1);
        Content = new Border { Padding = 14, StrokeThickness = 0, Background = background,
            StrokeShape = new RoundRectangle { CornerRadius = 20 }, Content = grid };
    }
}
