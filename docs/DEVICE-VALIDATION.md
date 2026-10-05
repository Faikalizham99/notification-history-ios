# Device acceptance checks

These checks need a signed build on an actual iOS 27 iPhone. They have not been performed in the Windows implementation workspace. Record the iPhone model, iOS build, signing tool, Apple team, and Git commit when running them. Use synthetic messages; avoid putting private notification content into issues or build logs.

## Install and shared container

1. Download the unsigned `.ipa` directly from the Actions build triggered by a new release tag push. Download the separate signing-info artifact for its validation report and entitlements, then confirm the IPA checksum.
2. Re-sign the main app, `PlugIns/NotificationHistoryWidget.appex`, `Extensions/NotificationHistoryIntents.appex`, and embedded framework. Use matching App Group entitlements and authorized provisioning for both extensions and the app.
   If the signer renames App Groups, confirm that the installed app and both extensions share at least one authorized identifier. The app and extensions prefer the original group if common, otherwise select the first common identifier in ordinal order. Verify extension-specific application identifiers and provisioning profiles; main-app installation alone does not validate either extension.
3. Preserve all embedded components. Install and launch. The app should show onboarding, rather than a storage error.
   Import the unsigned IPA in FlareStore, sign it, and install through FlareStore. Test both importing and installation; a Files preview failure alone is not an installation test. If installation fails, record the detailed installer error and retain that newly signed IPA for local inspection.
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
   Hold a notification card and confirm no gray rectangular highlight appears, including the space below rounded card corners. Tap the same card repeatedly, navigate back, scroll, refresh, and load additional pages; detail navigation should happen once per tap. Check tappable Apps rows and read-only copy boxes also avoid a selection overlay.
   History cards show a provided subtitle between title and message. Check subtitle plus message, subtitle only, and null/empty/whitespace subtitles: missing subtitles must leave no blank row, and subtitle-only cards must show the subtitle once. Check the appearance preview uses the same layout.
3. Favorite, unfavorite, delete, and clear. Return to the list and confirm the current state. Cancel the deletion confirmations and confirm no change.
   Swipe a history card left to reveal its red Delete action. Tap Delete and test both Cancel and Delete in the confirmation. Only the chosen notification should be removed, including in filtered/favorites views and the widget after refresh. A full swipe alone must not delete. Opening a second card's swipe should close the first; tapping an open card should close its swipe before navigating. Verify vertical scrolling, pull-to-refresh, paging, subtitles, and the absence of gray selection highlights still work.
   Confirm detail field captions sit above outlined read-only value boxes, empty fields show Not provided, and tapping any provided value or its Copy indicator copies the exact value. In Malaysia, the time must include actual hours/minutes/seconds and UTC+08:00, following the phone's time format. Check both 12- and 24-hour formats.
4. Set each retention choice. Insert older entries, including a favorite, and verify cutoff behavior. Restore Never for permanent history.
5. Change System, Light, and Dark, relaunch, and verify persistence. Check smallest supported screen, landscape, safe areas, VoiceOver labels, and large text. Review the screen recording or screenshots for clipped controls.
   Settings retention and theme selectors must have visible outlines and dropdown arrows in each theme. Tap both the selected value and the surrounding selector to open its choices.
6. Use at least 10,000 synthetic notifications to assess incremental loading and search latency on the target phone. Confirm scrolling memory stays bounded by displayed pages and body previews; clear afterward.

## App appearance and photo cropping

1. Upgrade over a version-1 installation with the same signing identity. Confirm history, favorites, settings, and Shortcut saves remain available.
2. Open Apps, edit a discovered app, and add an app before capturing its first notification. Verify source matching across case/whitespace, separate display names, and duplicate-source rejection.
3. Select portrait, landscape, rotated/HEIC, and transparent PNG photos. Drag, pinch, Reset, Cancel, and Use. Confirm the saved 512-pixel icon matches the crop frame; Crop again should use the larger resized source.
4. Change presets, circular color wheel colors, brightness, hex values, gradient toggle, automatic text, and all manual text colors. Drag through the wheel center and edge, including beyond the edge; verify Cancel restores the old color and Use keeps the chosen color. Enter both three- and six-digit hex values without premature rewriting. Compare the preview with the saved history card. Try invalid hex and empty names without losing the existing appearance.
5. Cancel the photo picker, crop, and page. Confirm the previous appearance remains. Replace/remove photos, relaunch, and check persistence. Clear history and verify app photos survive; remove appearance and verify default cards return.
6. Check the smallest supported phone, landscape, large text, VoiceOver, long app names/messages, fixed Save/Cancel header, scrolling, and keyboard-safe color/source fields. Compare rounded/circular masks with the crop frame.
7. Verify saved appearance on both widget sizes and captured records with the app closed. Profile changes must not alter stored notification content; widget reload timing is controlled by iOS.
8. Select Light, Dark, then System in Settings. Check the Apps list, appearance editor, display/source inputs, shape picker, all hex fields, circular color picker, and photo/crop screens. Text, placeholders, backgrounds, and buttons must remain readable; System should respond to an iOS appearance change while a MAUI editor is open. Custom notification card colors should remain unchanged.
9. Verify visible outlines on display/source names, icon shape, all five appearance hex values, color-wheel hex, swatches, Apps rows, history search/date filter, and development source/title/subtitle/message/date inputs. Focus an editable field and confirm its teal outline; unfocus and confirm the normal outline returns. Retention/theme selectors and notification detail copy boxes should use the same shared border style. Check long names, narrow screens, large text, and keyboard scrolling.

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
4. Re-sign a fresh build with reordered/renamed App Group entitlements. Confirm the app opens, Save Notification writes with the app closed, and both widget sizes read the same history. Mismatched groups must report the signing issue rather than suggesting only low disk space.
5. Change timezone and cross a daylight-saving boundary. Counts and date filters should follow local day boundaries; stored times should remain UTC instants.

Do not mark native integration, gallery discovery, closed-app execution, or end-to-end capture verified until these checks succeed.
