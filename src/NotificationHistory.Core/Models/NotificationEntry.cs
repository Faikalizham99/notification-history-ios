using SQLite;
namespace NotificationHistory.Core.Models;

[Table("Notifications")]
public sealed class NotificationEntry
{
    [PrimaryKey, AutoIncrement] public long Id { get; set; }
    public string? SourceApp { get; set; }
    public string? Title { get; set; }
    public string? Subtitle { get; set; }
    public string? Body { get; set; }
    public long ReceivedAt { get; set; }
    public long CreatedAt { get; set; }
    public bool IsFavorite { get; set; }
    public bool IsRead { get; set; }
    public string? CaptureId { get; set; }
    [Ignore] public DateTimeOffset ReceivedDate => DateTimeOffset.FromUnixTimeMilliseconds(ReceivedAt).ToLocalTime();
    [Ignore] public string SourceDisplay => string.IsNullOrWhiteSpace(SourceApp) ? "Unknown app" : SourceApp;
    [Ignore] public string TitleDisplay => string.IsNullOrWhiteSpace(Title) ? "Notification" : Title;
    [Ignore] public string Preview => !string.IsNullOrWhiteSpace(Body) ? Body is { Length: > 180 } ? Body[..(char.IsHighSurrogate(Body[179]) ? 179 : 180)] + "…" : Body
        : !string.IsNullOrWhiteSpace(Subtitle) ? Subtitle : "No message provided";
    [Ignore] public string TimeDisplay => ReceivedDate.ToString("t");
    [Ignore] public string FavoriteDisplay => IsFavorite ? "★" : "";
}

public sealed record NotificationFilter(string? Search = null, bool Favorites = false,
 string? Source = null, long? From = null, long? To = null, IReadOnlyList<string?>? Sources = null);
public sealed record PageCursor(long ReceivedAt, long Id);
