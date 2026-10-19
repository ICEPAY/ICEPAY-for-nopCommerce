#!/usr/bin/env python3
"""Download pinned source dependencies for development (Python 3.12+)."""
import io
import pathlib
import tarfile
import urllib.request

ROOT = pathlib.Path(__file__).resolve().parents[1]
SOURCES = {
    "nopCommerce": ("nopSolutions/nopCommerce", "0608f65d203470e5afc2dda12abdfaca734fbc8d"),
    "checkout-dotnet": ("ICEPAY/checkout-dotnet", "da092b2a5592046be8c4353d6744745b7d433d6d"),
}

for name, (repository, revision) in SOURCES.items():
    destination = ROOT / ".deps" / name
    stamp = destination / ".icepay-source-revision"
    if stamp.exists() and stamp.read_text().strip() == revision:
        print(f"{name}: already available")
        continue
    if destination.exists():
        raise SystemExit(f"Refusing to overwrite {destination}; move it aside first.")
    url = f"https://codeload.github.com/{repository}/tar.gz/{revision}"
    print(f"Downloading {repository} at {revision}", flush=True)
    with urllib.request.urlopen(url, timeout=120) as response:
        payload = response.read()
    destination.mkdir(parents=True)
    with tarfile.open(fileobj=io.BytesIO(payload), mode="r:gz") as archive:
        members = archive.getmembers()
        for member in members:
            member.name = "/".join(member.name.split("/")[1:])
        archive.extractall(destination, members=[m for m in members if m.name], filter="data")
    stamp.write_text(revision + "\n")
    print(f"{name}: ready", flush=True)
