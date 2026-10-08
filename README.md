# Notification History

An iOS-first .NET 10 MAUI app for personal notification history. It stores the optional text supplied by a Shortcut, lets you search and manage that history, and provides large and extra-large interactive native widgets. It contains no global notification listener, notification-service interception, cloud sync, analytics, or network upload code.

**Implementation status:** shared persistence checks pass on Windows and macOS. Native Swift framework/extension builds, concurrent Swift/.NET storage interoperability, and the Release iOS device app build have passed in GitHub Actions. Signed installation, intent discovery, widget behavior, and notification-field mappings still require validation on an iPhone. See [device acceptance checks](docs/DEVICE-VALIDATION.md).

## Apple platform investigation

Reviewed public Apple documentation on **4 October 2026**. The current guide documents a Notification trigger with an app selector and Title, Subtitle, and Message filtering. It does **not** specify a reliable output-variable contract for downstream actions. The WWDC demonstration confirms notification-driven automation, but is insufficient to prove individual field mappings. Some presentation text/version labels differ, so the app deliberately avoids promising one exact editor navigation sequence. Sources: [Apple trigger guide](https://support.apple.com/guide/shortcuts/event-triggers-apd932ff833f/ios), [What’s new in Shortcuts](https://developer.apple.com/videos/play/wwdc2026/310/).

| Question | Finding and implementation |
| --- | --- |
| Source app exposed? | App selection is documented. A passed display name, bundle ID, or other representation is not confirmed. Use a fixed Source App string for each automation. |
| Title exposed? | Title filtering is documented; downstream variable availability remains unverified. Optional text parameter. |
| Subtitle exposed? | Subtitle filtering is documented; downstream availability remains unverified. Optional text parameter. |
| Body/message exposed? | Message filtering is documented; downstream availability remains unverified. Optional Message parameter. |
| Direct receipt by App Intents? | App Intents expose typed action parameters. Shortcuts must map usable values into them; the intent does not automatically receive third-party notification objects. |
| Can capture with MAUI closed? | Apple documents background execution in an App Intents extension. Save Notification lives in its own native extension and does not open the UI. Actual device acceptance remains pending. |
| Separate extension required? | Apple permits intents in the main app or an extension. This project chooses the extension to keep Swift action discovery and execution independent of the MAUI process. |
| Entitlements? | All three processes require the same authorized App Group. Data Protection is also declared. No SiriKit, push-notification, or background-monitoring entitlement is used. |
| Shared SQLite safe? | SQLite supports concurrent local processes with WAL; one writer proceeds at a time. Transactions and a busy timeout coordinate access. Widget timelines open read-only; paging intents take the shared writer lock. |
| Specific widget links? | WidgetKit supports `widgetURL` and `Link`. The main app registers a custom URL scheme and handles missing/deleted entries. |

Relevant Apple references: [App Intents extensions](https://developer.apple.com/documentation/appintents/app-extension), [intent runtime behavior](https://developer.apple.com/documentation/appintents/configuring-the-runtime-behavior-of-your-app-intents), [App Groups](https://developer.apple.com/documentation/xcode/configuring-app-groups), [Data Protection](https://support.apple.com/guide/security/app-protection-and-app-groups-sec1a976c067/web), [widget links](https://developer.apple.com/documentation/widgetkit/linking-to-specific-app-scenes-from-your-widget-or-live-activity).

**Unresolved device facts:** the source representation, available notification input variables, original arrival timestamp/event ID, exact automation editor labels, and automatic execution choices. These are explicitly acceptance tests, not fabricated capabilities. If a field is unavailable, leave it blank. If all content is unavailable, the app can record a named event or accept manually supplied Shortcut text, but it cannot recover hidden notification content.

## Requirements

- .NET SDK **10.0.201**, pinned by `global.json`; MAUI Controls **10.0.20**.
- Workload set **10.0.204.1**, `maui-ios`. Native/IPA builds require macOS and matching **Xcode 26.4/26.4.1**, plus XcodeGen (`brew install xcodegen`). The workflow resolves `/Applications/Xcode_26.4.app` to its physical directory, exports `DEVELOPER_DIR`, and checks the asset compiler before building. This avoids [.NET's asset-compiler lookup failure through an Xcode symlink](https://github.com/dotnet/macios/issues/21762). It fails if the matching Xcode is unavailable; update SDK, workload, and Xcode together when runner images change. [Runner inventory](https://github.com/actions/runner-images/blob/main/images/macos/macos-26-Readme.md).
- Deployment target iOS **17+** for storage/UI/intents/widgets; the requested notification automation experience must be tested on **iOS 27**. The iPhone Extra-large Portrait widget uses an iOS 27-only API behind a runtime availability check and a separate native SDK build flag.
- Physical iPhone and a signing setup that retains extensions and authorizes App Groups. An unsigned IPA is an input to signing, not an installable app.
- GitHub Actions enabled on this repository for macOS builds. No signing secrets are needed for the default unsigned pipeline.

## Architecture

```text
Third-party notification
  → user-configured Shortcuts Notification trigger
  → mapped available text / fixed source name
  → Save Notification (native App Intents extension)
  → App Group / Library / NotificationHistory / history.sqlite3
     ↙                                         ↘
MAUI history, details, settings           WidgetKit read-only snapshots
```

The authoritative database and settings live in one shared container. There are no separate history databases or JSON snapshots. On iOS both C# and Swift use the system SQLite engine. Desktop tests use the SQLite package’s bundled engine. The app and native extensions read App Group IDs from the installed executable signatures and intersect all three sets. They prefer `group.com.faikal.notificationhistory` when it remains authorized everywhere; otherwise, they choose the same first identifier in ordinal order from the common set. This supports signing services that rewrite App Groups. Only signature metadata is read, without loading executable code or uploading signing data. iOS still enforces container authorization. If a component is missing, the signatures share no group, or the selected container cannot be accessed, the app reports the signing problem; it does not silently create private fallback history.

`shared/schema.sql` is embedded in C# and copied into both extensions. Schema version 2 adds `AppProfiles` inside `BEGIN IMMEDIATE`; existing version-1 history, settings, favorites, and capture IDs are preserved. Both writers perform the same migration. The read-only widget accepts versions 1 and 2 and uses custom appearance when available. Unknown newer schemas are rejected.

## App photos and colors

Open **Apps** from history, or **Settings → App photos and colors**. Sources already captured appear automatically; **Add** also allows configuring a source before its first notification. Match the Source App value in your Shortcut (case and surrounding whitespace are ignored), and choose a display name separately. Appearance changes restyle existing and future cards without changing saved message contents.

The editor includes a live preview using the same card component as history, a circular color wheel with drag-to-select hue/saturation and a brightness slider, hex values, color presets, solid/gradient backgrounds, automatic or custom text colors, and rounded/circular icons. Tap a color swatch to open the wheel; **Cancel** restores its previous color and **Use** keeps the chosen color in your draft. **Change Icon** uses Apple's selected-photo picker. The full-screen crop editor supports drag, pinch, Reset, Cancel, and Use. **Crop again** reopens the resized source rather than repeatedly cropping the small final icon. The source is reduced to at most 2048 pixels per side and the saved crop is 512 × 512 pixels.

Photo edits remain in memory until **Save**. Profiles are local SQLite records; PNG crops and resized sources live in `Library/NotificationHistory/Icons` in the same protected, backup-excluded App Group. No image is uploaded or duplicated per notification. Failed saves remove new files; unused icons from replacements/interrupted saves are cleaned during profile loading. Clearing history retains app appearance. Removing custom appearance removes its unused photos and restores default cards. Widget rows use saved display names, colors, and cropped icons; WidgetKit controls refresh timing.

Each app editor also has **Open app URL** and **Test opening**. Enter a full custom app-opening URL (such as `appscheme://`) or an HTTPS link supported by the installed app, then Save. Testing launches the draft link without saving it. Widget notification taps first activate Notification History, resolve the notification's source, and launch its currently saved app link. Missing, unsupported or failed links fall back to saved notification details; HTTPS links use the iOS universal-links-only option so failure does not open a browser. Badge taps still switch the displayed app. The source app controls where its link lands; no original notification destination is captured or reconstructed. Links live in the existing Settings table keyed by normalized source, with profile save/rename/removal in one transaction. Shared schema version 2 is unchanged. Reset appearance keeps the link; removing custom appearance removes it. Existing `notificationhistory://notification/ID` links still open local details.

The original source/title/body/time remain available in notification details. The global System/Light/Dark setting controls the Apps list, appearance editor, input fields, color wheel, and photo/crop screens as well as history and settings. Custom notification card colors remain the colors you chose. The working user-edited IPA workflow is preserved.

Input fields share a visible outline in both themes, with a teal focus outline while editing. This covers display/source names, appearance hex fields, the color wheel's hex field, history search/date filtering, and development source/title/subtitle/message/date inputs. Icon shape, retention, theme, and date selectors have dropdown arrows. Color swatches and tappable Apps rows also have outlines. Notification details keep their outlined read-only copy boxes; ordinary buttons, switches, and the brightness slider retain their existing recognizable control shapes.

Text inputs use shared light/dark styling, a single inline clear button where space allows, and iOS's keyboard Done toolbar. Compact appearance hex fields omit the clear button to keep the whole value visible. Tap outside an input or use Done to dismiss the keyboard without clearing the value; multiline message inputs still accept line breaks. History search uses a labeled Cancel button to clear the query and dismiss the keyboard while preserving other filters. Form Cancel buttons retain their draft-discard behavior. Native Add app prompts and photo/crop pickers retain their existing labeled Cancel controls.

History cards open through tap gestures instead of native row selection, preventing the gray selection rectangle on a held card. Collection views default to no selection app-wide. Card tapping, scrolling, pull-to-refresh, and incremental loading remain available.

Swipe a history card left to reveal **Delete**, then tap it and confirm. Cancel keeps the notification. Deleting refreshes history and requests a widget update; the existing delete action in notification details remains available. Only one card's swipe stays open at a time.
Cards have side gutters to separate them from the scroll indicator. Provided subtitles use a smaller bold, rounded label above the message, shared with the appearance preview. The red swipe action follows the card's rounded shape with a small gap between them.

Tap a truncated message body to expand it inside the card; tap the expanded body again to collapse. Only shortened messages toggle, including messages clipped by the three-line card limit. Tapping the title, icon or other card areas opens details. Expansion reads the complete stored body on demand and releases that extra text when collapsed.

App badges sit directly below the filters and remain fixed while history scrolls. Each badge uses that app's configured photo and colors and shows its total saved notification count. Scroll the badges horizontally and tap one to switch apps; the selected badge has an outline and checkmark. Apps are ordered alphabetically by display name, with the first app selected on a fresh launch. Search, favorites, date filtering, and paging apply within the selected app. Choose **All apps** in the source filter for combined history. Configured apps remain visible with zero counts after clearing history; unconfigured apps appear automatically when captured. Counts include all saved records, rather than unread records or only current search results.

In the app, long-press a nonempty badge to replace its count with a red **X**, then tap it to confirm clearing that app's non-favorites. Favorites, photos, colors and opening links stay intact. Cleanup covers that source's entire history, including records outside the current search/date/favorites filters and normalized source-name variants captured since loading the badges. Cancel, tap elsewhere, scroll, change filters or leave the page to restore the normal badge. Releasing the hold alone never opens the confirmation. Configured badges remain at zero; an unconfigured app with no remaining history disappears. Widget badges continue to filter only and offer no deletion action.

Persistence uses WAL, `synchronous=FULL`, a 5-second busy timeout, prepared parameters, short-lived connections, explicit insert/cleanup transactions, and automatic checkpoints. Widget timelines read app counts, appearance, navigation state, and a bounded notification page in one short read transaction. Widget navigation intents write only their selected app and page settings using the same shared writer lock as notification saves; they never change notifications. The widget does not continuously execute. Save failures propagate to Shortcuts; there is no guaranteed OS-level retry. Do not remove `-wal` or `-shm` files from a live database or copy only the main file for backup. [SQLite WAL documentation](https://sqlite.org/wal.html).

On Apple platforms, C# and Swift also acquire the same POSIX `flock` before opening a writable connection and release it after closing that connection. This serializes writes and close-time checkpoints across processes, protecting older system engines against the documented WAL-reset race. The OS releases locks after a process crash; a leftover `.lock` file is harmless. Read-only widget connections need no writer lock and can read during saves. The lock wait is bounded to five seconds. [SQLite race details](https://sqlite.org/wal.html#walreset).

Records use integer UTC Unix milliseconds for ReceivedAt/CreatedAt. Local display and local-calendar day bounds handle timezone and daylight-saving changes. Retention means elapsed 24-hour days for non-favorite notifications; cleanup runs on app use and intent saves. Favorites survive retention and **Clear non-favorites**, and remain visible in widget cards and app counts even after the retention period. Unfavoriting an expired notification makes it eligible for the next cleanup. Individual confirmed deletion can still remove a favorite. No continuous background cleanup is promised.

Source, title, subtitle, and body are nullable. Empty records receive honest fallback labels. Full bodies are retained; list queries fetch 181 characters to detect overflow, then display body previews of up to 180 characters with an ellipsis. Expanding a card or opening details reads its complete body by ID. Widget body queries fetch at most 1,024 characters and show as many complete lines as each card can fit. The list uses 60-row keyset pages ordered by timestamp and ID, avoiding deep offset scans. Date/source/favorite indexes support filters. Debounced, cancellable background substring search covers all four text fields, escapes `%`/`_`, and uses bound parameters. FTS was omitted because token matching differs from substring search. Unicode is preserved; SQLite’s default case-insensitive `LIKE` is primarily ASCII. Very large histories may justify a later indexed search design after device measurement.

Identical text is never discarded. An optional **Capture ID** provides transactional retry idempotence only when the caller actually has a unique event identifier. Leave it blank otherwise. Received At is also optional; absent an original timestamp, capture time is used.

## Capture diagnostics

For intermittent Shortcut failures, open **Settings → Capture diagnostics** after an error and tap **Refresh**. Compare the time with the failed automation, then use **Copy diagnostics** to copy a report. The page shows the latest 50 native action attempts, the last execution stage, whether the database write committed, and categorized error codes. Times on screen follow the device time zone; copied reports use UTC. An unfinished attempt may still be running, may have been interrupted, or may have failed to update its diagnostic file. A saved attempt means the write completed; it does not prove Shortcuts received the final response, or that retention/deletion kept the notification afterward. No matching record is inconclusive: the extension may not have started, input resolution may have failed, or diagnostics storage may have been unavailable.

Diagnostics contain random attempt IDs, timestamps, stages, save status, and numeric error codes. They exclude notification content, source app names, sender names, filenames, Capture IDs, arbitrary error descriptions, and file paths. Records remain local in the protected, backup-excluded App Group and are separate from SQLite, so a database error can still be inspected. Diagnostic writes are best effort and never replace a successful capture with an error. The native action also writes the same metadata to the system log. Older records are pruned beyond 50; attempts from the last five minutes are temporarily retained to avoid removing recent actions. **Clear diagnostics** removes diagnostic records without clearing history. Active actions may write a record again. Captures are not automatically retried, since startup failures cannot be retried inside an action that never runs, and blindly repeating an automation could duplicate saved entries.

## Project structure

| Path | Responsibility |
| --- | --- |
| `src/NotificationHistory.Core` | Models, async SQLite access, filters, retention, deep-link parsing |
| `src/NotificationHistory` | MAUI app, DI, view models, history XAML, details/settings/onboarding, iOS lifecycle |
| `shared/schema.sql` | Single shared schema contract |
| `native/Shared` | Swift SQLite adapter and common entitlements |
| `native/NotificationHistoryIntents` | Save Notification action and ExtensionKit entry point |
| `native/NotificationHistoryWidget` | Large/extra-large WidgetKit views, paging intents and timeline provider |
| `native/NotificationHistoryBridge` | Small C ABI function for MAUI to request widget reloads |
| `native/project.yml` | Versioned XcodeGen project specification for the native targets |
| `tests` | Managed persistence checks and native/managed cross-process tests |
| `scripts` | Native build, shared-storage tests, IPA packaging and validation |

Views contain presentation/navigation event handling; view models handle list state and management operations. The database runs SQLite work off the UI thread. Debug tools exist only under `#if DEBUG`, and do not insert anything automatically.

## Local development

On Windows, these are **managed compilation checks**, not an executable iOS `.app` build:

```powershell
dotnet workload install maui-ios --version 10.0.204.1
dotnet restore tests/NotificationHistory.Tests
dotnet run --project tests/NotificationHistory.Tests -c Release
dotnet restore src/NotificationHistory -r iossimulator-x64
dotnet build src/NotificationHistory -c Release -r iossimulator-x64 -p:EnableCodeSigning=false -p:TreatWarningsAsErrors=true
```

On macOS, build native components before the MAUI app:

```bash
dotnet workload install maui-ios --version 10.0.204.1
brew install xcodegen
export APP_VERSION=1.0.0 BUILD_NUMBER=1.0.0
bash scripts/build-native.sh
bash scripts/test-native-storage.sh
dotnet restore src/NotificationHistory -r ios-arm64
dotnet build src/NotificationHistory -c Release -r ios-arm64 \
  -p:EnableCodeSigning=false -p:ApplicationDisplayVersion="$APP_VERSION" \
  -p:ApplicationVersion="$BUILD_NUMBER" \
  -p:NativeBuildDir="$PWD/artifacts/native/Build/Products/Release-iphoneos"
python3 scripts/package-ipa.py \
  src/NotificationHistory/bin/Release/net10.0-ios/ios-arm64/NotificationHistory.app \
  --version "$APP_VERSION" --build "$BUILD_NUMBER" \
  --output artifacts/NotificationHistory-v1.0.0-ios-arm64.ipa
```

For simulator UI testing, build native products with `CONFIGURATION=Debug NATIVE_SDK=iphonesimulator bash scripts/build-native.sh`, then build MAUI in Debug with `iossimulator-arm64` on Apple Silicon (or `iossimulator-x64` on Intel), passing the absolute `Debug-iphonesimulator` NativeBuildDir. Simulator App Group behavior is not proof of device provisioning. Use `xcrun simctl install booted <app-path>` and `xcrun simctl launch booted com.faikal.notificationhistory` after building. For physical-device UI tests, re-sign/install the Debug app using your setup; Settings → Development tools allows custom source/title/subtitle/message/date, sample insertion, and deep-link testing. Release builds omit that page and its sample text.

The cross-storage script seeds via C#, reads/writes via Swift, runs C# and Swift writers simultaneously, checks native widget snapshots, then verifies Unicode and integrity in C#. Earlier storage checks have passed on macOS in GitHub Actions. Newly added interactive widget storage cases, native compilation, and signed-device rendering remain pending. `SkipNativeIntegration=true` exists only to allow macOS managed compilation checks; such a build must not be installed or packaged as complete.

## Shortcuts setup

1. First test an ordinary shortcut with **Save Notification**, typed values, and blank optional fields. Run it with the main app closed.
2. Locate the automation controls for your iOS version. Add the documented **Notification** trigger and select the source app. The current Apple presentation shows automation controls in the shortcut editor; do not rely on an older Personal Automation screen sequence.
3. Leave Title/Subtitle/Message filters empty unless you deliberately want fewer arrivals.
4. Add **Save Notification**. Enter a fixed **Source App**, such as WhatsApp.
5. Inspect the actual input variables on your phone. Map a usable title → Title, subtitle → Subtitle, and message/body → Message only when provided. Do not infer variables from trigger filter labels. Blank fields are valid.
6. Choose automatic execution if your trigger offers it. Send a real notification and inspect the stored entry. Repeat for each source app.

The app cannot install personal automations for you, bypass preview redaction, reconstruct past notifications, or guarantee that iOS executes every arrival. If the trigger is absent or supplies no useful data, use ordinary shortcuts with text until device/platform support is established. No global listener fallback is implemented.

## Widget setup

Add Notification History through the Home Screen widget gallery. Supported sizes are **Large**, **Extra-large** on iPad, and **Extra-large Portrait** on iOS 27. Small and Medium are no longer offered; remove an old widget and add a larger one after upgrading. The widget uses consistent 18-point outer padding, gutters for badge outlines, the app's Light/Dark/System setting, and rounded notification cards with your configured photos/colors, optional subtitle labels, and device-local times. The number of cards adapts to available height; wide iPad widgets use two columns. Last partial pages retain the same card heights.

The badge row shows four apps per page, or three on narrow widgets. Its **Apps** arrows page through more badges without changing the active app. Tap a badge to show that app's notifications and reset its notification page. Separate **Notifications** arrows browse older/newer pages; page indicators and disabled boundary buttons distinguish the controls. The first alphabetical app is selected initially. Selection and pages persist independently for each widget size; multiple widgets of the same size share this state. Counts cover retained saved records, not unread notifications. Configured apps remain available with zero counts, and removal of a selected source falls back to the first app. Interactions refresh the widget without opening the main app; free gesture scrolling is not supported by WidgetKit. [Apple's interactive widget guidance](https://developer.apple.com/documentation/widgetkit/adding-interactivity-to-widgets-and-live-activities).

Reloads are requested after saves, favorites, deletion, and cleanup. A 15-minute timeline policy is a request subject to WidgetKit's budget, not an exact interval. [WidgetKit update scheduling](https://developer.apple.com/documentation/widgetkit/keeping-a-widget-up-to-date).

Widget notification card taps use `notificationhistory://source-app/123` to open the configured source app or fall back to saved details; background taps use `notificationhistory://history`. In-app cards and existing `notificationhistory://notification/123` links open saved details. Cold launch links are queued until the navigation window is ready. Invalid/deleted IDs show an unavailable message. Widget preview content is marked privacy-sensitive, but prior system-rendered snapshots can still persist; use only widgets you are comfortable displaying.

## Identifiers and configuration

| Component | Identifier / placement |
| --- | --- |
| App | `com.faikal.notificationhistory` |
| Widget | `com.faikal.notificationhistory.widget`, `PlugIns/NotificationHistoryWidget.appex` |
| Intent | `com.faikal.notificationhistory.intents`, `Extensions/NotificationHistoryIntents.appex` |
| Bridge | `com.faikal.notificationhistory.bridge`, `Frameworks/NotificationHistoryBridge.framework` |
| Shared App Group | `group.com.faikal.notificationhistory` |
| URL scheme | `notificationhistory` |

The widget uses `NSExtensionPointIdentifier=com.apple.widgetkit-extension`. The App Intents target is an **ExtensionKit** extension, with `EXAppExtensionAttributes/EXExtensionPointIdentifier=com.apple.appintents-extension`, not a legacy SiriKit intents-service target. It belongs under `Extensions`. The MAUI project embeds the widget through `AdditionalAppExtensions`, the bridge through `NativeReference`, and the intent through a custom copy/signing target because the .NET SDK’s standard extension copier targets `PlugIns`. Both native Info.plists, entitlements, and the project specification are tracked. [Microsoft extension/build items](https://learn.microsoft.com/en-us/dotnet/ios/building-apps/build-items), [XcodeGen product types](https://github.com/yonaskolb/XcodeGen/blob/master/Docs/ProjectSpec.md#product-type), [ExtensionKit target example](https://github.com/tuist/XcodeProj/issues/687).

If changing source identifiers, update the preferred App Group in both managed and native resolvers, source entitlements, bundle IDs, workflow validation constants, and signing profiles together. Signing services may rewrite App Group IDs if the installed app and both extensions retain at least one common authorized group. Changing the group changes the shared container; it does not migrate existing history from another group.

## GitHub Actions and releases

`.github/workflows/build-ios.yml` builds an IPA only when a **new `v*.*.*` tag is pushed**, on the first run attempt. Branch pushes, pull requests, tag updates/deletions, and reruns do not build an IPA; there is no manual dispatch trigger. The MAUI job keeps `macos-latest`, matching **Xcode 26.4**, Release, and **ios-arm64**. A separate `xcode-27` job compiles the widget with the iOS 27 SDK to include Extra-large Portrait; its checked archive replaces only the widget bundle before app packaging. The app/intent/bridge toolchain and user-maintained packaging/download steps remain in place. The handoff checks identity, extension point, versions, SDK, executable presence, deployment target and archive paths before replacing anything; temporary staging is removed on success/failure. Native storage tests cover both badge and notification paging. Final IPA validation still verifies the actual device binaries and shared schemas.

Version `v1.0.2` yields display version `1.0.2`; a separate valid three-part numeric build number derives from the Actions run/attempt counters. Both jobs derive the same versions for the app, widget, App Intent extension, and bridge. The widget job has a 25-minute timeout and the MAUI job has a 45-minute timeout; obsolete runs on the same reference cancel. Read-only repository permissions suffice; the workflow does not publish GitHub Releases. Local `scripts/build-native.sh` with Xcode 26.4 offers Large/iPad Extra-large only. To include iPhone Extra-large Portrait locally, run `scripts/build-widget.sh` with Xcode 27 and overlay its output using `scripts/install-widget.py`, passing the same version/build as the main native build.

This repository currently uses **main**:

```bash
git add .
git commit -m "Release v1.0.2"
git tag -a v1.0.2 -m "Release v1.0.2"
git push origin main
git push origin v1.0.2
```

Use a new version for each release or build retry. Older tags retain their original workflow configuration; the tag-only policy applies to new tags containing this workflow change.

In Actions → Build Notification History IPA → successful run → Artifacts, download **NotificationHistory-v<version>-ios-arm64** and extract its `.ipa` for signing. The working user-maintained workflow currently uses an outer artifact ZIP with compression level 0 and verifies the extracted download against the original IPA checksum. Download the separate signing-info artifact for the validation report/checksum and entitlement files. Downloads expire after 14 days; older artifacts keep the format from their original workflow. A separate Windows check workflow verifies shared logic and managed Release compilation on main/PRs without producing an IPA.

## IPA validation

Expected layout:

```text
Payload/NotificationHistory.app/
  NotificationHistory
  Info.plist
  PlugIns/NotificationHistoryWidget.appex/
  Extensions/NotificationHistoryIntents.appex/
  Frameworks/NotificationHistoryBridge.framework/
```

```bash
unzip -l NotificationHistory-v1.0.0-ios-arm64.ipa
```

`scripts/package-ipa.py` fails for missing components, incorrect IDs/versions, missing action metadata, mismatched shared schemas, missing URL scheme, wrong extension points, or non-device/non-arm64 binaries. It checks source App Group/Data Protection entitlements and writes a classic ZIP32 IPA with explicit local CRC/file sizes, Unix executable permissions, and symlinks. It omits Apple ZIP extra fields, data descriptors, Finder metadata, and resource-fork sidecars for signer compatibility. It extracts the IPA using macOS `ditto` and validates the extracted app again. CI then downloads the uploaded IPA as a raw file and verifies its checksum, ZIP headers, CRCs, bundle versions, executable permissions, and physical-device binaries. An unsigned IPA has **no enforceable signed entitlement claims**; the report makes that distinction. After re-signing, inspect all signatures/profiles, for example `codesign -d --entitlements :- <bundle>` and `codesign --verify --strict <bundle>` for each nested bundle and the app. Verify the exact shared group on all three executable processes.

## Signing

The default build disables signing. Your signing tool must preserve **both** extension folders and the framework, register/install the ExtensionKit component, sign nested components before the main app, and supply authorized profiles with the same App Group on the app/widget/intent IDs. Sideloading does not remove iOS sandbox, entitlement, or provisioning restrictions. Some personal/free signing setups cannot authorize the required App Group; without it, this architecture cannot share history. Confirm your setup supports this before relying on automatic capture.

For FlareStore, import the downloaded IPA inside FlareStore, sign it, then use FlareStore's installation flow. Tapping an unsigned IPA in Files does not install the app. A Files preview error such as “Bad message” does not identify a bundle or signing defect; if the signed installation also fails, capture the installer's detailed error and inspect the newly signed IPA. Reuse the existing certificate and bundle identifier when updating an installed copy to retain its identity and history. [FlareStore signing and update instructions](https://flarestore.app/faq/).

If a re-signed app reports shared-storage signing errors, inspect the **signed** entitlements and provisioning profiles rather than the unsigned source plist. Each extension needs signing/provisioning appropriate to its own bundle identifier. A provider can supply renamed App Groups, but copying the main app's application identifier onto every extension or omitting extension profiles can prevent widget/action registration even when the main app opens. Ask the signing provider to preserve and provision the nested components. The app cannot create Apple-authorized profiles itself.

On Windows or macOS, inspect the shared-group selection from a signed IPA without installing or uploading it:

```bash
dotnet run --project tests/NotificationHistory.Tests -c Release -- --inspect-signing /path/to/signed.ipa
```

This reads all three executable entitlement blobs and prints their common selected App Group. It does not verify certificate trust or prove that iOS will launch the extensions. Native CI tests use the same synthetic signing fixtures to check that Swift and C# select identical groups and reject missing/malformed signing metadata.

Register the bundle IDs and group with your Apple team and enable membership in each app/extension profile. The `com.apple.security.application-groups` entitlement and Data Protection class are supplied in the tracked plist files. The history directory is protected until first unlock after restart, excluded from backups, and inaccessible before that unlock. This permits later locked-device background capture, subject to Shortcuts execution policy. Copying does not securely erase old disk blocks; app-level SQLite encryption/biometric locking is not implemented.

If adding CI signing later, put certificates/passwords and separate main/widget/intent provisioning profiles in GitHub Actions Secrets, import them into a temporary keychain, sign all nested bundles, and validate installed entitlements. Suggested names include `IOS_CERTIFICATE_BASE64`, `IOS_CERTIFICATE_PASSWORD`, `IOS_PROVISIONING_PROFILE_BASE64`, `IOS_WIDGET_PROFILE_BASE64`, `IOS_INTENTS_PROFILE_BASE64`, and `KEYCHAIN_PASSWORD`. No secrets or developer accounts are created automatically.

## Privacy and limitations

History stays in the device’s protected container; no analytics, crash uploader, ads, external APIs, or notification-content production logging are present. Debug samples require deliberate insertion and are excluded from Release. System clipboard features such as Universal Clipboard may transfer content you explicitly copy. Home Screen widgets and the unlocked app show potentially sensitive previews. Shortcuts itself may have OS-managed configuration backups; this app does not control them.

Automations must be configured per source, mappings vary with available input, and notifications with hidden/unavailable content remain partial. Widgets cannot independently monitor arrivals or continuously run. Native execution and signed App Group behavior are acceptance gates still pending on a real device. Run [the device checklist](docs/DEVICE-VALIDATION.md) before treating the end-to-end pipeline as verified.

`.gitignore` covers .NET/MAUI, editors, OS files, build outputs, Xcode user data, IPA/archives, signing materials, and local secrets. It does not ignore source, Info.plists, entitlements, or project files. Native Xcode project files are reproducible from the tracked `native/project.yml` and are generated when building.
