"""Validate and atomically install the separately compiled iOS 27 widget bundle."""
from __future__ import annotations

import argparse
import os
from pathlib import Path, PurePosixPath
import plistlib
import re
import tarfile
import tempfile

BUNDLE = "NotificationHistoryWidget.appex"


def install(archive: Path, products: Path, version: str, build: str) -> None:
    products = products.resolve(strict=True)
    target = products / BUNDLE
    if not target.is_dir() or target.is_symlink():
        raise ValueError("Expected an existing, ordinary widget bundle in native products")
    with tempfile.TemporaryDirectory(prefix=".widget-install-", dir=products) as temporary:
        staging = Path(temporary)
        with tarfile.open(archive, "r:gz") as package:
            members = package.getmembers()
            if len(members) > 4000 or sum(member.size for member in members) > 100 * 1024 * 1024:
                raise ValueError("Widget archive exceeds the handoff size limit")
            seen: set[str] = set()
            for member in members:
                path = PurePosixPath(member.name)
                if (path.is_absolute() or not path.parts or path.parts[0] != BUNDLE
                        or ".." in path.parts or "\\" in member.name or ":" in member.name
                        or not (member.isfile() or member.isdir()) or path.as_posix() in seen):
                    raise ValueError("Unsafe, linked, or duplicate widget archive entry")
                seen.add(path.as_posix())
                destination = staging.joinpath(*path.parts)
                if member.isdir():
                    destination.mkdir(parents=True, exist_ok=True)
                else:
                    destination.parent.mkdir(parents=True, exist_ok=True)
                    with package.extractfile(member) as source, destination.open("xb") as output:
                        while chunk := source.read(1024 * 1024):
                            output.write(chunk)
                os.chmod(destination, member.mode & 0o777)
        replacement = staging / BUNDLE
        with (replacement / "Info.plist").open("rb") as file:
            info = plistlib.load(file)
        if (info.get("CFBundleIdentifier") != "com.faikal.notificationhistory.widget"
                or info.get("CFBundleShortVersionString") != version
                or info.get("CFBundleVersion") != build
                or info.get("NSExtension", {}).get("NSExtensionPointIdentifier") != "com.apple.widgetkit-extension"):
            raise ValueError("Widget identity, version, or extension point does not match this app")
        sdk = re.fullmatch(r"iphoneos(\d+)(?:\.\d+)*", str(info.get("DTSDKName", "")))
        if not sdk or int(sdk.group(1)) < 27:
            raise ValueError("Widget was not compiled with the iOS 27 SDK")
        minimum = str(info.get("MinimumOSVersion", ""))
        if (not re.fullmatch(r"\d+(?:\.\d+)*", minimum) or int(minimum.split(".")[0]) != 17
                or any(int(part) != 0 for part in minimum.split(".")[1:])):
            raise ValueError("Widget does not match the app's iOS 17.0 deployment target")
        executable = info.get("CFBundleExecutable")
        if executable != "NotificationHistoryWidget" or not (replacement / executable).is_file():
            raise ValueError("Missing widget executable")
        os.chmod(replacement / executable, 0o755)
        backup = staging / "previous.appex"
        target.rename(backup)
        try:
            replacement.rename(target)
        except BaseException:
            backup.rename(target)
            raise


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("archive", type=Path)
    parser.add_argument("products", type=Path)
    parser.add_argument("--version", required=True)
    parser.add_argument("--build", required=True)
    options = parser.parse_args()
    install(options.archive, options.products, options.version, options.build)
    print("Installed matching iOS 27 widget; final IPA validation still checks device binaries and shared schemas.")
