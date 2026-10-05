namespace NotificationHistory.Core.Models;

public sealed class NotificationSourceCount
{
    public string? SourceApp { get; set; }
    public long Count { get; set; }
}

public sealed record AppBadgeData(AppProfile Profile, long Count, IReadOnlyList<string?> Sources);
