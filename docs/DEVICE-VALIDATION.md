# Device acceptance checks

These checks need a signed build on an actual iOS 27 iPhone. They have not been performed in the Windows implementation workspace. Record the iPhone model, iOS build, signing tool, Apple team, and Git commit when running them. Use synthetic messages; avoid putting private notification content into issues or build logs.

## Install and shared container

1. Download the unsigned `.ipa` directly from the Actions build triggered by a new release tag push. Download the separate signing-info artifact for its validation report and entitlements, then confirm the IPA checksum.
2. Re-sign the main app, `PlugIns/NotificationHistoryWidget.appex`, `Extensions/NotificationHistoryIntents.appex`, and embedded framework. Use matching App Group entitlements and authorized provisioning for both extensions and the app.
3. Preserve all embedded components. Install and launch. The app should show onboarding, rather than a storage error.
4. Reboot and unlock once before testing locked-device capture. Data Protection deliberately prevents access before that first unlock.

## Intent discovery and capture

1. In an ordinary shortcut, search for **Save Notification** under Notification History. Confirm Source App, Title, Subtitle, Message, Received At, and Capture ID are editable. Leave optional fields blank and ensure no prompt is required for them.
2. Set Source App to `Test App`, Title to `Ali`, and Message to `Bro tomorrow jadi? 👋 明天见`. Run with the app closed. Open history and compare all values.
3. Repeat with only a source; then with no fields. Both should save successfully with honest fallback labels.
4. Run the same text twice with no Capture ID. Two entries should appear. Run twice with one identical Capture ID. Only one entry should appear.
5. Configure a Notification trigger for one real source app. Record the actual editor location, automatic-run choices, and all input-variable names and types. Specifically check the source representation, title, subtitle, body/message, timestamp, and event ID. This is the unresolved platform field investigation.
6. Map only the fields actually available on that device. Use a fixed source name if the trigger does not expose an app name. Send notifications with the main app closed, foregrounded, and phone locked after first unlock. Check whether notification preview privacy settings change the supplied content.
7. If the trigger supplies no usable content, record that limitation. The action remains usable with typed text and other supported Shortcut input, but full automatic history has not passed acceptance.

## History and management

1. Use the Debug configuration’s Development tools to insert records for today, yesterday, and an older date. Confirm grouping, scrolling, detail navigation, individual-field copy, and full-notification copy.
2. Search English, Malay, Chinese, emoji, an apostrophe, `%`, and `_`. Combine favorites, app, and date filters and reset them individually. Check that rapid typing does not show results for a previous query.
3. Favorite, unfavorite, delete, and clear. Return to the list and confirm the current state. Cancel the deletion confirmations and confirm no change.
4. Set each retention choice. Insert older entries, including a favorite, and verify cutoff behavior. Restore Never for permanent history.
5. Change System, Light, and Dark, relaunch, and verify persistence. Check smallest supported screen, landscape, safe areas, VoiceOver labels, and large text. Review the screen recording or screenshots for clipped controls.
6. Use at least 10,000 synthetic notifications to assess incremental loading and search latency on the target phone. Confirm scrolling memory stays bounded by displayed pages and body previews; clear afterward.

## Widget and deep links

1. Add both small and medium widgets from the gallery. Verify today’s count, most recent source/title, and three medium rows.
2. Run Save Notification with the app closed. Confirm widget data eventually updates; reload requests remain subject to WidgetKit scheduling.
3. Tap the small widget and each medium row. Confirm the corresponding detail opens on cold and warm app launches. Tap the widget background and confirm history opens.
4. Delete a displayed entry, then tap its stale link. Confirm a clear unavailable message. Test malformed and negative IDs.
5. Clear history and verify the empty widget state. Reboot without unlocking and confirm protected data is not displayed from a new database read. WidgetKit may retain an earlier rendered snapshot; assess Home Screen and Lock Screen exposure separately.

## Failure and concurrency

1. Trigger bursts while refreshing history and while the widget reads. Confirm no missing independent entries or duplicate event tokens.
2. Force-close the app during writes, reopen, and verify integrity through the storage test harness or a debugger using synthetic data. Do not delete SQLite sidecars to “recover” a database.
3. Test low storage, revoked/mismatched App Group entitlements, removed extension, and future schema versions. Errors should surface without silently switching to a separate database.
4. Change timezone and cross a daylight-saving boundary. Counts and date filters should follow local day boundaries; stored times should remain UTC instants.

Do not mark native integration, gallery discovery, closed-app execution, or end-to-end capture verified until these checks succeed.
