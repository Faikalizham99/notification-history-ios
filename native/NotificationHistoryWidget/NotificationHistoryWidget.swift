import SwiftUI
import WidgetKit
import AppIntents
import UIKit
import OSLog

private struct HistoryWidgetLayout: Sendable {
    let pages: WidgetPageLayout
    let columns: Int
    init(family: WidgetFamily, size: CGSize) {
        let scope = family == .systemLarge ? "large" : family == .systemExtraLarge ? "wide" : "portrait"
        columns = scope == "wide" && size.width >= 560 ? 2 : 1
        let badges = size.width - 36 >= 304 ? 4 : 3
        // Reserve the explicit 18-point inset, header, badges, both pagers and gaps.
        let rowCount = min(scope == "portrait" ? 5 : 4, max(1, Int((size.height - 36 - 166 + 10) / 90)))
        pages = WidgetPageLayout(scope: scope, badgePageSize: badges, notificationPageSize: rowCount * columns)
    }
}

private struct HistoryTimelineEntry: TimelineEntry {
    let date: Date
    let layout: HistoryWidgetLayout
    let snapshot: WidgetHistorySnapshot?
}

private struct HistoryProvider: TimelineProvider {
    func placeholder(in context: Context) -> HistoryTimelineEntry {
        let layout = HistoryWidgetLayout(family: context.family, size: context.displaySize)
        let app = WidgetAppSummary(key: "MESSAGES", sourceName: "Messages", count: 2,
            appearance: .defaultProfile(for: "Messages"))
        let sample = CapturedNotification(id: 1, source: "Messages", title: "Saved notification",
            subtitle: "Your history", body: "Your notifications stay on this device.",
            receivedAt: Int64(Date().timeIntervalSince1970 * 1000), appearance: app.appearance)
        return HistoryTimelineEntry(date: Date(), layout: layout, snapshot: WidgetHistorySnapshot(
            apps: [app], appCount: 1, selectedApp: app, navigation: WidgetNavigation(selectedSourceKey: app.key),
            badgePageCount: 1, notificationPageCount: 1, notifications: [sample], theme: "System"))
    }
    func getSnapshot(in context: Context, completion: @escaping (HistoryTimelineEntry) -> Void) {
        load(context, completion: completion)
    }
    func getTimeline(in context: Context, completion: @escaping (Timeline<HistoryTimelineEntry>) -> Void) {
        load(context) { entry in
            completion(Timeline(entries: [entry], policy: .after(Date().addingTimeInterval(15 * 60))))
        }
    }
    private func load(_ context: Context, completion: @escaping (HistoryTimelineEntry) -> Void) {
        let layout = HistoryWidgetLayout(family: context.family, size: context.displaySize)
        Task {
            let snapshot = await Task.detached(priority: .utility) {
                try? SharedDatabase(readOnly: true).widgetSnapshot(layout: layout.pages)
            }.value
            completion(HistoryTimelineEntry(date: Date(), layout: layout, snapshot: snapshot))
        }
    }
}

extension WidgetNavigationAction: AppEnum {
    static var typeDisplayRepresentation = TypeDisplayRepresentation(name: "Widget navigation")
    static var caseDisplayRepresentations: [WidgetNavigationAction: DisplayRepresentation] = [
        .selectApp: "Select app", .previousApps: "Previous apps", .nextApps: "Next apps",
        .previousNotifications: "Previous notifications", .nextNotifications: "Next notifications"
    ]
}

struct BrowseHistoryWidgetIntent: AppIntent {
    static var title: LocalizedStringResource = "Browse notification widget"
    static var openAppWhenRun = false
    static var isDiscoverable = false
    @Parameter(title: "Widget size") var scope: String
    @Parameter(title: "Apps per page") var badgePageSize: Int
    @Parameter(title: "Notifications per page") var notificationPageSize: Int
    @Parameter(title: "Action") var action: WidgetNavigationAction
    @Parameter(title: "Source app") var sourceKey: String?

    init() {}
    init(layout: WidgetPageLayout, action: WidgetNavigationAction, sourceKey: String? = nil) {
        scope = layout.scope; badgePageSize = layout.badgePageSize
        notificationPageSize = layout.notificationPageSize; self.action = action
        // An empty canonical key is the Unknown app, not an absent intent parameter.
        self.sourceKey = sourceKey.map { "source:" + $0 }
    }
    func perform() async throws -> some IntentResult {
        let layout = WidgetPageLayout(scope: scope, badgePageSize: badgePageSize, notificationPageSize: notificationPageSize)
        let operation = action
        let key = sourceKey.flatMap { $0.hasPrefix("source:") ? String($0.dropFirst(7)) : nil }
        do {
            try await Task.detached(priority: .userInitiated) {
                try SharedDatabase().updateWidgetNavigation(layout: layout, action: operation, sourceKey: key)
            }.value
        } catch {
            // Preserve the prior selection/page and keep notification content out of logs.
            Logger(subsystem: "com.faikal.notificationhistory.widget", category: "Navigation")
                .error("Could not update local widget navigation")
        }
        // WidgetKit reloads this widget's timeline after an interactive intent.
        return .result()
    }
}

