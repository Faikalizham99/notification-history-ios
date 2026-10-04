"""Classic ZIP packaging and portable validation for a directly downloaded IPA."""
import argparse
import hashlib
import json
import os
import pathlib
import plistlib
import stat
import struct
import time
import zipfile

APP = "Payload/NotificationHistory.app/"
BUNDLES = ["", "PlugIns/NotificationHistoryWidget.appex/",
           "Extensions/NotificationHistoryIntents.appex/",
           "Frameworks/NotificationHistoryBridge.framework/"]


def require(condition, message):
    if not condition:
        raise RuntimeError(message)


def write_ipa(app, output):
    """Write seekable ZIP32 headers, Unix modes and symlinks, without macOS extras."""
    app = pathlib.Path(app)
    output = pathlib.Path(output)
    output.parent.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(output, "w", compression=zipfile.ZIP_DEFLATED,
                         compresslevel=6, allowZip64=False) as archive:
        payload = zipfile.ZipInfo("Payload/")
        payload.create_system = 3
        payload.external_attr = (stat.S_IFDIR | 0o755) << 16 | 0x10
        archive.writestr(payload, b"")
        for path in [app, *sorted(app.rglob("*"))]:
            relative = path.relative_to(app)
            if any(part in {".DS_Store", "__MACOSX"} or part.startswith("._") for part in relative.parts):
                continue
            name = APP.rstrip("/") + ("/" + relative.as_posix() if relative.parts else "")
            mode = path.lstat().st_mode
            if stat.S_ISDIR(mode):
                name += "/"
            require(stat.S_ISREG(mode) or stat.S_ISDIR(mode) or stat.S_ISLNK(mode),
                    f"Unsupported app resource: {name}")
            info = zipfile.ZipInfo(name, time.localtime(path.lstat().st_mtime)[:6])
            info.create_system = 3
            info.external_attr = mode << 16 | (0x10 if stat.S_ISDIR(mode) else 0)
            if stat.S_ISLNK(mode):
                target = os.readlink(path)
                require(not os.path.isabs(target) and path.resolve().is_relative_to(app.resolve()),
                        f"Symlink escapes app: {name}")
                archive.writestr(info, target.encode("utf-8"))
            elif stat.S_ISDIR(mode):
                archive.writestr(info, b"")
            else:
                info.compress_type = zipfile.ZIP_DEFLATED
                with path.open("rb") as source, archive.open(info, "w") as destination:
                    # The seekable output lets zipfile replace the local header
                    # with final CRC/sizes instead of appending a data descriptor.
                    while chunk := source.read(1024 * 1024):
                        destination.write(chunk)


