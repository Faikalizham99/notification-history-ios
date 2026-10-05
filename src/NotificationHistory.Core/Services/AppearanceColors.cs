using System.Globalization;
namespace NotificationHistory.Core.Services;

public static class AppearanceColors
{
    public static bool TryNormalize(string? value, out string result)
    {
        result = "";
        var hex = value?.Trim().TrimStart('#');
        if (hex is null || hex.Length is not (3 or 6) || hex.Any(c => !char.IsAsciiHexDigit(c))) return false;
        if (hex.Length == 3) hex = string.Concat(hex.Select(c => new string(c, 2)));
        result = "#" + hex.ToUpperInvariant(); return true;
    }
    public static string Normalize(string value) => TryNormalize(value, out var normalized) ? normalized
        : throw new ArgumentException("Use a color such as #075E54 or #FFF.");
    public static bool IsImageFile(string? value) => value is null ||
        (value.EndsWith("-icon.png", StringComparison.Ordinal) || value.EndsWith("-source.png", StringComparison.Ordinal)) &&
        value.Length is 41 or 43 && Guid.TryParseExact(value[..32], "N", out _);
    public static string AutomaticText(string background, string? gradient = null)
    {
        var values = new[] { Luminance(background), Luminance(gradient ?? background) };
        var white = values.Min(value => 1.05 / (value + .05));
        var black = values.Min(value => (value + .05) / (.005605391624202723 + .05));
        return white >= black ? "#FFFFFF" : "#111111";
    }
    private static double Luminance(string hex)
    {
        hex = Normalize(hex);
        double Channel(int offset)
        {
            var value = int.Parse(hex.AsSpan(offset, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255d;
            return value <= .04045 ? value / 12.92 : Math.Pow((value + .055) / 1.055, 2.4);
        }
        return .2126 * Channel(1) + .7152 * Channel(3) + .0722 * Channel(5);
    }
}
