"""Secure update discovery and installer verification for WinCare Pro."""

from __future__ import annotations

import hashlib
import json
import os
import subprocess
import tempfile
import urllib.parse
import urllib.request
from pathlib import Path

from release_config import release_setting


CREATE_NO_WINDOW = 0x08000000 if os.name == "nt" else 0
DEFAULT_MANIFEST_URL = release_setting("WINCAREPRO_UPDATE_MANIFEST_URL")
EXPECTED_PUBLISHER = release_setting("WINCAREPRO_SIGNER_SUBJECT")


def version_tuple(value: str) -> tuple[int, ...]:
    parts = value.strip().lstrip("v").split(".")
    if not parts or any(not part.isdigit() for part in parts):
        raise ValueError(f"Invalid version: {value}")
    return tuple(int(part) for part in parts)


def _https_url(value: str) -> str:
    parsed = urllib.parse.urlparse(value)
    if parsed.scheme != "https" or not parsed.netloc or parsed.username:
        raise ValueError("Release URLs must use public HTTPS addresses.")
    return value


class UpdateClient:
    def __init__(self, manifest_url: str = DEFAULT_MANIFEST_URL,
                 expected_publisher: str = EXPECTED_PUBLISHER):
        self.manifest_url = _https_url(manifest_url) if manifest_url else ""
        self.expected_publisher = expected_publisher.strip()

    @property
    def configured(self) -> bool:
        return bool(self.manifest_url and self.expected_publisher)

    def check(self, current_version: str) -> dict:
        if not self.configured:
            return {"available": False, "configured": False,
                    "message": "Update service is not configured."}
        request = urllib.request.Request(
            self.manifest_url, headers={"User-Agent": "WinCarePro-Updater/1.0"})
        with urllib.request.urlopen(request, timeout=10) as response:  # nosec B310
            if response.status != 200:
                raise OSError(f"Update server returned HTTP {response.status}.")
            manifest = json.loads(response.read(65537).decode("utf-8"))
        required = {"version", "url", "sha256"}
        if not isinstance(manifest, dict) or not required <= manifest.keys():
            raise ValueError("Update manifest is missing required fields.")
        manifest["url"] = _https_url(str(manifest["url"]))
        digest = str(manifest["sha256"]).lower()
        if len(digest) != 64 or any(ch not in "0123456789abcdef" for ch in digest):
            raise ValueError("Update manifest contains an invalid SHA-256.")
        manifest["sha256"] = digest
        manifest["available"] = (
            version_tuple(str(manifest["version"])) >
            version_tuple(current_version))
        manifest["configured"] = True
        return manifest

    def download_and_verify(self, manifest: dict,
                            destination: str | None = None) -> Path:
        url = _https_url(str(manifest["url"]))
        destination_path = Path(destination) if destination else (
            Path(tempfile.gettempdir()) / "WinCarePro-Update.exe")
        request = urllib.request.Request(
            url, headers={"User-Agent": "WinCarePro-Updater/1.0"})
        try:
            digest = hashlib.sha256()
            with (
                urllib.request.urlopen(request, timeout=60) as response,  # nosec B310
                destination_path.open("wb") as output,
            ):
                while chunk := response.read(1024 * 1024):
                    digest.update(chunk)
                    output.write(chunk)
            if digest.hexdigest().lower() != str(manifest["sha256"]).lower():
                raise ValueError("Downloaded update failed SHA-256 verification.")
            self._verify_authenticode(destination_path)
            return destination_path
        except Exception:
            destination_path.unlink(missing_ok=True)
            raise

    def _verify_authenticode(self, path: Path) -> None:
        if os.name != "nt":
            raise OSError("Authenticode verification requires Windows.")
        script = (
            "$s=Get-AuthenticodeSignature -LiteralPath $env:WINCAREPRO_SIGNATURE_TARGET;"
            "[pscustomobject]@{Status=$s.Status.ToString();"
            "Subject=$s.SignerCertificate.Subject}|ConvertTo-Json -Compress"
        )
        try:
            process = subprocess.run(
                ["powershell", "-NoProfile", "-NonInteractive", "-Command",
                 script],
                capture_output=True, text=True, timeout=20,
                creationflags=CREATE_NO_WINDOW,
                env={**os.environ,
                     "SystemRoot": os.environ.get("SystemRoot", r"C:\Windows"),
                     "WINDIR": os.environ.get("WINDIR", r"C:\Windows"),
                     "PSModulePath": r"C:\Program Files\WindowsPowerShell\Modules;C:\Windows\System32\WindowsPowerShell\v1.0\Modules",
                     "WINCAREPRO_SIGNATURE_TARGET": str(path)})
            result = json.loads(process.stdout or "{}")
            if process.returncode or result.get("Status") != "Valid":
                raise ValueError("Update does not have a valid Authenticode signature.")
            if result.get("Subject", "").strip().casefold() != self.expected_publisher.casefold():
                raise ValueError("Update signer does not match the trusted publisher.")
        except Exception as exc:
            path.unlink(missing_ok=True)
            if isinstance(exc, ValueError):
                raise
            raise ValueError("Update signature verification failed.") from exc


def install_and_relaunch(installer_path: Path) -> None:
    """Launch a verified installer silently, then exit this process.

    The installer's own post-install [Run] entry (installer/WinCarePro.iss)
    relaunches the app once the silent install completes.
    """
    subprocess.Popen(
        [str(installer_path), "/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART"],
        creationflags=CREATE_NO_WINDOW,
    )
    raise SystemExit(0)
