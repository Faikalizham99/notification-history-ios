"""Package and validate an unsigned device app; runs on the macOS build host."""
import argparse
import hashlib
import json
import pathlib
import plistlib
import re
import shutil
import subprocess
import tempfile
import zipfile

ROOT = pathlib.Path(__file__).resolve().parents[1]
GROUP = "group.com.faikal.notificationhistory"
APP_ID = "com.faikal.notificationhistory"


def require(condition, message):
    if not condition:
        raise RuntimeError(message)


def plist(path):
    with path.open("rb") as stream:
        return plistlib.load(stream)


def validate_bundle(bundle, bundle_id, version, build, extension_point=None):
    info = plist(bundle / "Info.plist")
    require(info["CFBundleIdentifier"] == bundle_id, f"Bundle ID mismatch: {bundle}")
    require(info["CFBundleShortVersionString"] == version, f"Version mismatch: {bundle}")
    require(str(info["CFBundleVersion"]) == build, f"Build number mismatch: {bundle}")
    require("iPhoneOS" in info.get("CFBundleSupportedPlatforms", []), f"Not a device build: {bundle}")
    if extension_point:
        actual = (info.get("EXAppExtensionAttributes", {}).get("EXExtensionPointIdentifier")
                  if extension_point == "com.apple.appintents-extension"
                  else info.get("NSExtension", {}).get("NSExtensionPointIdentifier"))
        require(actual == extension_point,
                f"Wrong extension point: {bundle}")
    executable = bundle / info["CFBundleExecutable"]
    require(executable.is_file(), f"Executable missing: {bundle}")
    architecture = subprocess.check_output(["lipo", "-archs", str(executable)], text=True).strip()
    require(architecture == "arm64", f"Expected only arm64: {bundle}: {architecture}")
    commands = subprocess.check_output(["xcrun", "vtool", "-show-build", str(executable)], text=True)
    require(re.search(r"platform\s+IOS\b", commands), f"Executable is not built for physical iOS: {bundle}")
    return info


def validate_app(app, version, build):
    info = validate_bundle(app, APP_ID, version, build)
    schemes = [s for entry in info.get("CFBundleURLTypes", []) for s in entry.get("CFBundleURLSchemes", [])]
    require("notificationhistory" in schemes, "Deep-link URL scheme missing")
    widget = app / "PlugIns/NotificationHistoryWidget.appex"
    intents = app / "Extensions/NotificationHistoryIntents.appex"
    validate_bundle(widget, APP_ID + ".widget", version, build, "com.apple.widgetkit-extension")
    validate_bundle(intents, APP_ID + ".intents", version, build, "com.apple.appintents-extension")
    validate_bundle(app / "Frameworks/NotificationHistoryBridge.framework", APP_ID + ".bridge", version, build)
    exports = subprocess.check_output(["nm", "-gU", str(app / "Frameworks/NotificationHistoryBridge.framework/NotificationHistoryBridge")], text=True)
    require("_nh_reload_widgets" in exports, "Widget reload bridge symbol missing")
    metadata = intents / "Metadata.appintents"
    require(metadata.is_dir() and any(p.stat().st_size > 0 for p in metadata.rglob("*") if p.is_file()),
            "App Intents metadata missing; action discovery would fail")
    for extension in [widget, intents]:
        require((extension / "schema.sql").read_bytes() == (ROOT / "shared/schema.sql").read_bytes(),
                f"Shared schema missing or differs: {extension}")
    for path in [ROOT / "src/NotificationHistory/Platforms/iOS/Entitlements.plist", ROOT / "native/Shared/Shared.entitlements"]:
        entitlements = plist(path)
        require(entitlements.get("com.apple.security.application-groups") == [GROUP], "App Group mismatch")
        require(entitlements.get("com.apple.developer.default-data-protection") == "NSFileProtectionCompleteUntilFirstUserAuthentication",
                "Data Protection entitlement missing")
    require(len(list((app / "PlugIns").glob("*.appex"))) == 1, "Unexpected Foundation extension set")
    require(len(list((app / "Extensions").glob("*.appex"))) == 1, "Unexpected ExtensionKit extension set")


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("app", type=pathlib.Path)
    parser.add_argument("--version", required=True)
    parser.add_argument("--build", required=True)
    parser.add_argument("--output", type=pathlib.Path, required=True)
    args = parser.parse_args()
    require(re.fullmatch(r"(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)", args.version), "Invalid display version")
    require(re.fullmatch(r"[1-9][0-9]{0,3}\.[0-9]{1,2}\.[0-9]{1,2}", args.build), "Invalid iOS build number")
    validate_app(args.app, args.version, args.build)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix="notificationhistory-ipa-") as temp:
        payload = pathlib.Path(temp) / "Payload"
        payload.mkdir()
        shutil.copytree(args.app, payload / "NotificationHistory.app", symlinks=True)
        # ditto retains executable permissions and framework symlinks.
        subprocess.run(["ditto", "-c", "-k", "--keepParent", str(payload), str(args.output.resolve())], check=True)
        with zipfile.ZipFile(args.output) as archive:
            require(archive.testzip() is None, "Corrupt IPA zip")
            require("Payload/NotificationHistory.app/PlugIns/NotificationHistoryWidget.appex/Info.plist" in archive.namelist(), "Widget missing from IPA")
            require("Payload/NotificationHistory.app/Extensions/NotificationHistoryIntents.appex/Info.plist" in archive.namelist(), "Intent extension missing from IPA")
        unpacked = pathlib.Path(temp) / "unpacked"
        subprocess.run(["ditto", "-x", "-k", str(args.output.resolve()), str(unpacked)], check=True)
        validate_app(unpacked / "Payload/NotificationHistory.app", args.version, args.build)
    report = {"ipa": args.output.name, "version": args.version, "build": args.build, "runtime": "ios-arm64",
              "configuration": "Release", "signing": "unsigned; source entitlements validated, installed entitlements require re-signing",
              "appGroup": GROUP, "sha256": hashlib.sha256(args.output.read_bytes()).hexdigest(),
              "bundles": [APP_ID, APP_ID + ".widget", APP_ID + ".intents", APP_ID + ".bridge"]}
    args.output.with_suffix(".validation.json").write_text(json.dumps(report, indent=2) + "\n")
    print(f"Validated {args.output}")


if __name__ == "__main__":
    main()
