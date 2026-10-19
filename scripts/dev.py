#!/usr/bin/env python3
"""Build, stage and restart the local nopCommerce host when plugin sources change."""
import argparse
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import time

ROOT = Path(__file__).resolve().parents[1]
HOST = ROOT / ".deps/nopCommerce/src/Presentation/Nop.Web"
HOST_PROJECT = HOST / "Nop.Web.csproj"
HOST_BINARY = HOST / "bin/Debug/net10.0/Nop.Web.dll"
SHIPPING_PROJECT = HOST.parents[1] / "Plugins/Nop.Plugin.Shipping.FixedByWeightByTotal/Nop.Plugin.Shipping.FixedByWeightByTotal.csproj"


def dotnet_path():
    candidate = shutil.which("dotnet")
    if candidate:
        return candidate
    candidate = Path.home() / ".dotnet/dotnet"
    if candidate.is_file():
        return str(candidate)
    raise RuntimeError("Install the .NET 10 SDK and put dotnet on PATH.")


def run(dotnet, *arguments):
    env = os.environ.copy()
    # nopCommerce's post-build tool invokes "dotnet" itself.
    env["PATH"] = str(Path(dotnet).resolve().parent) + os.pathsep + env.get("PATH", "")
    env["NUGET_PACKAGES"] = str(ROOT / ".deps/packages")
    print("\n[dev] " + " ".join(["dotnet", *map(str, arguments)]), flush=True)
    subprocess.run([dotnet, *map(str, arguments)], cwd=ROOT, env=env, check=True)


def build(dotnet, project, *properties):
    run(dotnet, "build", project, "-c", "Debug", "--disable-build-servers",
        "-m:1", "/p:UseSharedCompilation=false", "--verbosity", "minimal", *properties)


def stage_plugins():
    """Use nop 5's upload staging directory; startup replaces only these plugins."""
    manifests = sorted((ROOT / "src").glob("*/plugin.json"))
    if len(manifests) != 10:
        raise RuntimeError("Expected the shared core and nine ICEPAY payment plugins.")
    upload = HOST / "Plugins/Uploaded"
    upload.mkdir(parents=True, exist_ok=True)
    # Assemble everything before touching the host's staging directories.
    with tempfile.TemporaryDirectory(prefix="icepay-dev-") as temporary:
        staging = Path(temporary)
        for manifest in manifests:
            descriptor = json.loads(manifest.read_text())
            name = descriptor["SystemName"]
            if not name.startswith("Payments.ICEPAY.") or Path(name).name != name:
                raise RuntimeError(f"Unexpected plugin system name: {name}")
            target = staging / name
            target.mkdir()
            binary = manifest.parent / "bin/Debug/net10.0" / descriptor["FileName"]
            shutil.copy2(manifest, target / "plugin.json")
            shutil.copy2(binary, target / binary.name)
            logo = manifest.parent / "logo.png"
            if logo.is_file():
                shutil.copy2(logo, target / "logo.png")
            symbols = binary.with_suffix(".pdb")
            if symbols.exists():
                shutil.copy2(symbols, target / symbols.name)
            if name == "Payments.ICEPAY.Core":
                shutil.copy2(binary.parent / "ICEPAY.Checkout.dll", target / "ICEPAY.Checkout.dll")
                shutil.copytree(manifest.parent / "Views", target / "Views")
                shutil.copy2(ROOT / "THIRD-PARTY-NOTICES.txt", target / "THIRD-PARTY-NOTICES.txt")
                shutil.copytree(ROOT / "assets/payment-logos", target / "Assets/PaymentLogos")
        for source in staging.iterdir():
            destination = upload / source.name
            if destination.is_symlink():
                raise RuntimeError(f"Refusing to replace a symlink: {destination}")
            if destination.exists():
                shutil.rmtree(destination)
            shutil.copytree(source, destination)
    print("[dev] Staged ten Debug plugins, views and debugger symbols.", flush=True)


