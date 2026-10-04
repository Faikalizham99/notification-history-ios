namespace NotificationHistory.Services;

public static class StorageLocation
{
    public const string GroupId = "group.com.faikal.notificationhistory";
    public static string DatabasePath()
    {
#if IOS
        var container = Foundation.NSFileManager.DefaultManager.GetContainerUrl(GroupId)
         ?? throw new InvalidOperationException("App Group unavailable. Sign the app and both extensions with the same authorized App Group entitlement.");
        var directory = Path.Combine(container.Path!, "Library", "NotificationHistory");
        Directory.CreateDirectory(directory);
        var attributes = new Foundation.NSFileAttributes { ProtectionKey = Foundation.NSFileProtection.CompleteUntilFirstUserAuthentication };
        if (!Foundation.NSFileManager.DefaultManager.SetAttributes(attributes, directory, out var error))
            throw new InvalidOperationException("Cannot protect shared history: " + error?.LocalizedDescription);
        using var url = Foundation.NSUrl.FromFilename(directory);
        if (!url.SetResource(Foundation.NSUrl.IsExcludedFromBackupKey, Foundation.NSNumber.FromBoolean(true), out var backupError))
            throw new InvalidOperationException("Cannot exclude history from backups: " + backupError?.LocalizedDescription);
        return Path.Combine(directory, "history.sqlite3");
#else
  return Path.Combine(FileSystem.AppDataDirectory, "history.sqlite3");
#endif
    }
}
