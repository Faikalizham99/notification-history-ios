import importlib.util
import io
from pathlib import Path
import plistlib
import tarfile
import tempfile
import unittest
from unittest.mock import patch

SPEC = importlib.util.spec_from_file_location("widget_install", Path(__file__).parents[1] / "scripts/install-widget.py")
INSTALLER = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(INSTALLER)


class WidgetInstallTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix="notificationhistory-widget-install-")
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name).resolve()
        self.products = self.root / "Products" / "Release-iphoneos"
        self.target = self.products / INSTALLER.BUNDLE
        self.target.mkdir(parents=True)
        (self.target / "previous.txt").write_text("Keep me on failure", encoding="utf-8")
        self.info = {
            "CFBundleIdentifier": "com.faikal.notificationhistory.widget",
            "CFBundleShortVersionString": "1.0.18", "CFBundleVersion": "1.10.1",
            "CFBundleExecutable": "NotificationHistoryWidget", "DTSDKName": "iphoneos27.0",
            "MinimumOSVersion": "17.0",
            "NSExtension": {"NSExtensionPointIdentifier": "com.apple.widgetkit-extension"},
        }

    def archive(self, extra=None):
        archive = self.root / "widget.tar.gz"
        with tarfile.open(archive, "w:gz") as package:
            for name, data in [("Info.plist", plistlib.dumps(self.info)),
                               ("NotificationHistoryWidget", b"Fixture executable"),
                               ("schema.sql", b"Shared schema fixture"),
                               ("Assets/Unicode.txt", "应用 👋".encode())]:
                item = tarfile.TarInfo(INSTALLER.BUNDLE + "/" + name)
                item.size = len(data); item.mode = 0o755 if name == "NotificationHistoryWidget" else 0o644
                package.addfile(item, io.BytesIO(data))
            if extra is not None:
                package.addfile(extra, io.BytesIO(b"x") if extra.isfile() else None)
        return archive

    def install(self, archive):
        INSTALLER.install(archive, self.products, "1.0.18", "1.10.1")

    def assert_original(self):
        self.assertEqual((self.target / "previous.txt").read_text(), "Keep me on failure")
        self.assertEqual(list(self.products.iterdir()), [self.target])

    def test_valid_handoff_replaces_whole_bundle_and_cleans_staging(self):
        self.install(self.archive())
        self.assertFalse((self.target / "previous.txt").exists())
        self.assertEqual((self.target / "Assets/Unicode.txt").read_text(encoding="utf-8"), "应用 👋")
        self.assertEqual(list(self.products.iterdir()), [self.target])
        if __import__("os").name != "nt":
            self.assertEqual((self.target / "NotificationHistoryWidget").stat().st_mode & 0o777, 0o755)

    def test_mismatch_and_older_sdk_preserve_original(self):
        for field, value in [("CFBundleIdentifier", "wrong.id"), ("CFBundleShortVersionString", "1.0.17"),
                             ("CFBundleVersion", "1.10.2"), ("DTSDKName", "iphoneos26.4"),
                             ("DTSDKName", "iphonesimulator27.0"), ("MinimumOSVersion", "27.0"),
                             ("MinimumOSVersion", "17.1"),
                             ("CFBundleExecutable", "../other")]:
            with self.subTest(field=field):
                previous = self.info[field]; self.info[field] = value
                with self.assertRaises(ValueError): self.install(self.archive())
                self.assert_original(); self.info[field] = previous

    def test_traversal_duplicate_and_links_preserve_original(self):
        for name, link in [(INSTALLER.BUNDLE + "/../../outside", False), ("/outside", False),
                           (INSTALLER.BUNDLE + "/Info.plist", False), (INSTALLER.BUNDLE + "/linked", True),
                           (INSTALLER.BUNDLE + "/C:outside", False), (INSTALLER.BUNDLE + "/back\\slash", False)]:
            with self.subTest(name=name):
                item = tarfile.TarInfo(name)
                if link:
                    item.type = tarfile.SYMTYPE; item.linkname = "../../outside"
                else: item.size = 1
                with self.assertRaises(ValueError): self.install(self.archive(item))
                self.assert_original()
                self.assertFalse((self.root / "outside").exists())

    def test_missing_executable_preserves_original(self):
        archive = self.root / "missing.tar.gz"
        with tarfile.open(archive, "w:gz") as package:
            data = plistlib.dumps(self.info)
            item = tarfile.TarInfo(INSTALLER.BUNDLE + "/Info.plist"); item.size = len(data); item.mode = 0o644
            package.addfile(item, io.BytesIO(data))
        with self.assertRaises(ValueError): self.install(archive)
        self.assert_original()

    def test_failed_replacement_restores_original_and_cleans_staging(self):
        rename = Path.rename
        def fail_replacement(source, destination):
            if source.name == INSTALLER.BUNDLE and source.parent != self.products:
                raise OSError("Simulated handoff failure")
            return rename(source, destination)
        archive = self.archive()
        with patch.object(Path, "rename", fail_replacement):
            with self.assertRaises(OSError): self.install(archive)
        self.assert_original()


if __name__ == "__main__":
    unittest.main()
