#!/usr/bin/env python3
"""Package an already-built Release solution into nopCommerce's multi-plugin ZIP."""
import json
import pathlib
import zipfile

ROOT = pathlib.Path(__file__).resolve().parents[1]
output = ROOT / "artifacts" / "ICEPAY-nopCommerce-5.00-1.0.0.zip"
output.parent.mkdir(exist_ok=True)
entries = []
temporary = output.with_suffix(".zip.tmp")
with zipfile.ZipFile(temporary, "w", compression=zipfile.ZIP_DEFLATED) as archive:
    for manifest in sorted((ROOT / "src").glob("*/plugin.json")):
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
