import Foundation

final class ConcurrentFailures: @unchecked Sendable {
    private let lock = NSLock()
    private var failures = 0
    func record() { lock.lock(); defer { lock.unlock() }; failures += 1 }
    func count() -> Int { lock.lock(); defer { lock.unlock() }; return failures }
}
@main
struct NativeStorageTests {
    static func main() throws {
        if CommandLine.arguments.count == 3 && CommandLine.arguments[1] == "--verify-signing" {
            let directory = URL(fileURLWithPath: CommandLine.arguments[2])
            func read(_ name: String) throws -> Set<String> { try SignedAppGroups.read(directory.appendingPathComponent(name + ".macho")) }
            let original = try read("original")
            guard try SignedAppGroups.selectCommon([original, original, original]) == SignedAppGroups.preferred else {
                fatalError("Original shared group not preserved")
            }
            let app = try read("provider-app"), widget = try read("provider-widget"), intents = try read("provider-intents")
            guard try SignedAppGroups.selectCommon([app, widget, intents]) == "group.provider.a",
                  try read("invalid-groups") == ["group.provider.a"],
                  try read("unsigned").isEmpty, try read("missing-groups").isEmpty else {
                fatalError("Rewritten signing-group selection differs between Swift and C#")
            }
            var rejected = false
            do { _ = try SignedAppGroups.selectCommon([app, widget, original]) } catch { rejected = true }
            guard rejected else { fatalError("Mismatched groups accepted") }
            rejected = false
            do { _ = try SignedAppGroups.selectCommon([app, widget]) } catch { rejected = true }
            guard rejected else { fatalError("Missing extension accepted") }
            for name in ["truncated", "bad-offset", "bad-load", "bad-plist"] {
                rejected = false
                do { _ = try read(name) } catch { rejected = true }
                guard rejected else { fatalError("Malformed signature accepted: " + name) }
            }
            print("PASS Swift/C# agree on original, rewritten, missing, and malformed App Group signing fixtures")
            return
        }
        let file = URL(fileURLWithPath: CommandLine.arguments[1])
        let schema = URL(fileURLWithPath: CommandLine.arguments[2])
        try WidgetStorageTests.run(directory: file.deletingLastPathComponent(), schema: schema)
        let diagnosticDirectory = file.deletingLastPathComponent().appendingPathComponent("capture-diagnostics-" + UUID().uuidString, isDirectory: true)
        let trace = CaptureTrace(testingDirectory: diagnosticDirectory)
        trace.mark("opening_storage")
        trace.fail(HistoryError.sqlite(5))
        let diagnosticFiles = try FileManager.default.contentsOfDirectory(at: diagnosticDirectory, includingPropertiesForKeys: nil)
        let diagnosticData = try Data(contentsOf: diagnosticFiles.max(by: {
            ((try? $0.resourceValues(forKeys: [.contentModificationDateKey]).contentModificationDate) ?? .distantPast) <
            ((try? $1.resourceValues(forKeys: [.contentModificationDateKey]).contentModificationDate) ?? .distantPast)
        })!)
        let diagnostic = try JSONSerialization.jsonObject(with: diagnosticData) as! [String: Any]
        guard diagnostic["stage"] as? String == "opening_storage", diagnostic["outcome"] as? String == "failed",
              diagnostic["errorCode"] as? String == "sqlite_error", diagnostic["platformCode"] as? Int == 5,
              diagnostic["saved"] as? Bool == false,
              Set(diagnostic.keys).isSubset(of: ["schema", "id", "startedAt", "updatedAt", "stage", "outcome", "saved", "errorCode", "platformCode"]) else {
            fatalError("Native diagnostics do not preserve metadata-only failure status")
        }
        print("PASS native capture diagnostics record stages and numeric errors without content")
        trace.mark("saved"); trace.mark("completed")
        let savedDiagnostic = try JSONSerialization.jsonObject(with: Data(contentsOf: diagnosticFiles[0])) as! [String: Any]
        guard savedDiagnostic["saved"] as? Bool == true, savedDiagnostic["outcome"] as? String == "saved",
              savedDiagnostic["stage"] as? String == "completed" else { fatalError("Native diagnostics do not recognize a committed save") }
        let blockedPath = file.deletingLastPathComponent().appendingPathComponent("diagnostics-blocked-" + UUID().uuidString)
        try Data("Unrelated file".utf8).write(to: blockedPath)
        let unavailableTrace = CaptureTrace(testingDirectory: blockedPath)
        unavailableTrace.mark("saved"); unavailableTrace.mark("completed")
        guard try String(contentsOf: blockedPath, encoding: .utf8) == "Unrelated file" else { fatalError("Diagnostics replaced an unrelated file") }
        print("PASS native diagnostic failures cannot throw or overwrite unrelated files")
        if CommandLine.arguments.count == 4 && CommandLine.arguments[3] == "--verify-legacy" {
            let old = try SharedDatabase(readOnly: true, testingPath: file).snapshot()
            guard old.recent.count == 1, old.recent.first?.body == "Legacy message", old.recent.first?.appearance == nil else {
                fatalError("Read-only widget cannot read version-1 history")
            }
            _ = try SharedDatabase(testingPath: file, schemaURL: schema).save(source: "Native migration", title: "New", subtitle: nil, body: "After migration")
            let migrated = try SharedDatabase(readOnly: true, testingPath: file).snapshot()
            guard migrated.recent.count == 2 else { fatalError("Native migration lost history") }
            print("PASS native version-1 read and version-2 migration preserve existing history")
            return
        }
        if CommandLine.arguments.count == 4 && CommandLine.arguments[3] == "--verify-seed" {
            let store = try SharedDatabase(testingPath: file, schemaURL: schema)
            let snapshot = try store.snapshot()
            guard snapshot.today == 2, snapshot.recent.count == 2,
              snapshot.recent.contains(where: { $0.source == "Managed 👋" && $0.body == "Bro tomorrow jadi? 明天见\n100% _" }),
              snapshot.recent.contains(where: { $0.source == nil && $0.body == nil }) else {
                fatalError("Native reader could not read managed data")
            }
            print("PASS Native reads managed Unicode and partial fields")
            guard let profile = snapshot.recent.first(where: { $0.source == "Managed 👋" })?.appearance,
                  profile.displayName == "Configured app", profile.background == "#075E54", profile.circle else {
                fatalError("Widget cannot read managed appearance profiles")
            }
            print("PASS native widget reads managed app appearance profiles")
            return
        }
        _ = try SharedDatabase(testingPath: file, schemaURL: schema).save(source: "Native 🐈", title: "Ali", subtitle: nil, body: "Swift → C# 明天见\nRM25.00")
        let repeated1 = try SharedDatabase(testingPath: file, schemaURL: schema).save(source: nil, title: "Repeated", subtitle: nil, body: nil)
        let repeated2 = try SharedDatabase(testingPath: file, schemaURL: schema).save(source: nil, title: "Repeated", subtitle: nil, body: nil)
        guard repeated1 != repeated2 else { fatalError("Legitimate repeated notifications discarded") }
        let token1 = try SharedDatabase(testingPath: file, schemaURL: schema).save(source: nil, title: nil, subtitle: nil, body: nil, captureID: "native-event")
        let token2 = try SharedDatabase(testingPath: file, schemaURL: schema).save(source: nil, title: nil, subtitle: nil, body: nil, captureID: "native-event")
        guard token1 == token2 else { fatalError("Native idempotence failed") }
        let failures = ConcurrentFailures()
        DispatchQueue.concurrentPerform(iterations: 64) { index in
            do {
                let connection = try SharedDatabase(testingPath: file, schemaURL: schema)
                _ = try connection.save(source: "Native burst", title: "\(index)", subtitle: nil, body: "测试")
                let reader = try SharedDatabase(readOnly: true, testingPath: file)
                guard try reader.snapshot().recent.count == 3 else { throw HistoryError.database }
                guard try reader.widgetSnapshot(layout: WidgetPageLayout(scope: "large", badgePageSize: 4,
                    notificationPageSize: 2)).notifications.count <= 2 else { throw HistoryError.database }
            } catch { failures.record() }
        }
        guard failures.count() == 0 else { fatalError("Concurrent native/managed storage checks failed") }
        print("PASS Native reads managed rows, writes Unicode, preserves repeats, retries event tokens and reads during concurrent writes")
    }
}
