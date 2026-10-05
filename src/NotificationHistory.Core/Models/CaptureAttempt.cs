namespace NotificationHistory.Core.Models;

public sealed record CaptureAttempt
{
    public int Schema { get; init; }
    public string Id { get; init; } = "";
    public long StartedAt { get; init; }
    public long UpdatedAt { get; init; }
    public string Stage { get; init; } = "";
    public string Outcome { get; init; } = "";
    public bool Saved { get; init; }
    public string? ErrorCode { get; init; }
    public int? PlatformCode { get; init; }
    public string Status => Outcome == "failed" ? Saved ? "Saved; later failure" : "Failed"
        : Saved ? "Saved" : "In progress or interrupted";
    public string StageDisplay => Stage switch
    {
        "started" => "Action started",
        "opening_storage" => "Opening shared storage",
        "storage_opened" => "Storage opened",
        "saving" => "Saving notification",
        "saved" => "Notification saved",
        "requesting_widget_refresh" => "Requesting widget refresh",
        "completed" => "Action ready to return success",
        _ => "Unknown stage"
    };
    public string? ErrorDisplay => ErrorCode switch
    {
        "storage_unavailable" => "Shared storage unavailable",
        "database_error" => "Database operation failed",
        "sqlite_error" when PlatformCode is int code && (code & 255) is 5 or 6 => "Database busy or locked",
        "sqlite_error" => "SQLite operation failed",
        "writer_lock_timeout" => "Timed out waiting for the history lock",
        "writer_lock_error" => "Could not access the history lock",
        "schema_newer" => "Database requires a newer app version",
        "signing_invalid" => "Could not read signing information",
        "signing_no_common_group" => "App and extensions have no common App Group",
        "signing_group_unavailable" => "Signed App Group unavailable",
        "file_access_error" => "Could not access a storage file",
        "posix_error" => "Storage system operation failed",
        "cancelled" => "Execution cancelled",
        "unexpected_error" => "Unexpected execution error",
        _ => null
    };
}
