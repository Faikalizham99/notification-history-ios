namespace NotificationHistory.Core.Services;

public static class DeepLinks
{
    public static bool TryParse(string? value, out long id)
    {
        id = 0;
        return Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == "notificationhistory" &&
         uri.Host == "notification" && long.TryParse(uri.AbsolutePath.Trim('/'), out id) && id > 0;
    }
}
