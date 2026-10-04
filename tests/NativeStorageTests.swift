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
        if CommandLine.arguments.count == 4 && CommandLine.arguments[3] == "--verify-seed" {
            let store = try SharedDatabase(testingPath: file, schemaURL: schema)
            let snapshot = try store.snapshot()
            guard snapshot.today == 2, snapshot.recent.count == 2,
              snapshot.recent.contains(where: { $0.source == "Managed 👋" && $0.body == "Bro tomorrow jadi? 明天见\n100% _" }),
              snapshot.recent.contains(where: { $0.source == nil && $0.body == nil }) else {
                fatalError("Native reader could not read managed data")
            }
            print("PASS Native reads managed Unicode and partial fields")
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
            } catch { failures.record() }
        }
        guard failures.count() == 0 else { fatalError("Concurrent native/managed storage checks failed") }
        print("PASS Native reads managed rows, writes Unicode, preserves repeats, retries event tokens and reads during concurrent writes")
    }
}
