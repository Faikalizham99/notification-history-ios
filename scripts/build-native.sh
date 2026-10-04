#!/usr/bin/env bash
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
configuration="${CONFIGURATION:-Release}"
sdk="${NATIVE_SDK:-iphoneos}"
version="${APP_VERSION:-1.0.0}"
number="${BUILD_NUMBER:-1}"
if ! command -v xcodegen >/dev/null; then
  echo 'Install XcodeGen with: brew install xcodegen' >&2
  exit 1
fi
xcodegen generate --spec "$root/native/project.yml"
xcodebuild -project "$root/native/NotificationHistoryNative.xcodeproj" \
  -scheme NotificationHistoryNative -configuration "$configuration" -sdk "$sdk" \
  -derivedDataPath "$root/artifacts/native" \
  MARKETING_VERSION="$version" CURRENT_PROJECT_VERSION="$number" \
  CODE_SIGNING_ALLOWED=NO CODE_SIGNING_REQUIRED=NO build
products="$root/artifacts/native/Build/Products/$configuration-$sdk"
test -d "$products/NotificationHistoryWidget.appex"
test -d "$products/NotificationHistoryIntents.appex"
test -d "$products/NotificationHistoryBridge.framework"
for bundle in NotificationHistoryWidget.appex NotificationHistoryIntents.appex NotificationHistoryBridge.framework; do
  info="$products/$bundle/Info.plist"
  actual_version="$(/usr/libexec/PlistBuddy -c 'Print :CFBundleShortVersionString' "$info")"
  actual_number="$(/usr/libexec/PlistBuddy -c 'Print :CFBundleVersion' "$info")"
  if [[ "$actual_version" != "$version" || "$actual_number" != "$number" ]]; then
    echo "$bundle version mismatch: expected $version ($number), got $actual_version ($actual_number)" >&2
    exit 1
  fi
done
echo "Native products: $products"
