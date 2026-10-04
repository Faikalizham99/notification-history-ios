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
