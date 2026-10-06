import Foundation
import SQLite3

struct NotificationAppearance: Sendable {
    let displayName: String
    let background: String
    let gradient: String
    let useGradient: Bool
    let titleColor: String
    let bodyColor: String
    let timestampColor: String
    let autoText: Bool
    let imagePath: String?
    let circle: Bool
}

struct CapturedNotification: Sendable {
    let id: Int64
    let source: String?
    let title: String?
    let subtitle: String?
    let body: String?
    let receivedAt: Int64
    var appearance: NotificationAppearance? = nil
    var url: URL { URL(string: "notificationhistory://notification/\(id)")! }
    var sourceDisplay: String {
        if let appearance { return appearance.displayName }
        guard let source, !source.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty else { return "Unknown app" }
        return source
    }
    var titleDisplay: String {
        guard let title, !title.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty else { return "Notification" }
        return title
    }
    var preview: String {
        if let body, !body.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty { return body }
        if let subtitle, !subtitle.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty { return subtitle }
        return "No message provided"
    }
}

enum HistoryError: LocalizedError {
    case unavailable, database, newerSchema, sqlite(Int32), lockTimedOut, lockFailed(Int32)
    var errorDescription: String? {
        switch self {
        case .unavailable: "Shared history is unavailable. Check App Group signing and unlock the device once after restart."
        case .database, .sqlite: "History could not be saved or read. Check free storage and try again."
        case .lockTimedOut: "History is busy. Please try again shortly."
        case .lockFailed: "Could not access the shared history lock. Check device storage and signing."
        case .newerSchema: "Update Notification History to access this database."
        }
    }
}

// Never hold a connection across an await or a widget timeline lifetime.
final class SharedDatabase {
    private var handle: OpaquePointer?
    private var writerLock: SharedWriterLock?
    private var appearanceAvailable = false
    private var iconDirectory: URL?
    private let transient = unsafeBitCast(-1, to: sqlite3_destructor_type.self)

