import Foundation
import SQLite3

enum WidgetStorageTests {
    static func run(directory: URL, schema: URL) throws {
        let file = directory.appendingPathComponent("widget-" + UUID().uuidString + ".sqlite3")
        let now = Date(), layout = WidgetPageLayout(scope: "large", badgePageSize: 4, notificationPageSize: 2)
        do {
            let writer = try SharedDatabase(testingPath: file, schemaURL: schema)
            for app in 0..<10 {
                for item in 0..<4 {
                    _ = try writer.save(source: String(format: "App %02d", app), title: "Title \(item)", subtitle: "Work",
                        body: String(repeating: "Message 👋 ", count: 40), receivedAt: now.addingTimeInterval(Double(item)))
                }
            }
            _ = try writer.save(source: " app 00 \n", title: "Variant", subtitle: nil, body: "Case and whitespace", receivedAt: now)
            _ = try writer.save(source: " 应用 ", title: "Unicode", subtitle: nil, body: "测试", receivedAt: now)
            _ = try writer.save(source: "应用", title: "Unicode 2", subtitle: nil, body: "第二", receivedAt: now)
            _ = try writer.save(source: "O'Brian % _", title: "Quoted", subtitle: nil, body: "Bound source key", receivedAt: now)
            _ = try writer.save(source: "Expired", title: "Old", subtitle: nil, body: nil, receivedAt: now.addingTimeInterval(-8 * 86_400))
        }
        try sql(file, "INSERT INTO Settings VALUES('retention','7')")
        try sql(file, "INSERT INTO AppProfiles(SourceKey,SourceName,DisplayName,BackgroundColor,GradientColor,TitleColor,BodyColor,TimestampColor) VALUES('Z EMPTY','Z empty','Z empty','#075E54','#128C7E','#FFFFFF','#FFFFFF','#D1D5DB')")
        func read(_ pages: WidgetPageLayout? = nil) throws -> WidgetHistorySnapshot {
            try SharedDatabase(readOnly: true, testingPath: file).widgetSnapshot(layout: pages ?? layout, now: now)
        }
        func act(_ action: WidgetNavigationAction, _ key: String? = nil, _ pages: WidgetPageLayout? = nil) throws {
            try SharedDatabase(testingPath: file, schemaURL: schema).updateWidgetNavigation(layout: pages ?? layout, action: action, sourceKey: key, now: now)
        }
        var page = try read()
        guard page.appCount == 13, page.apps.count == 4, page.selectedApp?.key == "APP 00",
              page.selectedApp?.count == 5, page.notifications.count == 2, page.badgePageCount == 4,
              page.notifications.allSatisfy({ ($0.body?.count ?? 0) <= 200 && $0.subtitle == "Work" }) else {
            fatalError("Widget first-app selection, normalized counts, retention, or bounded previews differ")
        }
        let firstIDs = Set(page.notifications.map(\.id))
        try act(.nextNotifications); page = try read()
        guard page.navigation.notificationPage == 1, page.notifications.count == 2,
              firstIDs.isDisjoint(with: page.notifications.map(\.id)) else { fatalError("Widget notification pages overlap") }
        try act(.nextNotifications); try act(.nextNotifications); page = try read()
        guard page.navigation.notificationPage == 2, page.notifications.count == 1 else { fatalError("Widget final notification page is not clamped") }
        try act(.nextApps); page = try read()
        guard page.navigation.badgePage == 1, page.apps.first?.key == "APP 04", page.selectedApp?.key == "APP 00",
              page.navigation.notificationPage == 2 else { fatalError("Badge paging changed app selection or notification paging") }
        try act(.selectApp, "APP 04"); page = try read()
        guard page.selectedApp?.key == "APP 04", page.navigation.notificationPage == 0,
              page.notifications.allSatisfy({ $0.source == "App 04" }) else { fatalError("Badge selection does not isolate the app") }
        try act(.nextApps); try act(.nextApps); try act(.nextApps); page = try read()
        guard page.navigation.badgePage == 3, page.apps.count == 1 else { fatalError("Partial last badge page or upper boundary failed") }
        try act(.previousApps); page = try read()
        guard page.navigation.badgePage == 2, page.selectedApp?.key == "APP 04" else { fatalError("Previous badge page lost selection") }
        try act(.selectApp, "应用"); page = try read()
        guard page.selectedApp?.count == 2, page.notifications.count == 2 else { fatalError("Unicode app filtering or counts failed") }
        try act(.selectApp, "O'BRIAN % _"); page = try read()
        guard page.notifications.first?.title == "Quoted" else { fatalError("Quoted source key was not bound safely") }
        let portrait = WidgetPageLayout(scope: "portrait", badgePageSize: 3, notificationPageSize: 5)
        guard try read(portrait).selectedApp?.key == "APP 00", try read().selectedApp?.key == "O'BRIAN % _" else {
            fatalError("Widget families share navigation state unexpectedly")
        }
        try sql(file, "DELETE FROM Notifications WHERE SourceApp='O''Brian % _'")
        page = try read()
        guard page.selectedApp?.key == "APP 00", page.navigation.notificationPage == 0, page.navigation.badgePage == 0 else {
            fatalError("Deleted selected app does not return to the first app")
        }
        try sql(file, "UPDATE Settings SET Value='{\"selectedSourceKey\":\"APP 00\",\"badgePage\":9223372036854775807,\"notificationPage\":9223372036854775807}' WHERE Key='widget.navigation.large'")
        page = try read()
        guard page.navigation.badgePage == 2, page.navigation.notificationPage == 2 else { fatalError("Oversized persisted pages were not clamped") }
        try sql(file, "UPDATE Settings SET Value='broken JSON' WHERE Key='widget.navigation.large'")
        guard try read().navigation.notificationPage == 0 else { fatalError("Malformed widget settings broke history reads") }
        do {
            let writer = try SharedDatabase(testingPath: file, schemaURL: schema)
            _ = try writer.save(source: nil, title: nil, subtitle: "Subtitle only", body: nil, receivedAt: now)
            _ = try writer.save(source: " \n", title: nil, subtitle: nil, body: nil, receivedAt: now)
        }
        try act(.selectApp, ""); page = try read()
        guard page.selectedApp?.key == "", page.selectedApp?.count == 2, page.notifications.count == 2 else {
            fatalError("Unknown/blank source app cannot be selected")
        }
        var rejected = false
        do {
            _ = try SharedDatabase(readOnly: true, testingPath: file).widgetSnapshot(
                layout: WidgetPageLayout(scope: "large", badgePageSize: 0, notificationPageSize: Int.max), now: now)
        } catch { rejected = true }
        guard rejected else { fatalError("Invalid widget page capacities accepted") }
        try sql(file, "DELETE FROM Notifications")
        page = try read()
        guard page.appCount == 1, page.selectedApp?.key == "Z EMPTY", page.selectedApp?.count == 0, page.notifications.isEmpty else {
            fatalError("Configured zero-count apps disappeared after history was cleared")
        }
        var state = WidgetNavigation(selectedSourceKey: "missing", badgePage: Int.max, notificationPage: Int.max)
        state.resolve(apps: [], layout: layout)
        guard state.selectedSourceKey == nil, state.badgePage == 0, state.notificationPage == 0 else { fatalError("Empty catalog navigation is invalid") }
        print("PASS widget badge/notification paging, selection, retention, Unicode, family isolation, deletion and corrupt-state recovery")
    }
    private static func sql(_ file: URL, _ query: String) throws {
        var handle: OpaquePointer?
        guard sqlite3_open(file.path, &handle) == SQLITE_OK else { throw HistoryError.database }
        defer { sqlite3_close(handle) }
        guard sqlite3_exec(handle, query, nil, nil, nil) == SQLITE_OK else { throw HistoryError.database }
    }
}
