using Microsoft.Maui.Controls.Shapes;
namespace NotificationHistory.Views;

internal static class AppearanceUI
{
    public static Label Text(string value, double size = 15, bool bold = false) => new() {
        Text = value, FontSize = size, TextColor = Colors.White, FontAttributes = bold ? FontAttributes.Bold : FontAttributes.None };
    public static Label Caption(string value) => new() { Text = value, FontSize = 13, TextColor = Color.FromArgb("#A4A4AD") };
    public static Button Button(string value) => new() { Text = value, TextColor = Colors.White,
        BackgroundColor = Color.FromArgb("#242426"), CornerRadius = 22, FontSize = 15, Padding = new Thickness(16, 10) };
    public static Border Panel(View content, Thickness? padding = null) => new() { Content = content,
        Padding = padding ?? new Thickness(16), StrokeThickness = 0, BackgroundColor = Color.FromArgb("#1C1C1E"),
        StrokeShape = new RoundRectangle { CornerRadius = 20 } };
    public static Grid Header(Button left, string title, Button? right = null)
    {
        var header = new Grid { ColumnDefinitions = { new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto) }, ColumnSpacing = 8 };
        header.Add(left);
        var label = Text(title, 18, true); label.HorizontalTextAlignment = TextAlignment.Center; label.VerticalOptions = LayoutOptions.Center;
        header.Add(label, 1); if (right is not null) header.Add(right, 2);
        return header;
    }
}
