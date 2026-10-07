namespace NotificationHistory.Core.Services;

public static class DeepLinks
{
    public static bool TryParseSourceApp(string? value, out long id)
    {
        id = 0;
        return Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == "notificationhistory" &&
            uri.Host == "source-app" && uri.IsDefaultPort && string.IsNullOrEmpty(uri.UserInfo) &&
            string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment) &&
            long.TryParse(uri.AbsolutePath.Trim('/'), System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out id) && id > 0;
    }
    public static bool TryParse(string? value, out long id)
    {
        id = 0;
        return Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == "notificationhistory" &&
         uri.Host == "notification" && long.TryParse(uri.AbsolutePath.Trim('/'), out id) && id > 0;
    }
}
