import Foundation

struct WidgetAppSummary: Sendable, Identifiable {
    let key: String
    let sourceName: String
    let count: Int
    let appearance: NotificationAppearance
    var id: String { key }
    var displayName: String { appearance.displayName }
}

struct WidgetPageLayout: Sendable {
    static let scopes = ["large", "wide", "portrait"]
    let scope: String
    let badgePageSize: Int
    let notificationPageSize: Int
    var settingsKey: String { "widget.navigation." + scope }
}

enum WidgetNavigationAction: String, CaseIterable, Sendable {
    case selectApp, previousApps, nextApps, previousNotifications, nextNotifications
}

struct WidgetNavigation: Codable, Sendable {
    var selectedSourceKey: String?
    var badgePage = 0
    var notificationPage = 0

    static func pageCount(_ count: Int, size: Int) -> Int {
        // Avoid overflow for untrusted stored page numbers and very large counts.
        count == 0 ? 1 : (count - 1) / max(1, size) + 1
    }
    mutating func resolve(apps: [WidgetAppSummary], layout: WidgetPageLayout) {
        if !apps.contains(where: { $0.key == selectedSourceKey }) {
            selectedSourceKey = apps.first?.key; badgePage = 0; notificationPage = 0
        }
        badgePage = min(max(0, badgePage), Self.pageCount(apps.count, size: layout.badgePageSize) - 1)
        let count = apps.first(where: { $0.key == selectedSourceKey })?.count ?? 0
        notificationPage = min(max(0, notificationPage), Self.pageCount(count, size: layout.notificationPageSize) - 1)
    }
    mutating func apply(_ action: WidgetNavigationAction, sourceKey: String?, apps: [WidgetAppSummary], layout: WidgetPageLayout) {
        resolve(apps: apps, layout: layout)
        switch action {
        case .selectApp:
            if let index = apps.firstIndex(where: { $0.key == sourceKey }) {
                selectedSourceKey = apps[index].key; notificationPage = 0
                badgePage = index / layout.badgePageSize
            }
        case .previousApps: badgePage = max(0, badgePage - 1)
        case .nextApps:
            badgePage = min(badgePage + 1, Self.pageCount(apps.count, size: layout.badgePageSize) - 1)
        case .previousNotifications: notificationPage = max(0, notificationPage - 1)
        case .nextNotifications:
            let count = apps.first(where: { $0.key == selectedSourceKey })?.count ?? 0
            notificationPage = min(notificationPage + 1, Self.pageCount(count, size: layout.notificationPageSize) - 1)
        }
        resolve(apps: apps, layout: layout)
    }
}

struct WidgetHistorySnapshot: Sendable {
    let apps: [WidgetAppSummary]
    let appCount: Int
    let selectedApp: WidgetAppSummary?
    let navigation: WidgetNavigation
    let badgePageCount: Int
    let notificationPageCount: Int
    let notifications: [CapturedNotification]
    let theme: String
}

extension NotificationAppearance {
    static func defaultProfile(for source: String) -> NotificationAppearance {
        let name = source.trimmingCharacters(in: .whitespacesAndNewlines)
        let colors: (String, String)
        switch name.uppercased() {
        case "WHATSAPP": colors = ("#075E54", "#128C7E")
        case "INSTAGRAM": colors = ("#8B2868", "#B84376")
        case "LAZADA": colors = ("#282D80", "#39277F")
        case "FACEBOOK": colors = ("#173F78", "#242426")
        default: colors = ("#242426", "#171719")
        }
        return NotificationAppearance(displayName: name.isEmpty ? "Unknown app" : name,
            background: colors.0, gradient: colors.1, useGradient: true,
            titleColor: "#FFFFFF", bodyColor: "#FFFFFF", timestampColor: "#D1D5DB",
            autoText: true, imagePath: nil, circle: false)
    }
}
