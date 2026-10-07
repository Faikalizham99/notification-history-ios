using NotificationHistory.Core.Models;
namespace NotificationHistory.Core.Services;

public static class AppOpeningLinks
{
    public static string SettingsKey(string? source) => "app.open-url." + AppProfile.Key(source);

    public static string? Normalize(string? value)
    {
        var text = value?.Trim();
        if (string.IsNullOrEmpty(text)) return null;
        if (text.Length > 2048 || text.Any(c => char.IsWhiteSpace(c) || char.IsControl(c)) ||
            !Uri.TryCreate(text, UriKind.Absolute, out var uri) ||
            !text.StartsWith(uri.Scheme + "://", StringComparison.OrdinalIgnoreCase) ||
            uri.Scheme is "notificationhistory" or "file" or "data" or "javascript" or "http" ||
            uri.Scheme == "https" && (string.IsNullOrEmpty(uri.Host) || !string.IsNullOrEmpty(uri.UserInfo)))
            throw new ArgumentException("Enter a full app-opening URL, such as appscheme://, or an HTTPS app link. A bundle ID alone cannot open an app.");
        return text;
    }
}
