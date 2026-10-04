import WidgetKit
import Foundation

@_cdecl("nh_reload_widgets")
public func reloadNotificationHistoryWidgets() {
    DispatchQueue.main.async { WidgetCenter.shared.reloadAllTimelines() }
}
