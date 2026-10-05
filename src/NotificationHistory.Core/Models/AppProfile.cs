using SQLite;
using NotificationHistory.Core.Services;
namespace NotificationHistory.Core.Models;

[Table("AppProfiles")]
public sealed class AppProfile
{
    [PrimaryKey] public string SourceKey { get; set; } = "";
    public string SourceName { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string BackgroundColor { get; set; } = "#242426";
    public string GradientColor { get; set; } = "#171719";
    public bool UseGradient { get; set; } = true;
    public string TitleColor { get; set; } = "#FFFFFF";
    public string BodyColor { get; set; } = "#FFFFFF";
    public string TimestampColor { get; set; } = "#D1D5DB";
    public bool AutoTextColor { get; set; } = true;
    public string? ImageFile { get; set; }
    public string? OriginalImageFile { get; set; }
    public string IconShape { get; set; } = "Rounded";

    public AppProfile Copy() => (AppProfile)MemberwiseClone();
    public static string Key(string? source) => (source ?? "").Trim().ToUpperInvariant();
    public static AppProfile Default(string? source)
    {
        var name = string.IsNullOrWhiteSpace(source) ? "Unknown app" : source.Trim();
        var colors = Key(source) switch
        {
            "WHATSAPP" => ("#075E54", "#128C7E"),
            "INSTAGRAM" => ("#8B2868", "#B84376"),
            "LAZADA" => ("#282D80", "#39277F"),
            "FACEBOOK" => ("#173F78", "#242426"),
            _ => ("#242426", "#171719")
        };
        return new() { SourceKey = Key(source), SourceName = name, DisplayName = name,
            BackgroundColor = colors.Item1, GradientColor = colors.Item2 };
    }
    public void Validate()
    {
        SourceName = SourceName.Trim(); DisplayName = DisplayName.Trim();
        if (SourceName.Length is 0 or > 160 || DisplayName.Length is 0 or > 80)
            throw new ArgumentException("Enter a source name (up to 160 characters) and a display name (up to 80 characters).");
        SourceKey = Key(SourceName);
        BackgroundColor = AppearanceColors.Normalize(BackgroundColor);
        GradientColor = AppearanceColors.Normalize(GradientColor);
        TitleColor = AppearanceColors.Normalize(TitleColor);
        BodyColor = AppearanceColors.Normalize(BodyColor);
        TimestampColor = AppearanceColors.Normalize(TimestampColor);
        if (IconShape is not ("Rounded" or "Circle")) throw new ArgumentException("Choose a rounded or circular icon.");
        if (!AppearanceColors.IsImageFile(ImageFile) || !AppearanceColors.IsImageFile(OriginalImageFile))
            throw new ArgumentException("Invalid local icon reference.");
    }
}
