#!/usr/bin/env python3
"""Generate update.json manifest for WinCare Pro.

Usage:
    python generate_update_manifest.py --download-url "https://..." --version "1.3.0"
"""
import argparse
import hashlib
import json
from pathlib import Path

def main():
    parser = argparse.ArgumentParser(description="Generate WinCare Pro update manifest")
    parser.add_argument("--download-url", required=True, help="HTTPS URL to the installer")
    parser.add_argument("--version", default="1.3.0", help="Version string")
    parser.add_argument("--installer", default="dist/WinCarePro-Setup-1.3.0.exe", help="Path to installer exe")
    parser.add_argument("--output", default="dist/update.json", help="Output path for update.json")
    args = parser.parse_args()

    installer_path = Path(args.installer)
    if not installer_path.exists():
        raise FileNotFoundError(f"Installer not found: {installer_path}")

    sha256 = hashlib.sha256(installer_path.read_bytes()).hexdigest()

    # Verify URL is HTTPS
    if not args.download_url.startswith("https://"):
        raise ValueError("Download URL must be HTTPS")

    manifest = {
        "version": args.version,
        "url": args.download_url,
        "sha256": sha256,
    }

    output_path = Path(args.output)
    output_path.parent.mkdir(parents=True, exist_ok=True)
    output_path.write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")

    print(f"Manifest written to {output_path}")
    print(json.dumps(manifest, indent=2))

if __name__ == "__main__":
    main()
