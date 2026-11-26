"""Exercise the publishing gate using real packages and deliberately damaged copies."""

import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
import xml.etree.ElementTree as ET
import zipfile

VERSION, RUNTIME, CLI = sys.argv[1:]
VALIDATOR = Path(__file__).with_name("validate_package_licenses.py")


def validate(runtime=RUNTIME, cli=CLI):
    return subprocess.run([sys.executable, str(VALIDATOR), "--version", VERSION, runtime, cli],
                          capture_output=True, text=True)


class PackageLicenseTests(unittest.TestCase):
    def altered(self, package, *, remove=None, replace=None, edit_metadata=None):
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        target = Path(temporary.name) / Path(package).name
        with zipfile.ZipFile(package) as source, zipfile.ZipFile(target, "w", compression=zipfile.ZIP_DEFLATED) as sink:
            for name in source.namelist():
                if name == remove:
                    continue
                data = source.read(name)
                if replace and name == replace[0]:
                    data = replace[1]
                if edit_metadata and name.endswith(".nuspec"):
                    root = ET.fromstring(data)
                    edit_metadata(root.find("{*}metadata"))
                    data = ET.tostring(root, encoding="utf-8", xml_declaration=True)
                sink.writestr(name, data)
        return target

    def expect_rejection(self, message, **changes):
        result = validate(**changes)
        self.assertNotEqual(result.returncode, 0, result.stdout)
        self.assertIn(message, result.stderr)

    def test_real_packages_pass(self):
        result = validate()
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual([p["id"] for p in json.loads(result.stdout)["packages"]],
                         ["Magic.IndexedDb", "Magic.IndexedDb.Cli"])

    def test_missing_license_file(self):
        self.expect_rejection("missing LICENSE.txt", runtime=self.altered(RUNTIME, remove="LICENSE.txt"))

    def test_primary_mit_expression(self):
        def set_mit(metadata):
            license_node = metadata.find("{*}license")
            license_node.set("type", "expression")
            license_node.text = "MIT"

        self.expect_rejection("primary license", cli=self.altered(CLI, edit_metadata=set_mit))

    def test_wrong_license_path(self):
        def set_wrong_path(metadata):
            metadata.find("{*}license").text = "LICENSE-MIT.txt"

        self.expect_rejection("primary license", runtime=self.altered(RUNTIME, edit_metadata=set_wrong_path))

    def test_missing_license_metadata(self):
        def remove_license(metadata):
            metadata.remove(metadata.find("{*}license"))

        self.expect_rejection("primary license", cli=self.altered(CLI, edit_metadata=remove_license))

    def test_wrong_license_bytes(self):
        self.expect_rejection("unexpected bytes in LICENSE.txt",
                              cli=self.altered(CLI, replace=("LICENSE.txt", b"Wrong license")))

    def test_missing_historical_mit_notice(self):
        self.expect_rejection("missing LICENSE-MIT.txt", cli=self.altered(CLI, remove="LICENSE-MIT.txt"))

    def test_missing_third_party_inventory(self):
        self.expect_rejection("missing THIRD-PARTY-NOTICES.txt",
                              cli=self.altered(CLI, remove="THIRD-PARTY-NOTICES.txt"))

    def test_missing_bundled_dexie_license(self):
        self.expect_rejection("missing licenses/Dexie-Apache-2.0.txt",
                              runtime=self.altered(RUNTIME, remove="licenses/Dexie-Apache-2.0.txt"))

    def test_wrong_version(self):
        def set_version(metadata):
            metadata.find("{*}version").text = "9.9.9"

        self.expect_rejection("expected version", cli=self.altered(CLI, edit_metadata=set_version))

    def test_wrong_package_identity(self):
        def set_id(metadata):
            metadata.find("{*}id").text = "Different.Library"

        self.expect_rejection("expected id", runtime=self.altered(RUNTIME, edit_metadata=set_id))

    def test_obsolete_license_url(self):
        def set_url(metadata):
            url = metadata.find("{*}licenseUrl")
            if url is None:
                url = ET.SubElement(metadata, "licenseUrl")
            url.text = "https://opensource.org/licenses/MIT"

        self.expect_rejection("unexpected legacy licenseUrl", runtime=self.altered(RUNTIME, edit_metadata=set_url))


if __name__ == "__main__":
    unittest.main(argv=[sys.argv[0]], verbosity=2)