private struct HistoryWidgetView: View {
    @Environment(\.widgetFamily) private var family
    @Environment(\.colorScheme) private var systemScheme
    let entry: HistoryTimelineEntry
    private var scheme: ColorScheme {
        switch entry.snapshot?.theme {
        case "Light": .light
        case "Dark": .dark
        default: systemScheme
        }
    }
    var body: some View {
        Group {
            if family == .systemSmall || family == .systemMedium {
                emptyState("Choose a larger widget", message: "Remove this widget and add the Large size from the widget gallery.")
            } else if let snapshot = entry.snapshot {
                VStack(alignment: .leading, spacing: 10) {
                    header(snapshot)
                    if !snapshot.apps.isEmpty { badges(snapshot) }
                    if snapshot.notifications.isEmpty {
                        emptyState(snapshot.selectedApp == nil ? "A little peace of mind" : "No saved notifications",
                            message: snapshot.selectedApp == nil ? "Save a notification through your Shortcut to get started." : "Notifications from this app will appear here.")
                            .frame(maxHeight: .infinity)
                    } else {
                        notificationCards(snapshot)
                        pager(layout: entry.layout.pages, previous: .previousNotifications, next: .nextNotifications,
                            page: snapshot.navigation.notificationPage, count: snapshot.notificationPageCount,
                            label: "Notifications")
                    }
                }
            } else {
                emptyState("History unavailable", message: "Unlock your iPhone or open Notification History to check shared storage.")
            }
        }
        // A single outer inset keeps the provider's capacity and the rendered layout in sync.
        .padding(18)
        .environment(\.colorScheme, scheme)
        .containerBackground(for: .widget) {
            LinearGradient(colors: scheme == .dark ? [.appearanceHex("#161A20"), .appearanceHex("#0E1116")] :
                [.appearanceHex("#F8FAFC"), .appearanceHex("#EDF2F5")], startPoint: .topLeading, endPoint: .bottomTrailing)
        }
        .widgetURL(URL(string: "notificationhistory://history"))
    }
    private func header(_ snapshot: WidgetHistorySnapshot) -> some View {
        HStack(spacing: 8) {
            Image(systemName: "tray.full.fill").font(.system(size: 15, weight: .semibold)).foregroundStyle(.teal)
            Text(snapshot.selectedApp?.displayName ?? "Notification History")
                .font(.system(size: 16, weight: .semibold)).lineLimit(1).minimumScaleFactor(0.8).privacySensitive()
            Spacer(minLength: 4)
            if let selected = snapshot.selectedApp {
                Text("\(selected.count.formatted()) saved").font(.system(size: 11, weight: .medium))
                    .foregroundStyle(.secondary).lineLimit(1).minimumScaleFactor(0.8).privacySensitive()
            }
        }.frame(height: 20)
    }
    private func badges(_ snapshot: WidgetHistorySnapshot) -> some View {
        VStack(spacing: 6) {
            HStack(spacing: 8) {
                ForEach(snapshot.apps) { app in
                    Button(intent: BrowseHistoryWidgetIntent(layout: entry.layout.pages, action: .selectApp, sourceKey: app.key)) {
                        AppBadge(app: app, selected: app.key == snapshot.navigation.selectedSourceKey)
                    }.buttonStyle(.plain)
                        .accessibilityLabel("\(app.displayName), \(app.count) saved notifications")
                        .accessibilityAddTraits(app.key == snapshot.navigation.selectedSourceKey ? [.isSelected] : [])
                        .privacySensitive()
                }
                ForEach(snapshot.apps.count..<entry.layout.pages.badgePageSize, id: \.self) { _ in
                    Color.clear.frame(maxWidth: .infinity)
                }
            }.frame(height: 46)
            if snapshot.badgePageCount > 1 {
                pager(layout: entry.layout.pages, previous: .previousApps, next: .nextApps,
                    page: snapshot.navigation.badgePage, count: snapshot.badgePageCount, label: "Apps")
            }
        }
    }
    private func notificationCards(_ snapshot: WidgetHistorySnapshot) -> some View {
        GeometryReader { geometry in
            let columns = entry.layout.columns
            // Keep the same card heights on partial pages; don't stretch the last card.
            let rowCount = max(1, (entry.layout.pages.notificationPageSize + columns - 1) / columns)
            let height = max(0, (geometry.size.height - CGFloat(rowCount - 1) * 10) / CGFloat(rowCount))
            VStack(spacing: 10) {
                ForEach(0..<rowCount, id: \.self) { row in
                    HStack(spacing: 10) {
                        ForEach(0..<columns, id: \.self) { column in
                            let index = row * columns + column
                            if index < snapshot.notifications.count {
                                let item = snapshot.notifications[index]
                                Link(destination: item.url) {
                                    NotificationCard(item: item, height: height)
                                }.buttonStyle(.plain).frame(maxWidth: .infinity, maxHeight: .infinity)
                                    .accessibilityLabel("\(item.titleDisplay). \(item.preview). Open notification")
                                    .privacySensitive()
                            } else { Color.clear.frame(maxWidth: .infinity, maxHeight: .infinity) }
                        }
                    }.frame(height: height)
                }
            }
        }.frame(maxHeight: .infinity)
    }
    private func pager(layout: WidgetPageLayout, previous: WidgetNavigationAction, next: WidgetNavigationAction,
                       page: Int, count: Int, label: String) -> some View {
        HStack(spacing: 12) {
            Text(label).font(.system(size: 11, weight: .medium)).foregroundStyle(.secondary)
            Spacer(minLength: 4)
            if count > 1 {
                pagingButton("chevron.left", layout: layout, action: previous, disabled: page == 0, label: "Previous \(label.lowercased()) page")
            }
            Text("\(page + 1) / \(count)").font(.system(size: 11, weight: .semibold)).monospacedDigit().foregroundStyle(.secondary)
                .accessibilityLabel("\(label), page \(page + 1) of \(count)")
            if count > 1 {
                pagingButton("chevron.right", layout: layout, action: next, disabled: page >= count - 1, label: "Next \(label.lowercased()) page")
            }
        }.frame(height: 32)
    }
    private func pagingButton(_ image: String, layout: WidgetPageLayout, action: WidgetNavigationAction, disabled: Bool, label: String) -> some View {
        Button(intent: BrowseHistoryWidgetIntent(layout: layout, action: action)) {
            Image(systemName: image).font(.system(size: 11, weight: .bold))
                .frame(width: 44, height: 32).foregroundStyle(disabled ? Color.secondary.opacity(0.4) : Color.primary)
                .background(Color.primary.opacity(disabled ? 0.03 : 0.07), in: Capsule())
        }.buttonStyle(.plain).disabled(disabled).accessibilityLabel(label)
    }
    private func emptyState(_ title: String, message: String) -> some View {
        VStack(alignment: .leading, spacing: 10) {
            Image(systemName: "tray").font(.system(size: 24, weight: .light)).foregroundStyle(.teal)
            Text(title).font(.system(size: 16, weight: .semibold))
            Text(message).font(.system(size: 13)).foregroundStyle(.secondary)
        }.frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .leading).padding(12)
    }
}

