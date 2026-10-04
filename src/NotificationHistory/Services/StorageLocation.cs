namespace NotificationHistory.Services;

public static class StorageLocation
{
    public const string GroupId = NotificationHistory.Core.Services.SignedAppGroups.PreferredGroup;
    public static string DatabasePath()
    {
#if IOS
        string group;
        try
        {
            var app = Foundation.NSBundle.MainBundle;
            string Executable(Foundation.NSBundle? bundle) => bundle?.ExecutableUrl?.Path
                ?? throw new IOException("An app extension is missing.");
            var widget = Foundation.NSBundle.FromPath(Path.Combine(app.BundlePath, "PlugIns", "NotificationHistoryWidget.appex"));
            var intents = Foundation.NSBundle.FromPath(Path.Combine(app.BundlePath, "Extensions", "NotificationHistoryIntents.appex"));
            group = NotificationHistory.Core.Services.SignedAppGroups.SelectCommon(
                new[] { Executable(app), Executable(widget), Executable(intents) }
                    .Select(NotificationHistory.Core.Services.SignedAppGroups.Read));
        }
        catch (NotificationHistory.Core.Services.SharedStorageConfigurationException) { throw; }
        catch (Exception signingError)
        {
            throw new NotificationHistory.Core.Services.SharedStorageConfigurationException(
                "Cannot read shared-storage signing information. Re-sign the app and both extensions with matching App Groups.", signingError);
        }
        var container = Foundation.NSFileManager.DefaultManager.GetContainerUrl(group)
         ?? throw new NotificationHistory.Core.Services.SharedStorageConfigurationException(
             $"iOS cannot open the signed App Group ({group}). Re-sign the app and both extensions with profiles that authorize this group, then unlock your iPhone once after restarting.");
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
