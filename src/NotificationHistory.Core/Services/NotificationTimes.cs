using System.Globalization;
namespace NotificationHistory.Core.Services;

public static class NotificationTimes
{
    public static string Detail(DateTimeOffset localTime, CultureInfo? culture = null) =>
        $"{localTime.ToString("T", culture ?? CultureInfo.CurrentCulture)} (UTC{localTime.ToString("zzz", CultureInfo.InvariantCulture)})";
}
