using NotificationHistory.Core.Services;
using Microsoft.Maui.Graphics;
namespace NotificationHistory.Views;

public sealed class ColorWheelPage : ContentPage
{
    private readonly WheelDrawable wheel = new();
    private readonly GraphicsView canvas;
    private readonly Slider brightness = new() { Minimum = 0, Maximum = 1, MinimumTrackColor = Color.FromArgb("#087E8B") };
    private readonly Entry hex = new() { MaxLength = 7, FontFamily = "Menlo", HorizontalTextAlignment = TextAlignment.Center };
    private readonly Label brightnessLabel = AppearanceUI.Text("Brightness");
    private readonly Border sample;
    private readonly Action<string> changed;
    private readonly string initial;
    private readonly Button use = AppearanceUI.Button("Use");
    private bool updating, closing, accepted, selecting;
    public ColorWheelPage(string title, string initial, Action<string> changed)
    {
        this.initial = AppearanceColors.Normalize(initial); this.changed = changed;
        Title = title; AppearanceUI.Page(this); SafeAreaEdges = new(SafeAreaRegions.All);
        Microsoft.Maui.Controls.PlatformConfiguration.iOSSpecific.Page.SetModalPresentationStyle(this.On<Microsoft.Maui.Controls.PlatformConfiguration.iOS>(),
            Microsoft.Maui.Controls.PlatformConfiguration.iOSSpecific.UIModalPresentationStyle.FullScreen);
        var cancel = AppearanceUI.Button("Cancel"); cancel.Clicked += async (_, _) => await CloseAsync(false);
        use.BackgroundColor = Color.FromArgb("#087E8B"); use.TextColor = Colors.White;
        use.Clicked += async (_, _) => { if (AppearanceColors.TryNormalize(hex.Text, out _)) await CloseAsync(true); };
        canvas = new GraphicsView { Drawable = wheel, MaximumHeightRequest = 300, HorizontalOptions = LayoutOptions.Fill, VerticalOptions = LayoutOptions.Fill };
        SemanticProperties.SetDescription(canvas, "Color wheel. Drag to choose hue and saturation, or enter a hex color below.");
        canvas.StartInteraction += (_, e) =>
        {
            if (e.Touches.Length == 0) return;
            var radius = Math.Min(canvas.Width, canvas.Height) / 2 - 12;
            var x = e.Touches[0].X - canvas.Width / 2; var y = e.Touches[0].Y - canvas.Height / 2;
            selecting = radius > 0 && x * x + y * y <= radius * radius;
            if (selecting) SelectColor(canvas, e);
        };
        canvas.DragInteraction += (_, e) => { if (selecting) SelectColor(canvas, e); };
        canvas.EndInteraction += (_, _) => selecting = false; canvas.CancelInteraction += (_, _) => selecting = false;
        brightness.ValueChanged += (_, _) => { if (!updating) { wheel.Value = brightness.Value; Refresh(); } };
        SemanticProperties.SetDescription(brightness, "Color brightness");
        AppearanceUI.Input(hex); SemanticProperties.SetDescription(hex, "Hex color");
        hex.TextChanged += (_, _) =>
        {
            if (updating) return;
            if (AppearanceColors.TryNormalize(hex.Text, out var value)) { SetColor(value, false); use.IsEnabled = true; }
            else { use.IsEnabled = false; hex.SetAppThemeColor(InputView.TextColorProperty, Color.FromArgb("#B42323"), Color.FromArgb("#FF8A8A")); }
        };
        sample = AppearanceUI.Panel(new Label { Text = "Selected color", HorizontalTextAlignment = TextAlignment.Center }, new Thickness(18));
        var stack = new VerticalStackLayout { Padding = new Thickness(24, 12, 24, 24), Spacing = 18 };
        stack.Add(brightnessLabel); stack.Add(brightness); stack.Add(sample);
        stack.Add(AppearanceUI.Caption("HEX COLOR")); stack.Add(AppearanceUI.EditField(hex));
        stack.Add(AppearanceUI.Caption("The notification preview updates while you choose. Cancel restores the previous color."));
        // Keep the wheel outside the ScrollView so a vertical color drag cannot turn into page scrolling.
        var layout = new Grid { RowDefinitions = { new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Star), new(GridLength.Star) }, RowSpacing = 10 };
        var header = AppearanceUI.Header(cancel, title, use); header.Margin = new Thickness(20, 12, 20, 0);
        var instruction = AppearanceUI.Caption("Drag to choose a color. Adjust brightness below."); instruction.Margin = new Thickness(24, 8, 24, 0);
        canvas.Margin = new Thickness(24, 0);
        layout.Add(header); layout.Add(instruction, 0, 1); layout.Add(canvas, 0, 2); layout.Add(new ScrollView { Content = stack }, 0, 3); Content = layout;
        SetColor(this.initial);
    }
    private void SelectColor(object? sender, TouchEventArgs e)
    {
        if (e.Touches.Length == 0 || canvas.Width <= 0 || canvas.Height <= 0) return;
        var radius = Math.Min(canvas.Width, canvas.Height) / 2 - 12;
        if (radius <= 0) return;
        var point = e.Touches[0];
        (wheel.Hue, wheel.Saturation) = ColorWheelMath.AtPoint((point.X - canvas.Width / 2) / radius, (point.Y - canvas.Height / 2) / radius, wheel.Hue);
        Refresh();
    }
    private void SetColor(string color, bool updateHex = true)
    { (wheel.Hue, wheel.Saturation, wheel.Value) = ColorWheelMath.FromHex(color); Refresh(updateHex); }
    private void Refresh(bool updateHex = true)
    {
        updating = true;
        var color = ColorWheelMath.ToHex(wheel.Hue, wheel.Saturation, wheel.Value);
        brightness.Value = wheel.Value; brightnessLabel.Text = $"Brightness · {Math.Round(wheel.Value * 100)}%";
        if (updateHex) hex.Text = color;
        AppearanceUI.Input(hex); use.IsEnabled = true;
        sample.BackgroundColor = Color.FromArgb(color);
        ((Label)sample.Content!).TextColor = Color.FromArgb(AppearanceColors.AutomaticText(color));
        canvas.Invalidate(); updating = false; changed(color);
    }
    private async Task CloseAsync(bool accept)
    {
        if (closing) return; closing = true; accepted = accept;
        if (!accept) changed(initial);
        try { await Navigation.PopModalAsync(); }
        catch { accepted = false; closing = false; await DisplayAlertAsync("Unable to close picker", "Please try again.", "OK"); }
    }
    protected override void OnDisappearing()
    { base.OnDisappearing(); if (!accepted && !Navigation.ModalStack.Contains(this)) changed(initial); }
    private sealed class WheelDrawable : IDrawable
    {
        public double Hue, Saturation, Value;
        private static readonly RadialGradientPaint Desaturate = new(new[] { new PaintGradientStop(0, Colors.White), new PaintGradientStop(1, new Color(1f, 1f, 1f, 0f)) });
        public void Draw(ICanvas canvas, RectF bounds)
        {
            var cx = bounds.Width / 2; var cy = bounds.Height / 2; var radius = Math.Min(bounds.Width, bounds.Height) / 2 - 12;
            if (radius <= 0) return;
            canvas.SaveState();
            for (var i = 0; i < 360; i++)
            {
                var start = i * Math.Tau / 360; var end = (i + 1.5) * Math.Tau / 360;
                using var path = new PathF(); path.MoveTo(cx, cy);
                path.LineTo(cx + (float)Math.Cos(start) * radius, cy + (float)Math.Sin(start) * radius);
                path.LineTo(cx + (float)Math.Cos(end) * radius, cy + (float)Math.Sin(end) * radius); path.Close();
                canvas.FillColor = Color.FromArgb(ColorWheelMath.ToHex(i / 360d, 1, 1)); canvas.FillPath(path);
            }
            var circle = new RectF(cx - radius, cy - radius, radius * 2, radius * 2);
            canvas.SetFillPaint(Desaturate, circle); canvas.FillEllipse(circle);
            canvas.FillColor = new Color(0, 0, 0, (float)(1 - Value)); canvas.FillEllipse(circle);
            var x = cx + (float)(Math.Cos(Hue * Math.Tau) * Saturation) * radius;
            var y = cy + (float)(Math.Sin(Hue * Math.Tau) * Saturation) * radius;
            canvas.FillColor = Color.FromArgb(ColorWheelMath.ToHex(Hue, Saturation, Value)); canvas.FillCircle(x, y, 9);
            canvas.StrokeColor = Colors.Black; canvas.StrokeSize = 4; canvas.DrawCircle(x, y, 10);
            canvas.StrokeColor = Colors.White; canvas.StrokeSize = 2; canvas.DrawCircle(x, y, 10);
            canvas.RestoreState();
        }
    }
}