private struct AppBadge: View {
    let app: WidgetAppSummary
    let selected: Bool
    private var colors: AppearancePalette { AppearancePalette(app.appearance) }
    var body: some View {
        HStack(spacing: 6) {
            AppIcon(appearance: app.appearance, name: app.displayName, size: 28)
                .overlay(alignment: .bottomTrailing) {
                    if selected {
                        Image(systemName: "checkmark.circle.fill").font(.system(size: 10, weight: .bold))
                            .symbolRenderingMode(.palette).foregroundStyle(colors.title, colors.background)
                            .offset(x: 3, y: 3)
                    }
                }
            Text(app.count.formatted(.number.notation(.compactName))).font(.system(size: 12, weight: .bold))
                .monospacedDigit().lineLimit(1).minimumScaleFactor(0.7).foregroundStyle(colors.title).privacySensitive()
        }.padding(.horizontal, 8).frame(maxWidth: .infinity, maxHeight: .infinity)
            .background(colors.gradient, in: RoundedRectangle(cornerRadius: 18))
            .overlay(RoundedRectangle(cornerRadius: 18).strokeBorder(selected ? Color.teal : colors.title.opacity(0.2), lineWidth: selected ? 2 : 1))
            .padding(2)
    }
}

private struct NotificationCard: View {
    let item: CapturedNotification
    let height: CGFloat
    private var appearance: NotificationAppearance { item.appearance ?? .defaultProfile(for: item.source ?? "") }
    private var colors: AppearancePalette { AppearancePalette(appearance) }
    private var subtitle: String? { item.subtitle.flatMap { $0.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty ? nil : $0 } }
    private var bodyText: String? {
        if let body = item.body, !body.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty { return body }
        return subtitle == nil ? "No message provided" : nil
    }
    var body: some View {
        HStack(alignment: .top, spacing: 9) {
            AppIcon(appearance: appearance, name: item.sourceDisplay, size: 32)
            VStack(alignment: .leading, spacing: 3) {
                HStack(alignment: .firstTextBaseline, spacing: 6) {
                    Text(item.titleDisplay).font(.system(size: 15, weight: .semibold)).foregroundStyle(colors.title)
                        .lineLimit(1).privacySensitive()
                    Spacer(minLength: 0)
                    Text(Date(timeIntervalSince1970: Double(item.receivedAt) / 1000).formatted(date: .omitted, time: .shortened))
                        .font(.system(size: 10, weight: .medium)).foregroundStyle(colors.timestamp).lineLimit(1).privacySensitive()
                }
                if let subtitle {
                    Text(subtitle).font(.system(size: 11, weight: .semibold)).foregroundStyle(colors.body)
                        .lineLimit(1).padding(.horizontal, 6).padding(.vertical, 2)
                        .background(colors.body.opacity(0.1), in: Capsule())
                        .overlay(Capsule().strokeBorder(colors.body.opacity(0.22), lineWidth: 0.8)).privacySensitive()
                }
                if let bodyText {
                    Text(bodyText).font(.system(size: 13)).foregroundStyle(colors.body)
                        .lineLimit(height >= 120 ? 3 : height >= 100 ? 2 : 1).privacySensitive()
                }
            }.frame(maxWidth: .infinity, alignment: .leading)
        }.padding(10).frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading)
            .background(colors.gradient, in: RoundedRectangle(cornerRadius: 18))
            .overlay(RoundedRectangle(cornerRadius: 18).strokeBorder(colors.title.opacity(0.1), lineWidth: 0.8))
            .clipShape(RoundedRectangle(cornerRadius: 18))
    }
}