    init(readOnly: Bool = false, testingPath: URL? = nil, schemaURL: URL? = nil) throws {
        let file: URL
        if let testingPath { file = testingPath }
        else {
            let group = try SignedAppGroups.group()
            guard let container = FileManager.default.containerURL(forSecurityApplicationGroupIdentifier: group) else {
                throw SharedStorageConfigurationError.unavailableGroup(group)
            }
            let directory = container.appendingPathComponent("Library/NotificationHistory", isDirectory: true)
            if !readOnly {
                try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
#if os(iOS)
                try FileManager.default.setAttributes([.protectionKey: FileProtectionType.completeUntilFirstUserAuthentication], ofItemAtPath: directory.path)
#endif
                var protectedURL = directory
                var values = URLResourceValues(); values.isExcludedFromBackup = true
                try protectedURL.setResourceValues(values)
            }
            file = directory.appendingPathComponent("history.sqlite3")
        }
        if !readOnly { writerLock = try SharedWriterLock(path: file.path) }
        iconDirectory = file.deletingLastPathComponent().appendingPathComponent("Icons", isDirectory: true)
        let flags = (readOnly ? SQLITE_OPEN_READONLY : SQLITE_OPEN_READWRITE | SQLITE_OPEN_CREATE) | SQLITE_OPEN_FULLMUTEX
        guard sqlite3_open_v2(file.path, &handle, flags, nil) == SQLITE_OK else {
            let failure = databaseError()
            if let handle { sqlite3_close(handle) }; handle = nil; throw failure
        }
        do {
            sqlite3_busy_timeout(handle, 5000)
            // Swift normalization preserves Unicode app keys; SQLite UPPER/TRIM are ASCII-only.
            let functionResult = sqlite3_create_function_v2(handle, "nh_source_key", 1, SQLITE_UTF8 | SQLITE_DETERMINISTIC,
                nil, { context, _, arguments in
                    let value = arguments?[0]
                    var key = ""
                    if let value, let pointer = sqlite3_value_text(value) {
                        key = String(decoding: UnsafeBufferPointer(start: pointer, count: Int(sqlite3_value_bytes(value))), as: UTF8.self)
                            .trimmingCharacters(in: .whitespacesAndNewlines).uppercased()
                    }
                    key.withCString { sqlite3_result_text(context, $0, Int32(key.utf8.count), unsafeBitCast(-1, to: sqlite3_destructor_type.self)) }
                }, nil, nil, nil)
            guard functionResult == SQLITE_OK else { throw HistoryError.sqlite(functionResult) }
            if !readOnly {
                guard try textScalar("PRAGMA journal_mode=WAL") == "wal" else { throw HistoryError.database }
                try exec("PRAGMA synchronous=FULL")
                try exec("BEGIN IMMEDIATE")
                do {
                    let version = try scalar("PRAGMA user_version")
                    guard version <= 2 else { throw HistoryError.newerSchema }
                    if version < 2 {
                        guard let url = schemaURL ?? Bundle.main.url(forResource: "schema", withExtension: "sql") else { throw HistoryError.database }
                        try exec(String(contentsOf: url, encoding: .utf8))
                    }
                    try exec("COMMIT")
                    appearanceAvailable = try scalar("PRAGMA user_version") == 2
                } catch { try? exec("ROLLBACK"); throw error }
            } else {
                let version = try scalar("PRAGMA user_version")
                guard version == 1 || version == 2 else { throw HistoryError.newerSchema }
                appearanceAvailable = version == 2
            }
        } catch { sqlite3_close(handle); handle = nil; throw error }
    }
    deinit { if let handle { sqlite3_close(handle) }; writerLock = nil }
    private func databaseError() -> HistoryError {
        guard let handle else { return .unavailable }
        return .sqlite(sqlite3_extended_errcode(handle))
    }
    private func exec(_ sql: String) throws {
        guard sqlite3_exec(handle, sql, nil, nil, nil) == SQLITE_OK else { throw databaseError() }
    }
    private func prepare(_ sql: String) throws -> OpaquePointer {
        var statement: OpaquePointer?
        guard sqlite3_prepare_v2(handle, sql, -1, &statement, nil) == SQLITE_OK, let statement else { throw databaseError() }
        return statement
    }
    private func bind(_ value: String?, to statement: OpaquePointer, at index: Int32) throws {
        let result: Int32
        if let value { result = value.withCString { sqlite3_bind_text(statement, index, $0, Int32(value.utf8.count), transient) } }
        else { result = sqlite3_bind_null(statement, index) }
        guard result == SQLITE_OK else { throw HistoryError.sqlite(result) }
    }
    private func text(_ statement: OpaquePointer, _ column: Int32) -> String? {
        guard sqlite3_column_type(statement, column) != SQLITE_NULL, let pointer = sqlite3_column_text(statement, column) else { return nil }
        return String(decoding: UnsafeBufferPointer(start: pointer, count: Int(sqlite3_column_bytes(statement, column))), as: UTF8.self)
    }
    private func scalar(_ sql: String) throws -> Int64 {
        let statement = try prepare(sql); defer { sqlite3_finalize(statement) }
        guard sqlite3_step(statement) == SQLITE_ROW else { throw databaseError() }
        return sqlite3_column_int64(statement, 0)
    }
    private func textScalar(_ sql: String) throws -> String? {
        let statement = try prepare(sql); defer { sqlite3_finalize(statement) }
        guard sqlite3_step(statement) == SQLITE_ROW else { throw databaseError() }
        return text(statement, 0)
    }
    func save(source: String?, title: String?, subtitle: String?, body: String?, receivedAt: Date? = nil, captureID: String? = nil) throws -> Int64 {
        let now = Int64(Date().timeIntervalSince1970 * 1000)
        try exec("BEGIN IMMEDIATE")
        do {
            let statement = try prepare("INSERT OR IGNORE INTO Notifications(SourceApp,Title,Subtitle,Body,ReceivedAt,CreatedAt,CaptureId) VALUES(?,?,?,?,?,?,?)")
            defer { sqlite3_finalize(statement) }
            try bind(source, to: statement, at: 1); try bind(title, to: statement, at: 2)
            try bind(subtitle, to: statement, at: 3); try bind(body, to: statement, at: 4)
            sqlite3_bind_int64(statement, 5, Int64((receivedAt ?? Date()).timeIntervalSince1970 * 1000))
            sqlite3_bind_int64(statement, 6, now); try bind(captureID, to: statement, at: 7)
            guard sqlite3_step(statement) == SQLITE_DONE else { throw databaseError() }
            var id = sqlite3_last_insert_rowid(handle)
            if let captureID {
                let lookup = try prepare("SELECT Id FROM Notifications WHERE CaptureId=?"); defer { sqlite3_finalize(lookup) }
                try bind(captureID, to: lookup, at: 1)
                guard sqlite3_step(lookup) == SQLITE_ROW else { throw databaseError() }
                id = sqlite3_column_int64(lookup, 0)
            }
            let days = Int(try textScalar("SELECT COALESCE((SELECT Value FROM Settings WHERE Key='retention'),'0')") ?? "0") ?? 0
            if days > 0 {
                let cleanup = try prepare("DELETE FROM Notifications WHERE IsFavorite=0 AND ReceivedAt<?"); defer { sqlite3_finalize(cleanup) }
                sqlite3_bind_int64(cleanup, 1, now - Int64(days) * 86_400_000)
                guard sqlite3_step(cleanup) == SQLITE_DONE else { throw HistoryError.database }
            }
            try exec("COMMIT"); return id
        } catch { try? exec("ROLLBACK"); throw error }
    }
    func snapshot(now: Date = Date()) throws -> (today: Int, recent: [CapturedNotification]) {
        // A read transaction keeps the count and rows consistent while an intent writes.
        try exec("BEGIN")
        do {
            let start = Int64(Calendar.current.startOfDay(for: now).timeIntervalSince1970 * 1000)
            let end = Int64(Calendar.current.date(byAdding: .day, value: 1, to: Calendar.current.startOfDay(for: now))!.timeIntervalSince1970 * 1000)
            let days = Int(try textScalar("SELECT COALESCE((SELECT Value FROM Settings WHERE Key='retention'),'0')") ?? "0") ?? 0
            let cutoff = days > 0 ? Int64(now.timeIntervalSince1970 * 1000) - Int64(days) * 86_400_000 : Int64.min
            let count = Int(try scalar("SELECT COUNT(*) FROM Notifications WHERE ReceivedAt>=\(start) AND ReceivedAt<\(end) AND (IsFavorite=1 OR ReceivedAt>=\(cutoff))"))
            let statement = try prepare("SELECT Id,substr(SourceApp,1,100),substr(Title,1,200),substr(Subtitle,1,200),substr(Body,1,200),ReceivedAt,CASE WHEN length(SourceApp)<=160 THEN SourceApp ELSE NULL END FROM Notifications WHERE (IsFavorite=1 OR ReceivedAt>=\(cutoff)) ORDER BY ReceivedAt DESC,Id DESC LIMIT 3")
            defer { sqlite3_finalize(statement) }
            var rows: [CapturedNotification] = []
            var status = sqlite3_step(statement)
            while status == SQLITE_ROW {
                let source = text(statement, 1)
                rows.append(CapturedNotification(id: sqlite3_column_int64(statement, 0), source: source, title: text(statement, 2), subtitle: text(statement, 3), body: text(statement, 4), receivedAt: sqlite3_column_int64(statement, 5), appearance: try appearance(for: text(statement, 6))))
                status = sqlite3_step(statement)
            }
            guard status == SQLITE_DONE else { throw databaseError() }
            try exec("COMMIT"); return (count, rows)
        } catch { try? exec("ROLLBACK"); throw error }
    }
    private func appearance(for source: String?) throws -> NotificationAppearance? {
        guard appearanceAvailable, let source else { return nil }
        let statement = try prepare("SELECT DisplayName,BackgroundColor,GradientColor,UseGradient,TitleColor,BodyColor,AutoTextColor,ImageFile,IconShape,TimestampColor FROM AppProfiles WHERE SourceKey=?")
        defer { sqlite3_finalize(statement) }
        try bind(source.trimmingCharacters(in: .whitespacesAndNewlines).uppercased(), to: statement, at: 1)
        let result = sqlite3_step(statement)
        if result == SQLITE_DONE { return nil }
        guard result == SQLITE_ROW else { throw databaseError() }
        var imagePath: String?
        if let file = text(statement, 7), file.count == 41, file.hasSuffix("-icon.png"),
           file.prefix(32).allSatisfy({ $0.isHexDigit }), let directory = iconDirectory {
            imagePath = directory.appendingPathComponent(file).path
        }
        return NotificationAppearance(displayName: text(statement, 0) ?? source,
            background: text(statement, 1) ?? "#242426", gradient: text(statement, 2) ?? "#171719",
            useGradient: sqlite3_column_int(statement, 3) != 0,
            titleColor: text(statement, 4) ?? "#FFFFFF", bodyColor: text(statement, 5) ?? "#FFFFFF",
            timestampColor: text(statement, 9) ?? "#D1D5DB",
            autoText: sqlite3_column_int(statement, 6) != 0, imagePath: imagePath, circle: text(statement, 8) == "Circle")
    }

