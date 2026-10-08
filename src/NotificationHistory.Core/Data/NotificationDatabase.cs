using NotificationHistory.Core.Models;
using NotificationHistory.Core.Services;
using SQLite;
namespace NotificationHistory.Core.Data;

// Each operation owns a short-lived connection. SQLite coordinates other processes.
public sealed class NotificationDatabase
{
    private readonly Lazy<string> path;
    public NotificationDatabase(string path) : this(() => path) { }
    public NotificationDatabase(Func<string> pathFactory) { path = new(pathFactory); }
    private readonly SemaphoreSlim gate = new(1, 1);
    private bool initialized;
    public string Path => path.Value;
    private async Task<T> Run<T>(Func<SQLiteConnection, T> action, CancellationToken token = default)
    {
        await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            return await Task.Run(() =>
            {
                token.ThrowIfCancellationRequested();
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
                using var writerLock = SharedWriterLock.Acquire(Path, token);
                using var db = new SQLiteConnection(Path, SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create | SQLiteOpenFlags.FullMutex);
                db.BusyTimeout = TimeSpan.FromSeconds(5);
                if (!initialized)
                {
                    var mode = db.ExecuteScalar<string>("PRAGMA journal_mode=WAL");
                    if (mode != "wal") throw new InvalidOperationException("Shared storage requires SQLite WAL.");
                    db.Execute("BEGIN IMMEDIATE");
                    try
                    {
                        var version = db.ExecuteScalar<int>("PRAGMA user_version");
                        if (version > 2) throw new InvalidOperationException("Update the app to read this database.");
                        if (version < 2)
                        {
                            using var resource = typeof(NotificationDatabase).Assembly.GetManifestResourceStream("schema.sql")!;
                            using var reader = new StreamReader(resource);
                            foreach (var statement in reader.ReadToEnd().Split(';').Select(x => x.Trim()).Where(x => x.Length > 0)) db.Execute(statement);
                        }
                        db.Execute("COMMIT");
                        initialized = true;
                    }
                    catch { db.Execute("ROLLBACK"); throw; }
                }
                db.Execute("PRAGMA synchronous=FULL");
                return action(db);
            }, token).ConfigureAwait(false);
        }
        finally { gate.Release(); }
    }
    public Task<long> SaveAsync(NotificationEntry entry) => Run(db =>
    {
        db.Execute("BEGIN IMMEDIATE");
        try
        {
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            if (string.IsNullOrWhiteSpace(entry.CaptureId)) entry.CaptureId = null;
            entry.CreatedAt = now;
            if (entry.ReceivedAt == 0) entry.ReceivedAt = now;
            // Only an explicit caller-supplied event token is deduplicated; repeated text is valid.
            if (entry.CaptureId is not null)
            {
                var existing = db.ExecuteScalar<long>("SELECT Id FROM Notifications WHERE CaptureId=?", entry.CaptureId);
                if (existing != 0) { db.Execute("COMMIT"); return existing; }
            }
            db.Execute("INSERT OR IGNORE INTO Notifications(SourceApp,Title,Subtitle,Body,ReceivedAt,CreatedAt,CaptureId) VALUES(?,?,?,?,?,?,?)",
          entry.SourceApp, entry.Title, entry.Subtitle, entry.Body, entry.ReceivedAt, entry.CreatedAt, entry.CaptureId);
            entry.Id = entry.CaptureId is null ? db.ExecuteScalar<long>("SELECT last_insert_rowid()") :
          db.ExecuteScalar<long>("SELECT Id FROM Notifications WHERE CaptureId=?", entry.CaptureId);
            Cleanup(db, now);
            db.Execute("COMMIT");
            return entry.Id;
        }
        catch { db.Execute("ROLLBACK"); throw; }
    });
    public Task<List<NotificationEntry>> QueryAsync(NotificationFilter filter, PageCursor? cursor = null,
     int limit = 60, CancellationToken token = default) => Run(db =>
    {
        var clauses = new List<string>(); var args = new List<object>();
        if (filter.Favorites) clauses.Add("IsFavorite=1");
        if (filter.Sources is { } sources)
        {
            var values = sources.Where(s => s is not null).Distinct().ToArray();
            var sourceClauses = new List<string>();
            if (values.Length > 0)
            {
                sourceClauses.Add("SourceApp IN (" + string.Join(",", values.Select(_ => "?")) + ")");
                args.AddRange(values.Select(s => (object)s!));
            }
            if (sources.Contains(null)) sourceClauses.Add("SourceApp IS NULL");
            // A configured app with no captures must not display other apps' history.
            clauses.Add(sourceClauses.Count == 0 ? "0=1" : "(" + string.Join(" OR ", sourceClauses) + ")");
        }
        else if (filter.Source is not null) { clauses.Add("SourceApp=?"); args.Add(filter.Source); }
        if (filter.From is long from) { clauses.Add("ReceivedAt>=?"); args.Add(from); }
        if (filter.To is long to) { clauses.Add("ReceivedAt<?"); args.Add(to); }
        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            clauses.Add("(SourceApp LIKE ? ESCAPE '\\' OR Title LIKE ? ESCAPE '\\' OR Subtitle LIKE ? ESCAPE '\\' OR Body LIKE ? ESCAPE '\\')");
            var term = "%" + filter.Search.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
            for (var i = 0; i < 4; i++) args.Add(term);
        }
        if (cursor is not null)
        {
            clauses.Add("(ReceivedAt < ? OR (ReceivedAt = ? AND Id < ?))");
            args.Add(cursor.ReceivedAt); args.Add(cursor.ReceivedAt); args.Add(cursor.Id);
        }
        args.Add(Math.Clamp(limit, 1, 200));
        // One extra character lets the preview distinguish a complete body from a longer message.
        // The full body is fetched by ID only when the user expands it or opens details.
        return db.Query<NotificationEntry>("SELECT Id,SourceApp,Title,Subtitle,substr(Body,1,181) AS Body,ReceivedAt,CreatedAt,IsFavorite,IsRead,CaptureId FROM Notifications" + (clauses.Count > 0 ? " WHERE " + string.Join(" AND ", clauses) : "") +
      " ORDER BY ReceivedAt DESC, Id DESC LIMIT ?", args.ToArray());
    }, token);
    public Task<NotificationEntry?> GetAsync(long id) => Run<NotificationEntry?>(db => db.Find<NotificationEntry>(id));
    public Task<List<string>> SourcesAsync() => Run(db => db.Query<SourceRow>(
     "SELECT DISTINCT SourceApp FROM Notifications WHERE SourceApp IS NOT NULL AND SourceApp <> '' ORDER BY SourceApp COLLATE NOCASE").Select(x => x.SourceApp).ToList());
    public Task<List<NotificationSourceCount>> SourceCountsAsync(CancellationToken token = default) => Run(db =>
        db.Query<NotificationSourceCount>("SELECT SourceApp,COUNT(*) AS Count FROM Notifications GROUP BY SourceApp ORDER BY SourceApp"), token);
    public Task SetFavoriteAsync(long id, bool value) => Run(db => db.Execute("UPDATE Notifications SET IsFavorite=? WHERE Id=?", value, id));
    public Task MarkReadAsync(long id) => Run(db => db.Execute("UPDATE Notifications SET IsRead=1 WHERE Id=?", id));
    public Task DeleteAsync(long id) => Run(db => db.Execute("DELETE FROM Notifications WHERE Id=?", id));
    public Task<int> ClearAppAsync(string sourceKey) => Run(db =>
    {
        ArgumentNullException.ThrowIfNull(sourceKey);
        var key = AppProfile.Key(sourceKey);
        db.Execute("BEGIN IMMEDIATE");
        try
        {
            // Resolve current variants under the writer lock, including captures made
            // since the badge was loaded. SQLite UPPER alone would miss Unicode names.
            var sources = db.Query<NotificationSourceCount>("SELECT DISTINCT SourceApp FROM Notifications")
                .Where(row => AppProfile.Key(row.SourceApp) == key).Select(row => row.SourceApp).ToList();
            var removed = 0;
            foreach (var source in sources)
                removed += source is null
                    ? db.Execute("DELETE FROM Notifications WHERE IsFavorite=0 AND SourceApp IS NULL")
                    : db.Execute("DELETE FROM Notifications WHERE IsFavorite=0 AND SourceApp=?", source);
            db.Execute("COMMIT"); return removed;
        }
        catch { db.Execute("ROLLBACK"); throw; }
    });
    public Task ClearAsync() => Run(db => { db.Execute("DELETE FROM Notifications WHERE IsFavorite=0"); db.ExecuteScalar<int>("PRAGMA wal_checkpoint(TRUNCATE)"); return 0; });
    public Task<string?> SettingAsync(string key) => Run(db => db.ExecuteScalar<string?>("SELECT Value FROM Settings WHERE Key=?", key));
    public Task SetSettingAsync(string key, string value) => Run(db => db.Execute("INSERT OR REPLACE INTO Settings(Key,Value) VALUES(?,?)", key, value));
    public Task<string?> AppOpeningUrlAsync(string? source) => Run(db =>
        db.ExecuteScalar<string?>("SELECT Value FROM Settings WHERE Key=?", AppOpeningLinks.SettingsKey(source)));
    public Task<List<AppProfile>> ProfilesAsync() => Run(db =>
    {
        var profiles = db.Table<AppProfile>().OrderBy(x => x.DisplayName).ToList();
        foreach (var profile in profiles)
            profile.OpenAppUrl = db.ExecuteScalar<string?>("SELECT Value FROM Settings WHERE Key=?", AppOpeningLinks.SettingsKey(profile.SourceKey));
        return profiles;
    });
    public Task SaveProfileAsync(AppProfile profile, string? previousKey = null) => Run(db =>
    {
        profile.Validate();
        db.Execute("BEGIN IMMEDIATE");
        try
        {
            if (profile.SourceKey != previousKey && db.Find<AppProfile>(profile.SourceKey) is not null)
                throw new ArgumentException("This source app already has an appearance. Edit that app instead.");
            db.Execute("INSERT OR REPLACE INTO AppProfiles(SourceKey,SourceName,DisplayName,BackgroundColor,GradientColor,UseGradient,TitleColor,BodyColor,TimestampColor,AutoTextColor,ImageFile,OriginalImageFile,IconShape) VALUES(?,?,?,?,?,?,?,?,?,?,?,?,?)",
                profile.SourceKey, profile.SourceName, profile.DisplayName, profile.BackgroundColor, profile.GradientColor,
                profile.UseGradient, profile.TitleColor, profile.BodyColor, profile.TimestampColor, profile.AutoTextColor,
                profile.ImageFile, profile.OriginalImageFile, profile.IconShape);
            if (profile.OpenAppUrl is null)
                db.Execute("DELETE FROM Settings WHERE Key=?", AppOpeningLinks.SettingsKey(profile.SourceKey));
            else
                db.Execute("INSERT OR REPLACE INTO Settings(Key,Value) VALUES(?,?)", AppOpeningLinks.SettingsKey(profile.SourceKey), profile.OpenAppUrl);
            if (previousKey is not null && previousKey != profile.SourceKey)
            {
                db.Delete<AppProfile>(previousKey);
                db.Execute("DELETE FROM Settings WHERE Key=?", AppOpeningLinks.SettingsKey(previousKey));
            }
            db.Execute("COMMIT"); return 0;
        }
        catch { db.Execute("ROLLBACK"); throw; }
    });
    public Task DeleteProfileAsync(string key) => Run(db =>
    {
        db.Execute("BEGIN IMMEDIATE");
        try
        {
            var removed = db.Delete<AppProfile>(key);
            db.Execute("DELETE FROM Settings WHERE Key=?", AppOpeningLinks.SettingsKey(key));
            db.Execute("COMMIT"); return removed;
        }
        catch { db.Execute("ROLLBACK"); throw; }
    });
    public Task<int> CleanupAsync(DateTimeOffset? now = null) => Run(db => Cleanup(db, (now ?? DateTimeOffset.UtcNow).ToUnixTimeMilliseconds()));
    private static int Cleanup(SQLiteConnection db, long now)
    {
        var days = db.ExecuteScalar<string?>("SELECT Value FROM Settings WHERE Key='retention'");
        return int.TryParse(days, out var value) && value > 0 ? db.Execute("DELETE FROM Notifications WHERE IsFavorite=0 AND ReceivedAt<?", now - value * 86400000L) : 0;
    }
    public Task<string> IntegrityAsync() => Run(db => db.ExecuteScalar<string>("PRAGMA integrity_check"));
    private sealed class SourceRow { public string SourceApp { get; set; } = ""; }
}
