using Microsoft.Maui.Controls.Shapes;
namespace NotificationHistory.Views;

internal static class AppearanceUI
{
    public static void Page(ContentPage page) => page.SetAppThemeColor(VisualElement.BackgroundColorProperty, Color.FromArgb("#F2F2F7"), Color.FromArgb("#0B0B0D"));
    public static void Input(InputView input)
    {
        input.SetAppThemeColor(InputView.TextColorProperty, Color.FromArgb("#1C1C1E"), Colors.White);
        input.SetAppThemeColor(InputView.PlaceholderColorProperty, Color.FromArgb("#62626B"), Color.FromArgb("#A4A4AD"));
        input.SetAppThemeColor(VisualElement.BackgroundColorProperty, Color.FromArgb("#EAEAEE"), Color.FromArgb("#2C2C2E"));
    }
    public static void Foreground(Label label) => label.SetAppThemeColor(Label.TextColorProperty, Color.FromArgb("#1C1C1E"), Colors.White);
    public static Label Text(string value, double size = 15, bool bold = false)
    {
        var label = new Label { Text = value, FontSize = size, FontAttributes = bold ? FontAttributes.Bold : FontAttributes.None };
        Foreground(label); return label;
    }
    public static Label Caption(string value)
    {
        var label = new Label { Text = value, FontSize = 13 };
        label.SetAppThemeColor(Label.TextColorProperty, Color.FromArgb("#62626B"), Color.FromArgb("#A4A4AD")); return label;
    }
    public static Button Button(string value)
    {
        var button = new Button { Text = value, CornerRadius = 22, FontSize = 15, Padding = new Thickness(16, 10) };
        button.SetAppThemeColor(Microsoft.Maui.Controls.Button.TextColorProperty, Color.FromArgb("#1C1C1E"), Colors.White);
        button.SetAppThemeColor(VisualElement.BackgroundColorProperty, Color.FromArgb("#E5E5EA"), Color.FromArgb("#242426")); return button;
    }
    public static Border Panel(View content, Thickness? padding = null)
    {
        var panel = new Border { Content = content, Padding = padding ?? new Thickness(16), StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 20 } };
        panel.SetAppThemeColor(VisualElement.BackgroundColorProperty, Colors.White, Color.FromArgb("#1C1C1E")); return panel;
    }
    public static Border ValueBox(View content, Thickness? padding = null)
    {
        var box = new Border { Content = content, Padding = padding ?? new Thickness(14), MinimumHeightRequest = 52,
            StrokeThickness = 1.5, StrokeShape = new RoundRectangle { CornerRadius = 12 } };
        box.SetAppThemeColor(VisualElement.BackgroundColorProperty, Colors.White, Color.FromArgb("#202024"));
        box.SetAppTheme<Brush>(Border.StrokeProperty, new SolidColorBrush(Color.FromArgb("#B7B7C2")), new SolidColorBrush(Color.FromArgb("#686873"))); return box;
    }
    public static Border Selector(Picker picker)
    {
        picker.SetAppThemeColor(Picker.TextColorProperty, Color.FromArgb("#1C1C1E"), Colors.White);
        picker.SetAppThemeColor(Picker.TitleColorProperty, Color.FromArgb("#62626B"), Color.FromArgb("#A4A4AD"));
        picker.BackgroundColor = Colors.Transparent; picker.MinimumHeightRequest = 48;
        var row = new Grid { ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto) }, ColumnSpacing = 8 };
        row.Add(picker);
        var arrow = Text("⌄", 23, true); arrow.VerticalOptions = LayoutOptions.Center; arrow.InputTransparent = true; row.Add(arrow, 1);
        var box = ValueBox(row, new Thickness(14, 4));
        var tap = new TapGestureRecognizer(); tap.Tapped += (_, _) => picker.Focus(); box.GestureRecognizers.Add(tap);
        SemanticProperties.SetHint(picker, "Tap to choose " + picker.Title.ToLowerInvariant()); return box;
    }
    public static Grid Header(Button left, string title, Button? right = null)
    {
        var header = new Grid { ColumnDefinitions = { new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto) }, ColumnSpacing = 8 };
        header.Add(left);
        var label = Text(title, 18, true); label.HorizontalTextAlignment = TextAlignment.Center; label.VerticalOptions = LayoutOptions.Center;
        header.Add(label, 1); if (right is not null) header.Add(right, 2);
        return header;
    }
}
