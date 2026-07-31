"""Sign and publish WinCarePro release artifacts."""

import argparse
import hashlib
import json
import os
import shutil
import subprocess
from pathlib import Path


def find_signtool() -> str:
    found = shutil.which("signtool")
    if found:
        return found
    kits = Path(os.environ.get("ProgramFiles(x86)", r"C:\Program Files (x86)")) / "Windows Kits" / "10" / "bin"
    matches = sorted(kits.glob(r"*\x64\signtool.exe"), reverse=True)
    if not matches:
        raise FileNotFoundError("signtool.exe was not found. Install the Windows SDK.")
    return str(matches[0])


def sign(path: Path, thumbprint: str, timestamp_url: str) -> None:
    subprocess.run(
        [
            find_signtool(), "sign", "/sha1", thumbprint, "/fd", "SHA256",
            "/tr", timestamp_url, "/td", "SHA256", str(path),
        ],
        check=True,
    )
    subprocess.run([find_signtool(), "verify", "/pa", "/v", str(path)], check=True)


def manifest(path: Path, version: str, download_url: str, output: Path) -> None:
    digest = hashlib.sha256(path.read_bytes()).hexdigest()
    output.write_text(
        json.dumps({"version": version, "url": download_url, "sha256": digest}, indent=2) + "\n",
        encoding="utf-8",
    )


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("exe", type=Path)
    parser.add_argument("--version", required=True)
    parser.add_argument("--url", required=True)
    parser.add_argument("--manifest", type=Path, default=Path("dist/update.json"))
    parser.add_argument("--thumbprint", default=os.environ.get("WINCAREPRO_SIGN_CERT_THUMBPRINT"))
    parser.add_argument("--timestamp-url", default="http://timestamp.digicert.com")
    args = parser.parse_args()
    if not args.exe.is_file():
        parser.error(f"EXE not found: {args.exe}")
    if not args.thumbprint:
        parser.error("Set --thumbprint or WINCAREPRO_SIGN_CERT_THUMBPRINT")
    sign(args.exe.resolve(), args.thumbprint, args.timestamp_url)
    manifest(args.exe.resolve(), args.version, args.url, args.manifest.resolve())
    print(f"Signed {args.exe} and wrote {args.manifest}")


if __name__ == "__main__":
    main()
