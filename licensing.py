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
import hashlib
import urllib.request
import urllib.parse
from pathlib import Path

APP_DIR = Path(os.environ.get("LOCALAPPDATA", str(Path.home()))) / "WinCarePro"
LICENSE_FILE = APP_DIR / "license.dat"


class LicenseManager:
    """
    Handles licensing verification, online activation, and feature tier checks.
    """

    GUMROAD_PRODUCT_PERMALINK = "wincarepro"

    def __init__(self, product_permalink=None):
        if product_permalink:
            self.GUMROAD_PRODUCT_PERMALINK = product_permalink
        self.license_info = self._load_license()

    def _get_machine_guid(self) -> str:
        """Fetch unique hardware MachineGuid to bind local license signature."""
        try:
            import winreg
            with winreg.OpenKey(winreg.HKEY_LOCAL_MACHINE, r"SOFTWARE\Microsoft\Cryptography") as k:
                val, _ = winreg.QueryValueEx(k, "MachineGuid")
                return str(val).strip()
        except Exception:
            return "WINCARE_PRO_DEFAULT_HWGUID"

    def _compute_signature(self, key: str, email: str) -> str:
        """Compute SHA256 signature tied to hardware GUID and secret key."""
        hw_id = self._get_machine_guid()
        raw = f"{key.strip().upper()}:{email.strip().lower()}:{hw_id}:WINCARE_PRO_SECRET_v1.3"
        return hashlib.sha256(raw.encode("utf-8")).hexdigest()

    def _load_license(self) -> dict:
        """Load stored license from local storage and verify cryptographic signature."""
        if not LICENSE_FILE.exists():
            return {"tier": "Free", "key": "", "activated": False, "email": ""}
        try:
            with open(LICENSE_FILE, "r", encoding="utf-8") as f:
                data = json.load(f)
                key = data.get("key", "")
                email = data.get("email", "")
                sig = data.get("signature", "")
                if data.get("activated") and sig == self._compute_signature(key, email):
                    return data
        except Exception:
            pass
        return {"tier": "Free", "key": "", "activated": False, "email": ""}

    def _save_license(self, key: str, email: str, tier: str = "Pro") -> bool:
        """Save activated license data with cryptographic signature to disk."""
        try:
            APP_DIR.mkdir(parents=True, exist_ok=True)
            sig = self._compute_signature(key, email)
            data = {
                "tier": tier,
                "key": key.strip().upper(),
                "email": email.strip().lower(),
                "activated": True,
                "signature": sig
            }
            with open(LICENSE_FILE, "w", encoding="utf-8") as f:
                json.dump(data, f, indent=2)
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
                        self._save_license(clean_key, buyer_email, tier="Pro")
                        return True, "License successfully verified and activated online!"
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
