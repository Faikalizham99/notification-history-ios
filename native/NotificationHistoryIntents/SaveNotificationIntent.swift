import AppIntents
import WidgetKit

struct SaveNotificationIntent: AppIntent {
    static var title: LocalizedStringResource = "Save Notification"
    static var description = IntentDescription("Save the notification details you provide to local history. Empty fields are supported.")
    static var openAppWhenRun = false

    @Parameter(title: "Source App") var sourceApp: String?
    @Parameter(title: "Title") var notificationTitle: String?
    @Parameter(title: "Subtitle") var subtitle: String?
    @Parameter(title: "Message") var message: String?
    @Parameter(title: "Received At", description: "Optional original arrival time. Defaults to when this action runs.") var receivedAt: Date?
    @Parameter(title: "Capture ID", description: "Optional unique event ID for retries. Leave empty unless you have a real event identifier.") var captureID: String?

    static var parameterSummary: some ParameterSummary {
        Summary("Save notification from \(.$sourceApp)") {
            \.$notificationTitle
            \.$subtitle
            \.$message
            \.$receivedAt
            \.$captureID
        }
    }
    func perform() async throws -> some IntentResult & ReturnsValue<Int> {
        let source = sourceApp, title = notificationTitle, sub = subtitle, body = message, date = receivedAt
        // Empty Capture ID means no deduplication, rather than merging every blank ID.
        let token = captureID.flatMap { $0.isEmpty ? nil : $0 }
        let id = try await Task.detached(priority: .userInitiated) {
            try SharedDatabase().save(source: source, title: title, subtitle: sub, body: body, receivedAt: date, captureID: token)
        }.value
        WidgetCenter.shared.reloadAllTimelines()
        return .result(value: Int(id))
    }
}

@main
struct NotificationHistoryIntents: AppIntentsExtension {}
