namespace NotificationHistory.Core.Services;

public static class ColorWheelMath
{
    public static (double Hue, double Saturation, double Value) FromHex(string hex)
    {
        var value = Convert.ToInt32(AppearanceColors.Normalize(hex)[1..], 16);
        var r = ((value >> 16) & 255) / 255d; var g = ((value >> 8) & 255) / 255d; var b = (value & 255) / 255d;
        var max = Math.Max(r, Math.Max(g, b)); var delta = max - Math.Min(r, Math.Min(g, b));
        var hue = delta == 0 ? 0 : max == r ? (g - b) / delta : max == g ? (b - r) / delta + 2 : (r - g) / delta + 4;
        return ((hue / 6 + 1) % 1, max == 0 ? 0 : delta / max, max);
    }
    public static string ToHex(double hue, double saturation, double value)
    {
        if (!double.IsFinite(hue) || !double.IsFinite(saturation) || !double.IsFinite(value)) throw new ArgumentException("Invalid color coordinates.");
        hue = (hue % 1 + 1) % 1; saturation = Math.Clamp(saturation, 0, 1); value = Math.Clamp(value, 0, 1);
        var h = hue * 6; var c = value * saturation; var x = c * (1 - Math.Abs(h % 2 - 1)); var m = value - c;
        var (r, g, b) = (int)h switch { 0 => (c, x, 0d), 1 => (x, c, 0d), 2 => (0d, c, x), 3 => (0d, x, c), 4 => (x, 0d, c), _ => (c, 0d, x) };
        return $"#{(int)Math.Round((r + m) * 255):X2}{(int)Math.Round((g + m) * 255):X2}{(int)Math.Round((b + m) * 255):X2}";
    }
    // Coordinates are relative to the wheel center, with radius one.
    public static (double Hue, double Saturation) AtPoint(double x, double y, double previousHue)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y)) throw new ArgumentException("Invalid color coordinates.");
        var distance = Math.Sqrt(x * x + y * y);
        return (distance < .000001 ? previousHue : (Math.Atan2(y, x) / Math.Tau + 1) % 1, Math.Min(1, distance));
    }
}
