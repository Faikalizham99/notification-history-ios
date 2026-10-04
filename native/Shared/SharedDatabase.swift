import Foundation
import SQLite3

struct CapturedNotification: Sendable {
    let id: Int64
    let source: String?
    let title: String?
    let subtitle: String?
    let body: String?
    let receivedAt: Int64
    var url: URL { URL(string: "notificationhistory://notification/\(id)")! }
    var sourceDisplay: String {
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
    case unavailable, database, newerSchema
    var errorDescription: String? {
        switch self {
        case .unavailable: "Shared history is unavailable. Check App Group signing and unlock the device once after restart."
        case .database: "History could not be saved or read. Check free storage and try again."
        case .newerSchema: "Update Notification History to access this database."
        }
    }
}

// Never hold a connection across an await or a widget timeline lifetime.
final class SharedDatabase {
    private var handle: OpaquePointer?
    private var writerLock: SharedWriterLock?
    private let transient = unsafeBitCast(-1, to: sqlite3_destructor_type.self)
    static let group = "group.com.faikal.notificationhistory"

    init(readOnly: Bool = false, testingPath: URL? = nil, schemaURL: URL? = nil) throws {
        let file: URL
        if let testingPath { file = testingPath }
        else {
            guard let container = FileManager.default.containerURL(forSecurityApplicationGroupIdentifier: Self.group) else { throw HistoryError.unavailable }
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
        let flags = (readOnly ? SQLITE_OPEN_READONLY : SQLITE_OPEN_READWRITE | SQLITE_OPEN_CREATE) | SQLITE_OPEN_FULLMUTEX
        guard sqlite3_open_v2(file.path, &handle, flags, nil) == SQLITE_OK else {
            if let handle { sqlite3_close(handle) }; handle = nil; throw HistoryError.unavailable
        }
        do {
            sqlite3_busy_timeout(handle, 5000)
            if !readOnly {
                guard try textScalar("PRAGMA journal_mode=WAL") == "wal" else { throw HistoryError.database }
                try exec("PRAGMA synchronous=FULL")
                try exec("BEGIN IMMEDIATE")
                do {
                    let version = try scalar("PRAGMA user_version")
                    guard version <= 1 else { throw HistoryError.newerSchema }
                    if version == 0 {
                        guard let url = schemaURL ?? Bundle.main.url(forResource: "schema", withExtension: "sql") else { throw HistoryError.database }
                        try exec(String(contentsOf: url, encoding: .utf8))
                    }
                    try exec("COMMIT")
                } catch { try? exec("ROLLBACK"); throw error }
            } else {
                let version = try scalar("PRAGMA user_version")
                guard version == 1 else { throw HistoryError.newerSchema }
            }
        } catch { sqlite3_close(handle); handle = nil; throw error }
    }
    deinit { if let handle { sqlite3_close(handle) }; writerLock = nil }
    private func exec(_ sql: String) throws {
        guard sqlite3_exec(handle, sql, nil, nil, nil) == SQLITE_OK else { throw HistoryError.database }
    }
    private func prepare(_ sql: String) throws -> OpaquePointer {
        var statement: OpaquePointer?
        guard sqlite3_prepare_v2(handle, sql, -1, &statement, nil) == SQLITE_OK, let statement else { throw HistoryError.database }
        return statement
    }
    private func bind(_ value: String?, to statement: OpaquePointer, at index: Int32) throws {
        let result: Int32
        if let value { result = value.withCString { sqlite3_bind_text(statement, index, $0, Int32(value.utf8.count), transient) } }
        else { result = sqlite3_bind_null(statement, index) }
        guard result == SQLITE_OK else { throw HistoryError.database }
    }
    private func text(_ statement: OpaquePointer, _ column: Int32) -> String? {
        guard sqlite3_column_type(statement, column) != SQLITE_NULL, let pointer = sqlite3_column_text(statement, column) else { return nil }
        return String(decoding: UnsafeBufferPointer(start: pointer, count: Int(sqlite3_column_bytes(statement, column))), as: UTF8.self)
    }
    private func scalar(_ sql: String) throws -> Int64 {
        let statement = try prepare(sql); defer { sqlite3_finalize(statement) }
        guard sqlite3_step(statement) == SQLITE_ROW else { throw HistoryError.database }
        return sqlite3_column_int64(statement, 0)
    }
    private func textScalar(_ sql: String) throws -> String? {
        let statement = try prepare(sql); defer { sqlite3_finalize(statement) }
        guard sqlite3_step(statement) == SQLITE_ROW else { throw HistoryError.database }
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
            guard sqlite3_step(statement) == SQLITE_DONE else { throw HistoryError.database }
            var id = sqlite3_last_insert_rowid(handle)
            if let captureID {
                let lookup = try prepare("SELECT Id FROM Notifications WHERE CaptureId=?"); defer { sqlite3_finalize(lookup) }
                try bind(captureID, to: lookup, at: 1)
                guard sqlite3_step(lookup) == SQLITE_ROW else { throw HistoryError.database }
                id = sqlite3_column_int64(lookup, 0)
            }
            let days = Int(try textScalar("SELECT COALESCE((SELECT Value FROM Settings WHERE Key='retention'),'0')") ?? "0") ?? 0
            if days > 0 {
                let cleanup = try prepare("DELETE FROM Notifications WHERE ReceivedAt<?"); defer { sqlite3_finalize(cleanup) }
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
            let count = Int(try scalar("SELECT COUNT(*) FROM Notifications WHERE ReceivedAt>=\(max(start, cutoff)) AND ReceivedAt<\(end)"))
            let statement = try prepare("SELECT Id,substr(SourceApp,1,100),substr(Title,1,200),substr(Subtitle,1,200),substr(Body,1,200),ReceivedAt FROM Notifications WHERE ReceivedAt>=\(cutoff) ORDER BY ReceivedAt DESC,Id DESC LIMIT 3")
            defer { sqlite3_finalize(statement) }
            var rows: [CapturedNotification] = []
            var status = sqlite3_step(statement)
            while status == SQLITE_ROW {
                rows.append(CapturedNotification(id: sqlite3_column_int64(statement, 0), source: text(statement, 1), title: text(statement, 2), subtitle: text(statement, 3), body: text(statement, 4), receivedAt: sqlite3_column_int64(statement, 5)))
                status = sqlite3_step(statement)
            }
            guard status == SQLITE_DONE else { throw HistoryError.database }
            try exec("COMMIT"); return (count, rows)
        } catch { try? exec("ROLLBACK"); throw error }
    }
}
