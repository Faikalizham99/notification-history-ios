#!/usr/bin/env bash
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
configuration="${CONFIGURATION:-Release}"
version="${APP_VERSION:-1.0.0}"
number="${BUILD_NUMBER:-1}"
sdk_version="$(xcrun --sdk iphoneos --show-sdk-version)"
if [[ "${sdk_version%%.*}" -lt 27 ]]; then
  echo 'Extra-large Portrait requires the iOS 27 SDK. Select Xcode 27 for the widget build.' >&2
  exit 1
fi
xcodegen generate --spec "$root/native/project.yml"
xcodebuild -project "$root/native/NotificationHistoryNative.xcodeproj" \
  -scheme NotificationHistoryWidget -configuration "$configuration" -sdk iphoneos \
  -derivedDataPath "$root/artifacts/widget-xcode27" \
  MARKETING_VERSION="$version" CURRENT_PROJECT_VERSION="$number" \
  OTHER_SWIFT_FLAGS='$(inherited) -D NH_EXTRA_LARGE_PORTRAIT' \
  CODE_SIGNING_ALLOWED=NO CODE_SIGNING_REQUIRED=NO build
products="$root/artifacts/widget-xcode27/Build/Products/$configuration-iphoneos"
test -f "$products/NotificationHistoryWidget.appex/Info.plist"
# Preserve the Mach-O executable's mode across GitHub's artifact transport.
COPYFILE_DISABLE=1 tar -czf "$root/artifacts/NotificationHistoryWidget-xcode27.tar.gz" -C "$products" NotificationHistoryWidget.appex
