# Notification History

An iOS-first .NET 10 MAUI app for personal notification history. It stores the optional text supplied by a Shortcut, lets you search and manage that history, and provides small and medium native widgets. It contains no global notification listener, notification-service interception, cloud sync, analytics, or network upload code.

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
| Shared SQLite safe? | SQLite supports concurrent local processes with WAL; one writer proceeds at a time. Transactions and a busy timeout coordinate access. The widget opens read-only. |
| Specific widget links? | WidgetKit supports `widgetURL` and `Link`. The main app registers a custom URL scheme and handles missing/deleted entries. |

Relevant Apple references: [App Intents extensions](https://developer.apple.com/documentation/appintents/app-extension), [intent runtime behavior](https://developer.apple.com/documentation/appintents/configuring-the-runtime-behavior-of-your-app-intents), [App Groups](https://developer.apple.com/documentation/xcode/configuring-app-groups), [Data Protection](https://support.apple.com/guide/security/app-protection-and-app-groups-sec1a976c067/web), [widget links](https://developer.apple.com/documentation/widgetkit/linking-to-specific-app-scenes-from-your-widget-or-live-activity).

**Unresolved device facts:** the source representation, available notification input variables, original arrival timestamp/event ID, exact automation editor labels, and automatic execution choices. These are explicitly acceptance tests, not fabricated capabilities. If a field is unavailable, leave it blank. If all content is unavailable, the app can record a named event or accept manually supplied Shortcut text, but it cannot recover hidden notification content.

## Requirements

- .NET SDK **10.0.201**, pinned by `global.json`; MAUI Controls **10.0.20**.
- Workload set **10.0.204.1**, `maui-ios`. Native/IPA builds require macOS and matching **Xcode 26.4/26.4.1**, plus XcodeGen (`brew install xcodegen`). The workflow resolves `/Applications/Xcode_26.4.app` to its physical directory, exports `DEVELOPER_DIR`, and checks the asset compiler before building. This avoids [.NET's asset-compiler lookup failure through an Xcode symlink](https://github.com/dotnet/macios/issues/21762). It fails if the matching Xcode is unavailable; update SDK, workload, and Xcode together when runner images change. [Runner inventory](https://github.com/actions/runner-images/blob/main/images/macos/macos-26-Readme.md).
- Deployment target iOS **17+** for storage/UI/intents/widgets; the requested notification automation experience must be tested on **iOS 27**. No iOS 27-only SDK API is assumed in the code.
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

The authoritative database and settings live in one shared container. There are no separate history databases or JSON snapshots. On iOS both C# and Swift use the system SQLite engine. Desktop tests use the SQLite package’s bundled engine. If the App Group cannot be accessed, the app shows a storage error; it does not silently create private fallback history.

`shared/schema.sql` is embedded in C# and copied into both extensions. Version 1 is established inside `BEGIN IMMEDIATE`; SQLite’s `user_version` records it. Unknown newer schemas are rejected. Future migrations must be ordered, transactional, and understood by every writing process; never independently migrate the widget. The widget only reads schema 1.

Persistence uses WAL, `synchronous=FULL`, a 5-second busy timeout, prepared parameters, short-lived connections, explicit insert/cleanup transactions, and automatic checkpoints. The widget reads its count and three rows in one short read transaction. It does not write, migrate, or continuously execute. Save failures propagate to Shortcuts; there is no guaranteed OS-level retry. Do not remove `-wal` or `-shm` files from a live database or copy only the main file for backup. [SQLite WAL documentation](https://sqlite.org/wal.html).

On Apple platforms, C# and Swift also acquire the same POSIX `flock` before opening a writable connection and release it after closing that connection. This serializes writes and close-time checkpoints across processes, protecting older system engines against the documented WAL-reset race. The OS releases locks after a process crash; a leftover `.lock` file is harmless. Read-only widget connections need no writer lock and can read during saves. The lock wait is bounded to five seconds. [SQLite race details](https://sqlite.org/wal.html#walreset).

Records use integer UTC Unix milliseconds for ReceivedAt/CreatedAt. Local display and local-calendar day bounds handle timezone and daylight-saving changes. Retention means elapsed 24-hour days, including favorites; cleanup runs on app use and intent saves, and widget queries hide expired records. No continuous background cleanup is promised.

Source, title, subtitle, and body are nullable. Empty records receive honest fallback labels. Full bodies are retained; list queries fetch only 180-character body previews and widget queries only 200 characters. The list uses 60-row keyset pages ordered by timestamp and ID, avoiding deep offset scans. Date/source/favorite indexes support filters. Debounced, cancellable background substring search covers all four text fields, escapes `%`/`_`, and uses bound parameters. FTS was omitted because token matching differs from substring search. Unicode is preserved; SQLite’s default case-insensitive `LIKE` is primarily ASCII. Very large histories may justify a later indexed search design after device measurement.

Identical text is never discarded. An optional **Capture ID** provides transactional retry idempotence only when the caller actually has a unique event identifier. Leave it blank otherwise. Received At is also optional; absent an original timestamp, capture time is used.

## Project structure

| Path | Responsibility |
| --- | --- |
| `src/NotificationHistory.Core` | Models, async SQLite access, filters, retention, deep-link parsing |
| `src/NotificationHistory` | MAUI app, DI, view models, history XAML, details/settings/onboarding, iOS lifecycle |
| `shared/schema.sql` | Single shared schema contract |
| `native/Shared` | Swift SQLite adapter and common entitlements |
| `native/NotificationHistoryIntents` | Save Notification action and ExtensionKit entry point |
| `native/NotificationHistoryWidget` | Small/medium WidgetKit views and timeline provider |
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

The cross-storage script seeds via C#, reads/writes via Swift, runs C# and Swift writers simultaneously, checks native widget snapshots, then verifies Unicode and integrity in C#. These checks have passed on macOS in GitHub Actions. `SkipNativeIntegration=true` exists only to allow macOS managed compilation checks; such a build must not be installed or packaged as complete.

## Shortcuts setup

1. First test an ordinary shortcut with **Save Notification**, typed values, and blank optional fields. Run it with the main app closed.
2. Locate the automation controls for your iOS version. Add the documented **Notification** trigger and select the source app. The current Apple presentation shows automation controls in the shortcut editor; do not rely on an older Personal Automation screen sequence.
3. Leave Title/Subtitle/Message filters empty unless you deliberately want fewer arrivals.
4. Add **Save Notification**. Enter a fixed **Source App**, such as WhatsApp.
5. Inspect the actual input variables on your phone. Map a usable title → Title, subtitle → Subtitle, and message/body → Message only when provided. Do not infer variables from trigger filter labels. Blank fields are valid.
6. Choose automatic execution if your trigger offers it. Send a real notification and inspect the stored entry. Repeat for each source app.

The app cannot install personal automations for you, bypass preview redaction, reconstruct past notifications, or guarantee that iOS executes every arrival. If the trigger is absent or supplies no useful data, use ordinary shortcuts with text until device/platform support is established. No global listener fallback is implemented.

## Widget setup

Add Notification History through the Home Screen widget gallery. Small shows today’s count and the latest source/title; medium shows three latest captured notifications. Reloads are requested after saves, favorites, deletion, and cleanup. A 15-minute timeline policy is a request subject to WidgetKit’s budget, not an exact interval. [WidgetKit update scheduling](https://developer.apple.com/documentation/widgetkit/keeping-a-widget-up-to-date).

Small and medium row taps use `notificationhistory://notification/123`; background taps use `notificationhistory://history`. Cold launch links are queued until the navigation window is ready. Invalid/deleted IDs show an unavailable message. Widget preview content is marked privacy-sensitive, but prior system-rendered snapshots can still persist; use only widgets you are comfortable displaying.

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

If changing identifiers, update both MAUI and Swift storage constants, all entitlements, bundle IDs, workflow validation constants, and signing profiles together. Never let re-signing rewrite App Group entitlements without matching the hard-coded container ID.

## GitHub Actions and releases

`.github/workflows/build-ios.yml` builds an IPA only when a **new `v*.*.*` tag is pushed**, on the first run attempt. Branch pushes, pull requests, tag updates/deletions, and reruns do not build an IPA; there is no manual dispatch trigger. It uses `macos-latest`, Release, and **ios-arm64**. Version `v1.0.2` yields display version `1.0.2`; a separate valid three-part numeric build number derives from the Actions run/attempt counters. The app, widget, App Intent extension, and bridge share both versions. It caches NuGet packages, installs only MAUI iOS, builds native targets once, checks persistence/interoperability, then builds the device app and validates the IPA. The timeout is 45 minutes and obsolete builds on the same reference cancel. Read-only repository permissions suffice; it does not publish GitHub Releases.

This repository currently uses **main**:

```bash
git add .
git commit -m "Release v1.0.2"
git tag -a v1.0.2 -m "Release v1.0.2"
git push origin main
git push origin v1.0.2
```

Use a new version for each release or build retry. Older tags retain their original workflow configuration; the tag-only policy applies to new tags containing this workflow change.

In Actions → Build Notification History IPA → successful run → Artifacts, click **NotificationHistory-v1.0.2-ios-arm64.ipa** (or your tag's version) to download the IPA directly, without an outer ZIP. The workflow uses [`actions/upload-artifact@v7` with `archive: false`](https://github.com/actions/upload-artifact#upload-an-individual-file-unzipped) for the single IPA file. Download the separate `NotificationHistory-v1.0.2-signing-info` artifact for the validation report/checksum and entitlement files for re-signing; these supporting files are bundled in a ZIP. Downloads expire after 14 days. The direct IPA download applies to new tag builds containing this change; older artifacts keep their original format. A separate Windows check workflow verifies shared logic and managed Release compilation on main/PRs without producing an IPA.

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

`scripts/package-ipa.py` fails for missing components, incorrect IDs/versions, missing action metadata, mismatched shared schemas, missing URL scheme, wrong extension points, or non-device/non-arm64 binaries. It checks source App Group/Data Protection entitlements, packages with `ditto` to preserve permissions, extracts the IPA, and validates the extracted app again. An unsigned IPA has **no enforceable signed entitlement claims**; the report makes that distinction. After re-signing, inspect all signatures/profiles, for example `codesign -d --entitlements :- <bundle>` and `codesign --verify --strict <bundle>` for each nested bundle and the app. Verify the exact shared group on all three executable processes.

## Signing

The default build disables signing. Your signing tool must preserve **both** extension folders and the framework, register/install the ExtensionKit component, sign nested components before the main app, and supply authorized profiles with the same App Group on the app/widget/intent IDs. Sideloading does not remove iOS sandbox, entitlement, or provisioning restrictions. Some personal/free signing setups cannot authorize the required App Group; without it, this architecture cannot share history. Confirm your setup supports this before relying on automatic capture.

Register the bundle IDs and group with your Apple team and enable membership in each app/extension profile. The `com.apple.security.application-groups` entitlement and Data Protection class are supplied in the tracked plist files. The history directory is protected until first unlock after restart, excluded from backups, and inaccessible before that unlock. This permits later locked-device background capture, subject to Shortcuts execution policy. Copying does not securely erase old disk blocks; app-level SQLite encryption/biometric locking is not implemented.

If adding CI signing later, put certificates/passwords and separate main/widget/intent provisioning profiles in GitHub Actions Secrets, import them into a temporary keychain, sign all nested bundles, and validate installed entitlements. Suggested names include `IOS_CERTIFICATE_BASE64`, `IOS_CERTIFICATE_PASSWORD`, `IOS_PROVISIONING_PROFILE_BASE64`, `IOS_WIDGET_PROFILE_BASE64`, `IOS_INTENTS_PROFILE_BASE64`, and `KEYCHAIN_PASSWORD`. No secrets or developer accounts are created automatically.

## Privacy and limitations

History stays in the device’s protected container; no analytics, crash uploader, ads, external APIs, or notification-content production logging are present. Debug samples require deliberate insertion and are excluded from Release. System clipboard features such as Universal Clipboard may transfer content you explicitly copy. Home Screen widgets and the unlocked app show potentially sensitive previews. Shortcuts itself may have OS-managed configuration backups; this app does not control them.

Automations must be configured per source, mappings vary with available input, and notifications with hidden/unavailable content remain partial. Widgets cannot independently monitor arrivals or continuously run. Native execution and signed App Group behavior are acceptance gates still pending on a real device. Run [the device checklist](docs/DEVICE-VALIDATION.md) before treating the end-to-end pipeline as verified.

`.gitignore` covers .NET/MAUI, editors, OS files, build outputs, Xcode user data, IPA/archives, signing materials, and local secrets. It does not ignore source, Info.plists, entitlements, or project files. Native Xcode project files are reproducible from the tracked `native/project.yml` and are generated when building.
