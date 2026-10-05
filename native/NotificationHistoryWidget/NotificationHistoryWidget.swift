import SwiftUI
import WidgetKit
import UIKit

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
                AppearanceNotificationRow(item: latest, small: true)
            } else {
                ForEach(entry.recent, id: \.id) { item in
                    Link(destination: item.url) {
                        AppearanceNotificationRow(item: item, small: false)
                    }
                }
            }
            Spacer(minLength: 0)
        }
        .containerBackground(.background, for: .widget)
        .widgetURL(family == .systemSmall ? entry.recent.first?.url ?? URL(string: "notificationhistory://history") : URL(string: "notificationhistory://history"))
    }
}
private struct AppearanceNotificationRow: View {
    let item: CapturedNotification
    let small: Bool
    private var appearance: NotificationAppearance? { item.appearance }
    private var background: Color { .appearanceHex(appearance?.background ?? "#242426") }
    private var end: Color { .appearanceHex(appearance?.useGradient == true ? appearance!.gradient : appearance?.background ?? "#242426") }
    private var textColor: Color {
        guard appearance?.autoText == true else { return .appearanceHex(appearance?.titleColor ?? "#FFFFFF") }
        func luminance(_ hex: String) -> Double {
            let value = UInt32(hex.trimmingCharacters(in: CharacterSet(charactersIn: "#")), radix: 16) ?? 0
            func channel(_ offset: UInt32) -> Double {
                let c = Double((value >> offset) & 255) / 255
                return c <= 0.04045 ? c / 12.92 : pow((c + 0.055) / 1.055, 2.4)
            }
            return 0.2126 * channel(16) + 0.7152 * channel(8) + 0.0722 * channel(0)
        }
        let values = [luminance(appearance!.background), luminance(appearance!.useGradient ? appearance!.gradient : appearance!.background)]
        return 1.05 / (values.max()! + 0.05) >= (values.min()! + 0.05) / (0.005605391624202723 + 0.05) ? .white : .appearanceHex("#111111")
    }
    var body: some View {
        HStack(alignment: .top, spacing: 7) {
            Group {
                if let path = appearance?.imagePath, let image = UIImage(contentsOfFile: path) {
                    Image(uiImage: image).resizable().scaledToFill()
                } else {
                    Text(String(item.sourceDisplay.prefix(1)).uppercased()).font(.caption.bold())
                        .frame(maxWidth: .infinity, maxHeight: .infinity).background(.white.opacity(0.15))
                }
            }.frame(width: small ? 30 : 24, height: small ? 30 : 24)
                .clipShape(RoundedRectangle(cornerRadius: appearance?.circle == true ? 20 : 7)).foregroundStyle(textColor).privacySensitive()
            VStack(alignment: .leading, spacing: 2) {
                Text(item.sourceDisplay).font(.caption2.weight(.semibold)).foregroundStyle(textColor).lineLimit(1).privacySensitive()
                Text(small ? item.titleDisplay : "\(item.titleDisplay): \(item.preview)")
                    .font(small ? .caption.weight(.semibold) : .caption2)
                    .foregroundStyle(appearance?.autoText == true ? textColor : .appearanceHex(appearance?.bodyColor ?? "#FFFFFF"))
                    .lineLimit(small ? 2 : 1).privacySensitive()
            }.frame(maxWidth: .infinity, alignment: .leading)
        }.padding(7).background(LinearGradient(colors: [background, end], startPoint: .topLeading, endPoint: .bottomTrailing))
            .clipShape(RoundedRectangle(cornerRadius: 10))
    }
}
private extension Color {
    static func appearanceHex(_ hex: String) -> Color {
        let value = UInt32(hex.trimmingCharacters(in: CharacterSet(charactersIn: "#")), radix: 16) ?? 0x242426
        return Color(red: Double((value >> 16) & 255) / 255, green: Double((value >> 8) & 255) / 255, blue: Double(value & 255) / 255)
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