def validate_ipa(path):
    with zipfile.ZipFile(path) as archive, pathlib.Path(path).open("rb") as raw:
        entries = archive.infolist()
        names = [entry.filename for entry in entries]
        require(len(names) == len(set(name.casefold() for name in names)), "Duplicate IPA paths")
        require(APP + "Info.plist" in names, "IPA must contain Payload/NotificationHistory.app")
        for entry in entries:
            name = entry.filename
            parts = pathlib.PurePosixPath(name).parts
            require((name == "Payload/" or name.startswith(APP)) and ".." not in parts and "\\" not in name,
                    f"Unexpected IPA path: {name}")
            require(not any(part in {".DS_Store", "__MACOSX"} or part.startswith("._") for part in parts),
                    f"macOS metadata in IPA: {name}")
            require(entry.compress_type in {zipfile.ZIP_STORED, zipfile.ZIP_DEFLATED} and entry.extract_version <= 20,
                    f"Unsupported ZIP format: {name}")
            require(not entry.flag_bits & 9 and not entry.extra, f"Non-classic ZIP fields: {name}")
            raw.seek(entry.header_offset)
            header = raw.read(30)
            require(len(header) == 30 and header[:4] == b"PK\x03\x04", f"Invalid local ZIP header: {name}")
            _, version, flags, method, _, _, crc, compressed, size, name_length, extra_length = struct.unpack("<4s5H3I2H", header)
            require(version <= 20 and flags == entry.flag_bits and method == entry.compress_type and
                    crc == entry.CRC and compressed == entry.compress_size and size == entry.file_size and extra_length == 0,
                    f"Local ZIP header differs from directory: {name}")
            local_name = raw.read(name_length).decode("utf-8" if flags & 0x800 else "cp437")
            require(local_name == name, f"Local ZIP filename differs from directory: {name}")
            mode = entry.external_attr >> 16
            require(entry.create_system == 3 and stat.S_IFMT(mode) in {stat.S_IFREG, stat.S_IFDIR, stat.S_IFLNK},
                    f"Unix resource permissions missing: {name}")
            if stat.S_ISLNK(mode):
                target = archive.read(entry).decode("utf-8")
                resolved = os.path.normpath(str(pathlib.PurePosixPath(name).parent / target)).replace("\\", "/")
                require(not target.startswith("/") and resolved.startswith(APP), f"Symlink escapes app: {name}")
        require(archive.testzip() is None, "IPA CRC validation failed")
        main = plistlib.loads(archive.read(APP + "Info.plist"))
        for index, relative in enumerate(BUNDLES):
            info = plistlib.loads(archive.read(APP + relative + "Info.plist"))
            require(info.get("CFBundleShortVersionString") == main.get("CFBundleShortVersionString") and
                    info.get("CFBundleVersion") == main.get("CFBundleVersion"), f"Bundle versions differ: {relative}")
            require("iPhoneOS" in info.get("CFBundleSupportedPlatforms", []), f"Not an iPhoneOS bundle: {relative}")
            executable = info["CFBundleExecutable"]
            require(executable and "/" not in executable and "\\" not in executable and executable not in {".", ".."},
                    f"Invalid executable name: {relative}")
            entry = archive.getinfo(APP + relative + executable)
            require(stat.S_ISREG(entry.external_attr >> 16) and entry.external_attr >> 16 & 0o111,
                    f"Executable permission missing: {entry.filename}")
            with archive.open(entry) as binary:
                header = binary.read(32)
                require(len(header) == 32, f"Truncated executable: {entry.filename}")
                magic, cpu, _, kind, count, size, _, _ = struct.unpack("<8I", header)
                require(magic == 0xfeedfacf and cpu == 0x0100000c and kind == (6 if index == 3 else 2),
                        f"Expected physical arm64 Mach-O: {entry.filename}")
                require(count <= 4096 and size <= 1024 * 1024, "Invalid Mach-O load commands")
                commands = binary.read(size)
                require(len(commands) == size, "Truncated Mach-O load commands")
                cursor, device = 0, False
                for _ in range(count):
                    require(cursor + 8 <= size, "Truncated Mach-O command")
                    command, length = struct.unpack_from("<2I", commands, cursor)
                    require(length >= 8 and cursor + length <= size, "Invalid Mach-O command length")
                    if command == 0x32:
                        require(length >= 24, "Truncated Mach-O platform command")
                        device = struct.unpack_from("<I", commands, cursor + 8)[0] == 2
                    cursor += length
                require(device, f"Executable is not built for physical iOS: {entry.filename}")
    return main


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("ipa", type=pathlib.Path)
    parser.add_argument("--report", type=pathlib.Path, required=True)
    args = parser.parse_args()
    report = json.loads(args.report.read_text())
    with args.ipa.open("rb") as stream:
        require(hashlib.file_digest(stream, "sha256").hexdigest() == report["sha256"],
                "Downloaded IPA checksum differs from packaged IPA")
    info = validate_ipa(args.ipa)
    require(info["CFBundleShortVersionString"] == report["version"] and str(info["CFBundleVersion"]) == report["build"],
            "Downloaded IPA version differs from report")
    print("PASS downloaded IPA checksum, ZIP headers, CRCs, bundle versions, executable permissions and device binaries")


if __name__ == "__main__":
    main()
