import Foundation
import OSLog

// Best-effort metadata only. Diagnostic writes must never prevent a capture.
final class CaptureTrace: @unchecked Sendable {
    private struct Attempt: Encodable {
        let schema = 1
        let id: String
        let startedAt: Int64
        var updatedAt: Int64
        var stage = "started"
        var outcome = "running"
        var saved = false
        var errorCode: String?
        var platformCode: Int?
    }
    private let lock = NSLock()
    private let logger = Logger(subsystem: "com.faikal.notificationhistory", category: "Capture")
    private var attempt: Attempt
    private let directory: URL?

    init(testingDirectory: URL? = nil) {
        let now = Int64(Date().timeIntervalSince1970 * 1000)
        attempt = Attempt(id: UUID().uuidString.lowercased(), startedAt: now, updatedAt: now)
        directory = try? Self.prepareDirectory(testingDirectory ?? Self.directory())
        mark("started")
        prune()
    }
    private static func directory() throws -> URL {
        let group = try SignedAppGroups.group()
        guard let container = FileManager.default.containerURL(forSecurityApplicationGroupIdentifier: group) else {
            throw SharedStorageConfigurationError.unavailableGroup(group)
        }
        return container.appendingPathComponent("Library/NotificationHistory/CaptureDiagnostics", isDirectory: true)
    }
    private static func prepareDirectory(_ directory: URL) throws -> URL {
        try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
#if os(iOS)
        try FileManager.default.setAttributes([.protectionKey: FileProtectionType.completeUntilFirstUserAuthentication], ofItemAtPath: directory.path)
#endif
        var protectedDirectory = directory
        var values = URLResourceValues(); values.isExcludedFromBackup = true
        try protectedDirectory.setResourceValues(values)
        return directory
    }
    func mark(_ stage: String) {
        lock.lock(); defer { lock.unlock() }
        attempt.stage = stage
        if stage == "saved" {
            attempt.outcome = "saved"; attempt.saved = true
            attempt.errorCode = nil; attempt.platformCode = nil
        }
        write()
    }
    func fail(_ error: Error) {
        lock.lock(); defer { lock.unlock() }
        // Keep the last stage, so a failure can be located without message contents.
        attempt.outcome = "failed"
        if let history = error as? HistoryError {
            switch history {
            case .unavailable: attempt.errorCode = "storage_unavailable"
            case .database: attempt.errorCode = "database_error"
            case .sqlite(let code): attempt.errorCode = "sqlite_error"; attempt.platformCode = Int(code)
            case .lockTimedOut: attempt.errorCode = "writer_lock_timeout"
            case .lockFailed(let code): attempt.errorCode = "writer_lock_error"; attempt.platformCode = Int(code)
            case .newerSchema: attempt.errorCode = "schema_newer"
            }
        } else if let signing = error as? SharedStorageConfigurationError {
            switch signing {
            case .invalidSignature: attempt.errorCode = "signing_invalid"
            case .noCommonGroup: attempt.errorCode = "signing_no_common_group"
            case .unavailableGroup: attempt.errorCode = "signing_group_unavailable"
            }
        } else if error is CancellationError { attempt.errorCode = "cancelled" }
        else {
            let platform = error as NSError
            if platform.domain == NSCocoaErrorDomain {
                attempt.errorCode = "file_access_error"; attempt.platformCode = platform.code
            } else if platform.domain == NSPOSIXErrorDomain {
                attempt.errorCode = "posix_error"; attempt.platformCode = platform.code
            } else { attempt.errorCode = "unexpected_error" }
        }
        write()
    }
    private func write() {
        attempt.updatedAt = Int64(Date().timeIntervalSince1970 * 1000)
        logger.notice("Capture \(self.attempt.id, privacy: .public): \(self.attempt.stage, privacy: .public), \(self.attempt.outcome, privacy: .public), \(self.attempt.errorCode ?? "none", privacy: .public), platform code \(self.attempt.platformCode ?? 0, privacy: .public)")
        guard let directory else { return }
        do {
            let data = try JSONEncoder().encode(attempt)
#if os(iOS)
            try data.write(to: directory.appendingPathComponent(attempt.id + ".json"), options: [.atomic, .completeFileProtectionUntilFirstUserAuthentication])
#else
            try data.write(to: directory.appendingPathComponent(attempt.id + ".json"), options: .atomic)
#endif
        } catch { /* The original capture remains authoritative. */ }
    }
    private func prune() {
        guard let directory,
              let files = try? FileManager.default.contentsOfDirectory(at: directory, includingPropertiesForKeys: [.creationDateKey]) else { return }
        let records = files.filter { $0.pathExtension == "json" && UUID(uuidString: $0.deletingPathExtension().lastPathComponent) != nil }
            .sorted { ((try? $0.resourceValues(forKeys: [.creationDateKey]).creationDate) ?? .distantPast) >
                      ((try? $1.resourceValues(forKeys: [.creationDateKey]).creationDate) ?? .distantPast) }
        // Retain recent attempts while actions may still be executing.
        for file in records.dropFirst(50) {
            if let created = try? file.resourceValues(forKeys: [.creationDateKey]).creationDate,
               created < Date().addingTimeInterval(-300) { try? FileManager.default.removeItem(at: file) }
        }
    }
}
