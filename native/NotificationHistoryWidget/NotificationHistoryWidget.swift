import SwiftUI
import WidgetKit

struct HistoryTimelineEntry: TimelineEntry {
    let date: Date
    let today: Int
    let recent: [CapturedNotification]
    let unavailable: Bool
}
struct HistoryProvider: TimelineProvider {
    func placeholder(in context: Context) -> HistoryTimelineEntry {
        HistoryTimelineEntry(date: Date(), today: 0, recent: [], unavailable: false)
    }
    func getSnapshot(in context: Context, completion: @escaping (HistoryTimelineEntry) -> Void) { load(completion) }
    func getTimeline(in context: Context, completion: @escaping (Timeline<HistoryTimelineEntry>) -> Void) {
        load { entry in
            let next = Date().addingTimeInterval(15 * 60)
            completion(Timeline(entries: [entry], policy: .after(next)))
        }
    }
    private func load(_ completion: @escaping (HistoryTimelineEntry) -> Void) {
        Task {
            let entry = await Task.detached(priority: .utility) {
                do {
                    let snapshot = try SharedDatabase(readOnly: true).snapshot()
                    return HistoryTimelineEntry(date: Date(), today: snapshot.today, recent: snapshot.recent, unavailable: false)
                } catch { return HistoryTimelineEntry(date: Date(), today: 0, recent: [], unavailable: true) }
            }.value
            completion(entry)
        }
    }
}
struct HistoryWidgetView: View {
    @Environment(\.widgetFamily) private var family
    let entry: HistoryTimelineEntry
    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            HStack {
                Label(family == .systemSmall ? "Notifications" : "Recent Notifications", systemImage: "tray.full.fill")
                    .font(.caption.weight(.semibold)).foregroundStyle(.teal)
                Spacer(minLength: 0)
                if family != .systemSmall { Text("\(entry.today)").font(.headline) }
            }
            if family == .systemSmall { Text("\(entry.today) today").font(.title2.bold()) }
            if entry.recent.isEmpty {
                Text(entry.unavailable ? "Open history or unlock your iPhone to access saved notifications." : "No captured notifications yet.").font(.caption).foregroundStyle(.secondary)
            } else if family == .systemSmall, let latest = entry.recent.first {
                Text(latest.sourceDisplay).font(.caption).foregroundStyle(.secondary).lineLimit(1).privacySensitive()
                Text(latest.titleDisplay).font(.headline).lineLimit(2).privacySensitive()
            } else {
                ForEach(entry.recent, id: \.id) { item in
                    Link(destination: item.url) {
                        VStack(alignment: .leading, spacing: 2) {
                            Text(item.sourceDisplay).font(.caption2.weight(.semibold)).foregroundStyle(.teal).lineLimit(1).privacySensitive()
                            Text("\(item.titleDisplay): \(item.preview)")
                                .font(.caption).foregroundStyle(.primary).lineLimit(1).privacySensitive()
                        }.frame(maxWidth: .infinity, alignment: .leading)
                    }
                }
            }
            Spacer(minLength: 0)
        }
        .containerBackground(.background, for: .widget)
        .widgetURL(family == .systemSmall ? entry.recent.first?.url ?? URL(string: "notificationhistory://history") : URL(string: "notificationhistory://history"))
    }
}
@main
struct NotificationHistoryWidget: Widget {
    var body: some WidgetConfiguration {
        StaticConfiguration(kind: "NotificationHistory", provider: HistoryProvider()) { HistoryWidgetView(entry: $0) }
            .configurationDisplayName("Notification History")
            .description("Recent notifications you saved through Shortcuts.")
            .supportedFamilies([.systemSmall, .systemMedium])
    }
}
