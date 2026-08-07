#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
================================================================================
 WinCare Pro - Commercial Licensing & Key Activation Engine
================================================================================
 Manages Free vs. Pro tier enforcement, Gumroad/Payhip license key activation,
 cryptographic key validation, and persistent local activation state.
================================================================================
"""

import os
import json
import base64
import ctypes
import urllib.request
import urllib.parse
from pathlib import Path

APP_DIR = Path(os.environ.get("LOCALAPPDATA", str(Path.home()))) / "WinCarePro"
LICENSE_FILE = APP_DIR / "license.dat"

class _DataBlob(ctypes.Structure):
    _fields_ = [("cbData", ctypes.c_ulong), ("pbData", ctypes.POINTER(ctypes.c_ubyte))]


def _blob(data: bytes) -> tuple[_DataBlob, object]:
    buffer = ctypes.create_string_buffer(data)
    return _DataBlob(len(data), ctypes.cast(buffer, ctypes.POINTER(ctypes.c_ubyte))), buffer


def _dpapi(data: bytes, protect: bool) -> bytes:
    if os.name != "nt":
        raise OSError("Windows Data Protection is unavailable.")
    input_blob, buffer = _blob(data)
    output_blob = _DataBlob()
    function = ctypes.windll.crypt32.CryptProtectData if protect else ctypes.windll.crypt32.CryptUnprotectData
    args = (
        ctypes.byref(input_blob), None, None, None, None, 1,
        ctypes.byref(output_blob),
    )
    if not function(*args):
        raise ctypes.WinError()
    try:
        return ctypes.string_at(output_blob.pbData, output_blob.cbData)
    finally:
        ctypes.windll.kernel32.LocalFree(output_blob.pbData)


class LicenseManager:
    """
    Handles licensing verification, online activation, and feature tier checks.
    """

    GUMROAD_PRODUCT_PERMALINK = "wincarepro"

    def __init__(self, product_permalink=None):
        if product_permalink:
            self.GUMROAD_PRODUCT_PERMALINK = product_permalink
        self.license_info = self._load_license()

    def _load_license(self) -> dict:
        """Load a license protected for the current Windows user through DPAPI."""
        if not LICENSE_FILE.exists():
            return {"tier": "Free", "key": "", "activated": False, "email": ""}
        try:
            encrypted = base64.b64decode(LICENSE_FILE.read_bytes(), validate=True)
            data = json.loads(_dpapi(encrypted, protect=False).decode("utf-8"))
            if data.get("activated") and data.get("key"):
                return data
        except Exception:
            pass
        return {"tier": "Free", "key": "", "activated": False, "email": ""}

    def _save_license(self, key: str, email: str, tier: str = "Pro") -> bool:
        """Atomically save an online-verified license protected by Windows DPAPI."""
        try:
            APP_DIR.mkdir(parents=True, exist_ok=True)
            data = {
                "tier": tier,
                "key": key.strip().upper(),
                "email": email.strip().lower(),
                "activated": True,
            }
            encrypted = _dpapi(json.dumps(data).encode("utf-8"), protect=True)
            temporary = LICENSE_FILE.with_suffix(".tmp")
            temporary.write_bytes(base64.b64encode(encrypted))
            temporary.replace(LICENSE_FILE)
            self.license_info = data
            return True
        except Exception:
            return False

    def is_pro(self) -> bool:
        """Return the state loaded from a locally verified online activation."""
        return bool(self.license_info.get("activated"))

    def get_tier_display(self) -> str:
        """Return formatted tier string for UI display."""
        if self.is_pro():
            return "PRO TIER (Lifetime Active)"
        return "FREE TIER (Basic Maintenance)"

    def verify_key_offline(self, key: str) -> bool:
        """
        Offline activation is intentionally unsupported.

        A key's shape is not proof of purchase; accepting prefixes or UUIDs
        allowed arbitrary strings to unlock the product.
        """
        return False

    def activate_online(self, license_key: str, email: str = "") -> tuple[bool, str]:
        """
        Validate a key against Gumroad. Network failures never grant access.
        """
        clean_key = license_key.strip()
        if not clean_key:
            return False, "License key cannot be empty."

        # Attempt Gumroad API validation
        try:
            url = "https://api.gumroad.com/v2/licenses/verify"
            payload = urllib.parse.urlencode({
                "product_permalink": self.GUMROAD_PRODUCT_PERMALINK,
                "license_key": clean_key
            }).encode("utf-8")

            req = urllib.request.Request(url, data=payload, method="POST")
            with urllib.request.urlopen(req, timeout=5) as resp:  # nosec B310
                if resp.status == 200:
                    data = json.loads(resp.read().decode("utf-8"))
                    if data.get("success"):
                        buyer_email = data.get("purchase", {}).get("email", email)
                        if self._save_license(clean_key, buyer_email, tier="Pro"):
                            return True, "License successfully verified and activated online!"
                        # Fail closed: Gumroad validated the key, but we could not
                        # persist it securely (e.g. signing secret not configured).
                        return False, (
                            "License was verified, but activation could not be saved "
                            "securely through Windows Data Protection."
                        )
        except Exception:
            pass  # Offline or Gumroad API unreachable

        return False, (
            "License could not be verified. Check the key and internet connection, "
            "then try again."
        )

    def deactivate(self) -> tuple[bool, str]:
        """Deactivate current license and revert to Free tier."""
        try:
            if LICENSE_FILE.exists():
                os.remove(LICENSE_FILE)
            self.license_info = {"tier": "Free", "key": "", "activated": False, "email": ""}
            return True, "License deactivated. System reverted to Free Tier."
        except Exception as e:
            return False, f"Failed to deactivate license: {e}"


if __name__ == "__main__":
    lm = LicenseManager()
    print("Initial License Tier:", lm.get_tier_display())
