import os
import pathlib
import plistlib
import stat
import struct
import sys
import tempfile
import unittest
import zipfile

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parents[1] / "scripts"))
from ipa_archive import APP, BUNDLES, validate_ipa, write_ipa


class IpaArchiveTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix="notificationhistory-zip-test-")
        self.addCleanup(self.temp.cleanup)
        self.root = pathlib.Path(self.temp.name)
        self.app = self.root / "NotificationHistory.app"
        self.ipa = self.root / "NotificationHistory.ipa"
        for index, relative in enumerate(BUNDLES):
            bundle = self.app / relative
            bundle.mkdir(parents=True, exist_ok=True)
            info = {"CFBundleExecutable": "Fixture.exe", "CFBundleShortVersionString": "1.0.5",
                    "CFBundleVersion": "1.90.1", "CFBundleSupportedPlatforms": ["iPhoneOS"]}
            (bundle / "Info.plist").write_bytes(plistlib.dumps(info, fmt=plistlib.FMT_BINARY))
            header = struct.pack("<8I", 0xfeedfacf, 0x0100000c, 0, 6 if index == 3 else 2, 1, 24, 0, 0)
            platform = struct.pack("<6I", 0x32, 24, 2, 0x110000, 0x1a0400, 0)
            binary = bundle / "Fixture.exe"
            binary.write_bytes(header + platform + b"synthetic fixture" * 100)
            binary.chmod(0o755)

    def rewrite(self, change):
        alternate = self.ipa.with_suffix(".new.ipa")
        with zipfile.ZipFile(self.ipa) as source, zipfile.ZipFile(alternate, "w") as output:
            for info in source.infolist():
                result = change(info, source.read(info))
                if result is not None:
                    entry, data = result
                    output.writestr(entry, data)
        alternate.replace(self.ipa)

    def test_classic_headers_permissions_and_nested_bundles(self):
        write_ipa(self.app, self.ipa)
        self.assertEqual(validate_ipa(self.ipa)["CFBundleShortVersionString"], "1.0.5")
        with zipfile.ZipFile(self.ipa) as archive:
            for relative in BUNDLES:
                info = archive.getinfo(APP + relative + "Fixture.exe")
                self.assertEqual(info.flag_bits & 9, 0)
                self.assertEqual(info.extra, b"")
                self.assertEqual(stat.S_IMODE(info.external_attr >> 16), stat.S_IMODE((self.app / relative / "Fixture.exe").stat().st_mode))
                self.assertEqual(archive.read(info), (self.app / relative / "Fixture.exe").read_bytes())

    def test_mac_metadata_is_not_packaged(self):
        (self.app / ".DS_Store").write_bytes(b"finder")
        (self.app / "._Info.plist").write_bytes(b"appledouble")
        (self.app / "__MACOSX").mkdir()
        (self.app / "__MACOSX" / "metadata").write_bytes(b"metadata")
        write_ipa(self.app, self.ipa)
        validate_ipa(self.ipa)
        with zipfile.ZipFile(self.ipa) as archive:
            self.assertFalse(any("__MACOSX" in name or "._" in name or ".DS_Store" in name for name in archive.namelist()))

    def test_outer_zip_rejected(self):
        write_ipa(self.app, self.ipa)
        outer = self.root / "outer.ipa"
        with zipfile.ZipFile(outer, "w") as archive:
            archive.write(self.ipa, self.ipa.name)
        with self.assertRaisesRegex(RuntimeError, "must contain Payload"):
            validate_ipa(outer)

    def test_data_descriptor_flag_rejected(self):
        write_ipa(self.app, self.ipa)
        with zipfile.ZipFile(self.ipa) as archive:
            directory = archive.start_dir
        data = bytearray(self.ipa.read_bytes())
        struct.pack_into("<H", data, 6, 8)
        struct.pack_into("<H", data, directory + 8, 8)
        self.ipa.write_bytes(data)
        with self.assertRaisesRegex(RuntimeError, "Non-classic ZIP"):
            validate_ipa(self.ipa)

    def test_local_size_mismatch_rejected(self):
        write_ipa(self.app, self.ipa)
        data = bytearray(self.ipa.read_bytes())
        struct.pack_into("<I", data, 22, 1)
        self.ipa.write_bytes(data)
        with self.assertRaisesRegex(RuntimeError, "Local ZIP header differs"):
            validate_ipa(self.ipa)

    def test_missing_executable_permission_rejected(self):
        write_ipa(self.app, self.ipa)
        def change(info, data):
            if info.filename == APP + "Fixture.exe":
                info.external_attr &= ~(0o111 << 16)
            return info, data
        self.rewrite(change)
        with self.assertRaisesRegex(RuntimeError, "Executable permission missing"):
            validate_ipa(self.ipa)

    def test_simulator_binary_rejected(self):
        write_ipa(self.app, self.ipa)
        def change(info, data):
            if info.filename == APP + "Fixture.exe":
                data = bytearray(data)
                struct.pack_into("<I", data, 40, 7)
            return info, data
        self.rewrite(change)
        with self.assertRaisesRegex(RuntimeError, "not built for physical iOS"):
            validate_ipa(self.ipa)

    def test_bundle_version_mismatch_rejected(self):
        write_ipa(self.app, self.ipa)
        def change(info, data):
            if info.filename == APP + BUNDLES[1] + "Info.plist":
                plist = plistlib.loads(data)
                plist["CFBundleVersion"] = "1.90.2"
                data = plistlib.dumps(plist)
            return info, data
        self.rewrite(change)
        with self.assertRaisesRegex(RuntimeError, "Bundle versions differ"):
            validate_ipa(self.ipa)

    def test_missing_extension_rejected(self):
        write_ipa(self.app, self.ipa)
        self.rewrite(lambda info, data: None if info.filename.startswith(APP + BUNDLES[2]) else (info, data))
        with self.assertRaises(KeyError):
            validate_ipa(self.ipa)

    @unittest.skipIf(os.name == "nt", "Symlink creation requires privileges on Windows")
    def test_symlink_permissions_and_target_preserved(self):
        (self.app / "binary-link").symlink_to("Fixture.exe")
        write_ipa(self.app, self.ipa)
        validate_ipa(self.ipa)
        with zipfile.ZipFile(self.ipa) as archive:
            info = archive.getinfo(APP + "binary-link")
            self.assertTrue(stat.S_ISLNK(info.external_attr >> 16))
            self.assertEqual(archive.read(info), b"Fixture.exe")

    @unittest.skipIf(os.name == "nt", "Symlink creation requires privileges on Windows")
    def test_symlink_escape_rejected(self):
        (self.app / "outside-link").symlink_to("../outside")
        with self.assertRaisesRegex(RuntimeError, "Symlink escapes app"):
            write_ipa(self.app, self.ipa)


if __name__ == "__main__":
    unittest.main()
