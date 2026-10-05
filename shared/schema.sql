CREATE TABLE IF NOT EXISTS Notifications (
 Id INTEGER PRIMARY KEY AUTOINCREMENT,
 SourceApp TEXT, Title TEXT, Subtitle TEXT, Body TEXT,
 ReceivedAt INTEGER NOT NULL, CreatedAt INTEGER NOT NULL,
 IsFavorite INTEGER NOT NULL DEFAULT 0,
 IsRead INTEGER NOT NULL DEFAULT 0,
 CaptureId TEXT UNIQUE
);
CREATE INDEX IF NOT EXISTS IX_Notifications_Received ON Notifications(ReceivedAt DESC, Id DESC);
CREATE INDEX IF NOT EXISTS IX_Notifications_Source ON Notifications(SourceApp, ReceivedAt DESC, Id DESC);
CREATE INDEX IF NOT EXISTS IX_Notifications_Favorite ON Notifications(IsFavorite, ReceivedAt DESC, Id DESC);
CREATE TABLE IF NOT EXISTS Settings (Key TEXT PRIMARY KEY, Value TEXT NOT NULL);
CREATE TABLE IF NOT EXISTS AppProfiles (
 SourceKey TEXT PRIMARY KEY, SourceName TEXT NOT NULL, DisplayName TEXT NOT NULL,
 BackgroundColor TEXT NOT NULL, GradientColor TEXT NOT NULL, UseGradient INTEGER NOT NULL DEFAULT 1,
 TitleColor TEXT NOT NULL, BodyColor TEXT NOT NULL, TimestampColor TEXT NOT NULL,
 AutoTextColor INTEGER NOT NULL DEFAULT 1, ImageFile TEXT, OriginalImageFile TEXT,
 IconShape TEXT NOT NULL DEFAULT 'Rounded'
);
PRAGMA user_version=2;