    private func setting(_ key: String) throws -> String? {
        let statement = try prepare("SELECT Value FROM Settings WHERE Key=?"); defer { sqlite3_finalize(statement) }
        try bind(key, to: statement, at: 1)
        let status = sqlite3_step(statement)
        if status == SQLITE_DONE { return nil }
        guard status == SQLITE_ROW else { throw databaseError() }
        return text(statement, 0)
    }
    private func widgetCutoff(now: Date) throws -> Int64 {
        let days = Int(try setting("retention") ?? "0") ?? 0
        // Settings normally contains 0/7/30/90. Treat malformed extremes as Never.
        guard days > 0, days <= 365_000 else { return Int64.min }
        return Int64(now.timeIntervalSince1970 * 1000) - Int64(days) * 86_400_000
    }
    private func widgetApps(cutoff: Int64) throws -> [WidgetAppSummary] {
        var apps: [String: WidgetAppSummary] = [:]
        if appearanceAvailable {
            let profiles = try prepare("SELECT SourceKey,SourceName FROM AppProfiles"); defer { sqlite3_finalize(profiles) }
            var status = sqlite3_step(profiles)
            while status == SQLITE_ROW {
                let key = text(profiles, 0) ?? "", source = text(profiles, 1) ?? ""
                apps[key] = WidgetAppSummary(key: key, sourceName: source, count: 0,
                    appearance: try appearance(for: source) ?? .defaultProfile(for: source))
                status = sqlite3_step(profiles)
            }
            guard status == SQLITE_DONE else { throw databaseError() }
        }
        let counts = try prepare("SELECT nh_source_key(SourceApp),MIN(SourceApp),COUNT(*) FROM Notifications WHERE (IsFavorite=1 OR ReceivedAt>=?) GROUP BY nh_source_key(SourceApp)")
        defer { sqlite3_finalize(counts) }; sqlite3_bind_int64(counts, 1, cutoff)
        var status = sqlite3_step(counts)
        while status == SQLITE_ROW {
            let key = text(counts, 0) ?? "", source = text(counts, 1) ?? ""
            let existing = apps[key]
            apps[key] = WidgetAppSummary(key: key, sourceName: existing?.sourceName ?? source,
                count: Int(sqlite3_column_int64(counts, 2)),
                appearance: existing?.appearance ?? .defaultProfile(for: source))
            status = sqlite3_step(counts)
        }
        guard status == SQLITE_DONE else { throw databaseError() }
        return apps.values.sorted {
            let order = $0.displayName.localizedCaseInsensitiveCompare($1.displayName)
            return order == .orderedSame ? $0.key < $1.key : order == .orderedAscending
        }
    }
    private func widgetNavigation(layout: WidgetPageLayout) throws -> WidgetNavigation {
        guard let value = try setting(layout.settingsKey), value.utf8.count <= 4096,
              let state = try? JSONDecoder().decode(WidgetNavigation.self, from: Data(value.utf8)) else { return WidgetNavigation() }
        return state
    }
    private func validateWidgetLayout(_ layout: WidgetPageLayout) throws {
        guard WidgetPageLayout.scopes.contains(layout.scope), (3...4).contains(layout.badgePageSize),
              (1...10).contains(layout.notificationPageSize) else { throw HistoryError.database }
    }
    func widgetSnapshot(layout: WidgetPageLayout, now: Date = Date()) throws -> WidgetHistorySnapshot {
        try validateWidgetLayout(layout); try exec("BEGIN")
        do {
            let cutoff = try widgetCutoff(now: now), apps = try widgetApps(cutoff: cutoff)
            var navigation = try widgetNavigation(layout: layout); navigation.resolve(apps: apps, layout: layout)
            let selected = apps.first(where: { $0.key == navigation.selectedSourceKey })
            var rows: [CapturedNotification] = []
            if let selected, selected.count > 0 {
                let statement = try prepare("SELECT Id,substr(SourceApp,1,100),substr(Title,1,200),substr(Subtitle,1,200),substr(Body,1,1024),ReceivedAt FROM Notifications WHERE (IsFavorite=1 OR ReceivedAt>=?) AND nh_source_key(SourceApp)=? ORDER BY ReceivedAt DESC,Id DESC LIMIT ? OFFSET ?")
                defer { sqlite3_finalize(statement) }
                sqlite3_bind_int64(statement, 1, cutoff); try bind(selected.key, to: statement, at: 2)
                sqlite3_bind_int64(statement, 3, Int64(layout.notificationPageSize))
                sqlite3_bind_int64(statement, 4, Int64(navigation.notificationPage) * Int64(layout.notificationPageSize))
                var status = sqlite3_step(statement)
                while status == SQLITE_ROW {
                    rows.append(CapturedNotification(id: sqlite3_column_int64(statement, 0), source: text(statement, 1),
                        title: text(statement, 2), subtitle: text(statement, 3), body: text(statement, 4),
                        receivedAt: sqlite3_column_int64(statement, 5), appearance: selected.appearance))
                    status = sqlite3_step(statement)
                }
                guard status == SQLITE_DONE else { throw databaseError() }
            }
            let start = navigation.badgePage * layout.badgePageSize
            let snapshot = WidgetHistorySnapshot(apps: Array(apps.dropFirst(start).prefix(layout.badgePageSize)),
                appCount: apps.count, selectedApp: selected, navigation: navigation,
                badgePageCount: WidgetNavigation.pageCount(apps.count, size: layout.badgePageSize),
                notificationPageCount: WidgetNavigation.pageCount(selected?.count ?? 0, size: layout.notificationPageSize),
                notifications: rows, theme: try setting("appearance") ?? "System")
            try exec("COMMIT"); return snapshot
        } catch { try? exec("ROLLBACK"); throw error }
    }
    func updateWidgetNavigation(layout: WidgetPageLayout, action: WidgetNavigationAction, sourceKey: String? = nil, now: Date = Date()) throws {
        guard writerLock != nil else { throw HistoryError.database }
        try validateWidgetLayout(layout); try exec("BEGIN IMMEDIATE")
        do {
            let apps = try widgetApps(cutoff: widgetCutoff(now: now))
            var navigation = try widgetNavigation(layout: layout)
            navigation.apply(action, sourceKey: sourceKey, apps: apps, layout: layout)
            let value = String(decoding: try JSONEncoder().encode(navigation), as: UTF8.self)
            let statement = try prepare("INSERT INTO Settings(Key,Value) VALUES(?,?) ON CONFLICT(Key) DO UPDATE SET Value=excluded.Value")
            defer { sqlite3_finalize(statement) }
            try bind(layout.settingsKey, to: statement, at: 1); try bind(value, to: statement, at: 2)
            guard sqlite3_step(statement) == SQLITE_DONE else { throw databaseError() }
            try exec("COMMIT")
        } catch { try? exec("ROLLBACK"); throw error }
    }
}