private struct AppIcon: View {
    let appearance: NotificationAppearance
    let name: String
    let size: CGFloat
    var body: some View {
        Group {
            if let path = appearance.imagePath, let image = UIImage(contentsOfFile: path)?.preparingThumbnail(of: CGSize(width: 84, height: 84)) {
                Image(uiImage: image).resizable().scaledToFill()
            } else {
                Text(String(name.prefix(1)).uppercased()).font(.system(size: 15, weight: .semibold))
                    .frame(maxWidth: .infinity, maxHeight: .infinity).background(.white.opacity(0.16))
            }
        }.frame(width: size, height: size)
            .clipShape(RoundedRectangle(cornerRadius: appearance.circle ? size / 2 : size / 4))
            .foregroundStyle(AppearancePalette(appearance).title).privacySensitive()
    }
}

private struct AppearancePalette {
    let background: Color
    let gradient: LinearGradient
    let title: Color
    let body: Color
    let timestamp: Color
    init(_ appearance: NotificationAppearance) {
        let start = Color.appearanceHex(appearance.background)
        background = start
        let end = appearance.useGradient ? appearance.gradient : appearance.background
        gradient = LinearGradient(colors: [start, .appearanceHex(end)], startPoint: .topLeading, endPoint: .bottomTrailing)
        let automatic = Self.automaticText(appearance.background, end)
        title = appearance.autoText ? automatic : .appearanceHex(appearance.titleColor)
        body = appearance.autoText ? automatic : .appearanceHex(appearance.bodyColor)
        timestamp = appearance.autoText ? automatic.opacity(0.8) : .appearanceHex(appearance.timestampColor)
    }
    private static func automaticText(_ background: String, _ end: String) -> Color {
        func luminance(_ hex: String) -> Double {
            let value = UInt32(hex.trimmingCharacters(in: CharacterSet(charactersIn: "#")), radix: 16) ?? 0
            func channel(_ offset: UInt32) -> Double {
                let c = Double((value >> offset) & 255) / 255
                return c <= 0.04045 ? c / 12.92 : pow((c + 0.055) / 1.055, 2.4)
            }
            return 0.2126 * channel(16) + 0.7152 * channel(8) + 0.0722 * channel(0)
        }
        let values = [luminance(background), luminance(end)]
        return 1.05 / (values.max()! + 0.05) >= (values.min()! + 0.05) / (0.005605391624202723 + 0.05) ? .white : .appearanceHex("#111111")
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
    private var families: [WidgetFamily] {
        var result: [WidgetFamily] = [.systemLarge, .systemExtraLarge]
#if NH_EXTRA_LARGE_PORTRAIT
        if #available(iOS 27.0, *) { result.append(.systemExtraLargePortrait) }
#endif
        return result
    }
    var body: some WidgetConfiguration {
        StaticConfiguration(kind: "NotificationHistory", provider: HistoryProvider()) { HistoryWidgetView(entry: $0) }
            .configurationDisplayName("Notification History")
            .description("Choose an app and browse its saved notifications, right here.")
            .supportedFamilies(families)
            .contentMarginsDisabled()
    }
}
