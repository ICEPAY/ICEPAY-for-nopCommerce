#!/usr/bin/env python3
"""Package an already-built Release solution into nopCommerce's multi-plugin ZIP."""
import argparse
import json
import pathlib
import re
import xml.etree.ElementTree as ET
import zipfile

ROOT = pathlib.Path(__file__).resolve().parents[1]
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("--release-tag", default="", help="Require this GitHub release tag to match the plugin version (optional v prefix).")
args = parser.parse_args()
version = ET.parse(ROOT / "Directory.Build.props").findtext(".//Version", "").strip()
if not re.fullmatch(r"[0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z.-]+)?", version):
    raise SystemExit("Directory.Build.props must contain a valid plugin version, for example 1.0.0.")
if args.release_tag and args.release_tag.removeprefix("v") != version:
    raise SystemExit(f"Release tag {args.release_tag!r} does not match plugin version {version}. Update the source version before tagging a release.")
manifests = sorted((ROOT / "src").glob("*/plugin.json"))
if len(manifests) != 10:
    raise SystemExit("Expected one core and nine payment plugins.")
for manifest in manifests:
    if json.loads(manifest.read_text())["Version"] != version:
        raise SystemExit(f"Version in {manifest.relative_to(ROOT)} must match Directory.Build.props ({version}).")
output = ROOT / "artifacts" / f"ICEPAY-nopCommerce-5.00-{version}.zip"
output.parent.mkdir(exist_ok=True)
entries = []
temporary = output.with_suffix(".zip.tmp")
with zipfile.ZipFile(temporary, "w", compression=zipfile.ZIP_DEFLATED) as archive:
    for manifest in manifests:
        descriptor = json.loads(manifest.read_text())
        folder = descriptor["SystemName"]
        binary = manifest.parent / "bin" / "Release" / "net10.0" / descriptor["FileName"]
        if not binary.is_file():
            raise SystemExit(f"Missing {binary}; build the solution in Release first.")
        archive.write(manifest, f"{folder}/plugin.json")
        archive.write(binary, f"{folder}/{binary.name}")
        logo = manifest.parent / "logo.png"
        if logo.is_file():
            archive.write(logo, f"{folder}/logo.png")
        if folder == "Payments.ICEPAY.Core":
            sdk = binary.parent / "ICEPAY.Checkout.dll"
            archive.write(sdk, f"{folder}/{sdk.name}")
            for view in (manifest.parent / "Views").rglob("*.cshtml"):
                archive.write(view, f"{folder}/{view.relative_to(manifest.parent)}")
            archive.write(ROOT / "THIRD-PARTY-NOTICES.txt", f"{folder}/THIRD-PARTY-NOTICES.txt")
            for asset in sorted((ROOT / "assets/payment-logos").iterdir()):
                archive.write(asset, f"{folder}/Assets/PaymentLogos/{asset.name}")
        entries.append({"Type": "Plugin", "SupportedVersion": "5.00", "DirectoryPath": folder + "/", "SystemName": folder})
    if len(entries) != 10:
        raise SystemExit("Expected one core and nine payment plugins.")
    archive.writestr("uploadedItems.json", json.dumps(entries, indent=2))
    archive.write(ROOT / "LICENSE", "LICENSE.txt")
    archive.write(ROOT / "README.md", "README.md")
temporary.replace(output)
print(output)