def prepare(dotnet):
    if not HOST_PROJECT.exists():
        subprocess.run([sys.executable, str(ROOT / "scripts/bootstrap.py")], cwd=ROOT, check=True)
    build(dotnet, HOST_PROJECT)
    # The upstream project copies itself into the development host's Plugins folder.
    build(dotnet, SHIPPING_PROJECT, f"/p:SolutionDir={HOST.parents[1]}/")
    build(dotnet, ROOT / "ICEPAY.slnx")
    stage_plugins()


def fingerprint():
    files = [ROOT / name for name in ("Directory.Build.props", "global.json", "ICEPAY.slnx")]
    files.extend(path for path in (ROOT / "src").rglob("*")
                 if path.is_file() and not {"bin", "obj"}.intersection(path.relative_to(ROOT / "src").parts))
    result = {}
    for path in files:
        try:
            stat = path.stat()
            result[str(path)] = (stat.st_mtime_ns, stat.st_size)
        except FileNotFoundError:
            pass  # A save can temporarily replace a file between listing and stat.
    return result


def start_host(dotnet, url):
    env = os.environ.copy()
    env["PATH"] = str(Path(dotnet).resolve().parent) + os.pathsep + env.get("PATH", "")
    env["ASPNETCORE_ENVIRONMENT"] = "Development"
    env["DOTNET_ENVIRONMENT"] = "Development"
    env["ASPNETCORE_URLS"] = url
    print(f"\n[dev] Starting nopCommerce at {url}", flush=True)
    return subprocess.Popen([dotnet, str(HOST_BINARY), "--urls", url], cwd=HOST, env=env)


def stop_host(process):
    if process and process.poll() is None:
        process.terminate()
        try:
            process.wait(timeout=15)
        except subprocess.TimeoutExpired:
            process.kill()
            process.wait()


def watch(dotnet, url):
    previous = fingerprint()
    prepare(dotnet)
    process = None
    rapid_exits = 0
    try:
        process = start_host(dotnet, url)
        started = time.monotonic()
        print("[dev] Save a plugin file to rebuild and restart. Ctrl+C stops the host.", flush=True)
        while True:
            time.sleep(0.75)
            current = fingerprint()
            if current != previous:
                time.sleep(0.5)  # Coalesce editor saves.
                current = fingerprint()
                try:
                    # Keep the last working store available while compilation runs.
                    build(dotnet, ROOT / "ICEPAY.slnx")
                except subprocess.CalledProcessError:
                    print("[dev] Build failed; keeping the previous store. Fix the error and save again.", flush=True)
                    previous = current
                    continue
                stop_host(process)
                stage_plugins()
                process = start_host(dotnet, url)
                started = time.monotonic()
                rapid_exits = 0
                previous = current
            elif process.poll() is not None:
                # nopCommerce exits normally when its admin UI requests a restart.
                code = process.returncode
                if code:
                    raise RuntimeError(f"nopCommerce exited with code {code}; inspect the log above.")
                rapid_exits = rapid_exits + 1 if time.monotonic() - started < 5 else 0
                if rapid_exits >= 3:
                    raise RuntimeError("nopCommerce repeatedly exited during startup; inspect the log above.")
                print("[dev] Application requested a restart.", flush=True)
                process = start_host(dotnet, url)
                started = time.monotonic()
    finally:
        stop_host(process)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("command", nargs="?", choices=["watch", "prepare"], default="watch")
    parser.add_argument("--url", default="http://localhost:5000")
    args = parser.parse_args()
    dotnet = dotnet_path()
    if args.command == "prepare":
        prepare(dotnet)
    else:
        watch(dotnet, args.url)


if __name__ == "__main__":
    try:
        main()
    except KeyboardInterrupt:
        print("\n[dev] Stopped. Database and store configuration were preserved.")
    except (RuntimeError, subprocess.CalledProcessError, OSError) as error:
        print(f"[dev] {error}", file=sys.stderr)
        sys.exit(1)
