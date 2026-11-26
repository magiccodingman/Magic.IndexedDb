"""Verify licensing and identity in the exact NuGet files passed to publication."""

import argparse
import hashlib
import json
from pathlib import Path
import sys
import xml.etree.ElementTree as ET
import zipfile

ROOT = Path(__file__).resolve().parents[2]
MICL_SHA256 = "6d30b1fcbc05dd440e4ca69e1e400483d1450dd37fb278880e5a9bc46446cf96"
NUGET_GENERATED_LICENSE_URL = "https://aka.ms/deprecateLicenseUrl"
SHARED_NOTICES = ("LICENSE.txt", "LICENSE-MIT.txt", "THIRD-PARTY-NOTICES.txt")
DEXIE_NOTICE = "licenses/Dexie-Apache-2.0.txt"


def require(condition, message):
    if not condition:
        raise ValueError(message)


def verify_archive(path, package_id, expected_version, files):
    with zipfile.ZipFile(path) as archive:
        names = archive.namelist()
        require(len(names) == len({name.casefold() for name in names}),
                f"{path.name}: duplicate archive entry")
        manifests = [name for name in names if name.lower().endswith(".nuspec")]
        require(manifests == [f"{package_id}.nuspec"], f"{path.name}: expected one {package_id}.nuspec")
        spec = ET.fromstring(archive.read(manifests[0]))
        metadata = spec.findall("{*}metadata")
        require(len(metadata) == 1, f"{path.name}: expected one metadata section")
        metadata = metadata[0]
        for field, expected in (("id", package_id), ("version", expected_version)):
            nodes = metadata.findall("{*}" + field)
            require(len(nodes) == 1 and nodes[0].text == expected,
                    f"{path.name}: expected {field} {expected}")
        licenses = metadata.findall("{*}license")
        require(len(licenses) == 1 and licenses[0].get("type") == "file" and licenses[0].text == "LICENSE.txt",
                f"{path.name}: primary license must be type=file and path=LICENSE.txt")
        license_urls = metadata.findall("{*}licenseUrl")
        require(not license_urls or (len(license_urls) == 1 and license_urls[0].text == NUGET_GENERATED_LICENSE_URL),
                f"{path.name}: unexpected legacy licenseUrl")
        for name in files:
            require(name in names, f"{path.name}: missing {name}")
            require(archive.read(name) == (ROOT / name).read_bytes(), f"{path.name}: unexpected bytes in {name}")
        return {
            "id": package_id, "version": expected_version,
            "licenseType": "file", "licensePath": "LICENSE.txt", "licenseSha256": MICL_SHA256,
            "notices": [name for name in files if name != "LICENSE.txt"],
        }


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--version", required=True)
    parser.add_argument("--report", type=Path)
    parser.add_argument("runtime", type=Path)
    parser.add_argument("cli", type=Path)
    args = parser.parse_args()
    try:
        require(hashlib.sha256((ROOT / "LICENSE.txt").read_bytes()).hexdigest() == MICL_SHA256,
                "Repository LICENSE.txt does not match the recorded MICL 1.0 text")
        packages = [
            verify_archive(args.runtime, "Magic.IndexedDb", args.version, (*SHARED_NOTICES, DEXIE_NOTICE)),
            verify_archive(args.cli, "Magic.IndexedDb.Cli", args.version, SHARED_NOTICES),
        ]
        report = json.dumps({"packages": packages}, indent=2)
        if args.report:
            args.report.write_text(report + "\n", encoding="utf-8")
        print(report)
    except (ValueError, OSError, ET.ParseError, zipfile.BadZipFile) as error:
        print(f"Package licensing validation failed: {error}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
