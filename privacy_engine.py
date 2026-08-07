#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
================================================================================
 WinCare Pro - Windows 11 Privacy & Anti-Spying Engine
================================================================================
 Module for controlling Windows 11 telemetry, Bing search integration,
 Copilot & AI Recall features, Advertising ID, Location tracking, and
 Tailored Experiences app diagnostics.
================================================================================
"""

import os
import sys
import logging
import subprocess
import ctypes
from typing import Callable, Optional, Dict, Any

# POSIX / Windows safe winreg import
try:
    import winreg
except ImportError:
    winreg = None

CREATE_NO_WINDOW = 0x08000000 if os.name == "nt" else 0

# Set up logger
logger = logging.getLogger("WinCarePro.PrivacyEngine")
if not logger.handlers:
    handler = logging.StreamHandler()
    formatter = logging.Formatter("[%(levelname)s] %(asctime)s - %(message)s", "%H:%M:%S")
    handler.setFormatter(formatter)
    logger.addHandler(handler)
    logger.setLevel(logging.INFO)


def is_admin() -> bool:
    """Check if current process has administrative privileges on Windows."""
    if os.name != "nt":
        return False
    try:
        return bool(ctypes.windll.shell32.IsUserAnAdmin())
    except Exception:
        return False


def privacy_protection_switches(states: Dict[str, Any]) -> Dict[str, bool]:
    """Translate engine states into UI switches whose labels begin with 'Disable'."""
    states = states or {}
    return {
        "bing": states.get("bing_start_search") is False,
        "copilot": states.get("copilot_recall") is False,
        "advertising_id": states.get("advertising_id") is False,
        "telemetry": states.get("telemetry_level") == 0,
        "location": states.get("location_tracking") is False,
        "app_diagnostics": states.get("app_diagnostics") is False,
    }


class PrivacyShield:
    """
    Windows 11 Privacy, Telemetry Shield & Anti-Spying Manager.
    Allows inspecting and modifying key privacy and tracking registry keys.
    """

    def __init__(self):
        self._admin = is_admin()

    def _read_dword(self, root_key, subkey: str, value_name: str, default: int = 0) -> int:
        """Reads a DWORD registry value safely."""
        if winreg is None:
            return default
        try:
            with winreg.OpenKey(root_key, subkey, 0, winreg.KEY_READ) as key:
                val, val_type = winreg.QueryValueEx(key, value_name)
                if val_type in (winreg.REG_DWORD, winreg.REG_SZ):
                    return int(val)
        except FileNotFoundError:
            return default
        except Exception as e:
            logger.debug(f"Error reading registry {subkey}\\{value_name}: {e}")
            return default
        return default

    def _write_dword(self, root_key, root_str: str, subkey: str, value_name: str, value: int) -> bool:
        """
        Writes a DWORD registry value safely using winreg, falling back to reg.exe if permission denied.
        """
        if winreg is None:
            logger.warning("winreg is not available on this platform.")
            return False

        # Attempt winreg write
        try:
            with winreg.CreateKeyEx(root_key, subkey, 0, winreg.KEY_SET_VALUE | winreg.KEY_WRITE) as key:
                winreg.SetValueEx(key, value_name, 0, winreg.REG_DWORD, value)
            return True
        except PermissionError:
            logger.warning(f"Permission denied for winreg write on {root_str}\\{subkey}. Attempting reg.exe fallback...")
        except Exception as e:
            logger.debug(f"winreg error on {root_str}\\{subkey}: {e}. Attempting fallback...")

        # Fallback to reg.exe command line tool
        try:
            cmd = ["reg", "add", f"{root_str}\\{subkey}", "/v", value_name, "/t", "REG_DWORD", "/d", str(value), "/f"]
            res = subprocess.run(cmd, capture_output=True, text=True, creationflags=CREATE_NO_WINDOW)
            if res.returncode == 0:
                return True
            else:
                logger.error(f"reg.exe failed to write {root_str}\\{subkey}\\{value_name}: {res.stderr.strip()}")
        except Exception as ex:
            logger.error(f"Fallback reg.exe execution failed: {ex}")

        return False

    # --------------------------------------------------------------------------
    # Individual Toggle Checkers & Mutators
    # --------------------------------------------------------------------------

    def get_bing_start_search(self) -> bool:
        """
        Returns True if Bing search suggestions in Start menu are ENABLED, False if DISABLED.
        Registry: HKCU\\Software\\Policies\\Microsoft\\Windows\\Explorer -> DisableSearchBoxSuggestions
        (0 = enabled, 1 = disabled)
        """
        if winreg is None:
            return True
        val = self._read_dword(winreg.HKEY_CURRENT_USER, r"Software\Policies\Microsoft\Windows\Explorer", "DisableSearchBoxSuggestions", default=0)
        return val == 0

    def set_bing_start_search(self, enable: bool) -> bool:
        """
        Enable (enable=True) or Disable (enable=False) Bing Start Menu Search & Suggestions.
        Registry: HKCU\\Software\\Policies\\Microsoft\\Windows\\Explorer -> DisableSearchBoxSuggestions (1=Disabled, 0=Enabled)
        Also sets HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\Search -> BingSearchEnabled
        """
        val = 0 if enable else 1
        success1 = self._write_dword(winreg.HKEY_CURRENT_USER, "HKCU", r"Software\Policies\Microsoft\Windows\Explorer", "DisableSearchBoxSuggestions", val)
        bing_val = 1 if enable else 0
        success2 = self._write_dword(winreg.HKEY_CURRENT_USER, "HKCU", r"Software\Microsoft\Windows\CurrentVersion\Search", "BingSearchEnabled", bing_val)
        return success1 and success2

    def get_copilot_recall(self) -> bool:
        """
        Returns True if Copilot & Recall are ENABLED, False if DISABLED.
        Registry: HKCU\\Software\\Policies\\Microsoft\\Windows\\WindowsCopilot -> TurnOffWindowsCopilot (0=Enabled, 1=Disabled)
        """
        if winreg is None:
            return True
        val = self._read_dword(winreg.HKEY_CURRENT_USER, r"Software\Policies\Microsoft\Windows\WindowsCopilot", "TurnOffWindowsCopilot", default=0)
        return val == 0

    def set_copilot_recall(self, enable: bool) -> bool:
        """
        Enable (enable=True) or Disable (enable=False) Windows Copilot & AI Recall.
        Registry: HKCU\\Software\\Policies\\Microsoft\\Windows\\WindowsCopilot -> TurnOffWindowsCopilot (1=Disabled, 0=Enabled)
        And DisableAIDataAnalysis (1=Disabled, 0=Enabled)
        """
        turn_off_val = 0 if enable else 1
        disable_ai_val = 0 if enable else 1
        s1 = self._write_dword(winreg.HKEY_CURRENT_USER, "HKCU", r"Software\Policies\Microsoft\Windows\WindowsCopilot", "TurnOffWindowsCopilot", turn_off_val)
        s2 = self._write_dword(winreg.HKEY_CURRENT_USER, "HKCU", r"Software\Policies\Microsoft\Windows\WindowsCopilot", "DisableAIDataAnalysis", disable_ai_val)
        return s1 and s2

    def get_advertising_id(self) -> bool:
        """
        Returns True if Windows Advertising ID is ENABLED, False if DISABLED.
        Registry: HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\AdvertisingInfo -> Enabled (1=Enabled, 0=Disabled)
        """
        if winreg is None:
            return True
        val = self._read_dword(winreg.HKEY_CURRENT_USER, r"Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo", "Enabled", default=1)
        return val == 1

    def set_advertising_id(self, enable: bool) -> bool:
        """
        Enable (enable=True) or Disable (enable=False) Advertising ID.
        Registry: HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\AdvertisingInfo -> Enabled (1=Enabled, 0=Disabled)
        """
        val = 1 if enable else 0
        return self._write_dword(winreg.HKEY_CURRENT_USER, "HKCU", r"Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo", "Enabled", val)

    def get_telemetry_level(self) -> int:
        """
        Returns current telemetry level:
          0 = Security / Min (Enterprise/Edu only, min on Home/Pro)
          1 = Basic (Required diagnostic data)
          3 = Full (Optional diagnostic data)
        Registry: HKLM\\SOFTWARE\\Policies\\Microsoft\\Windows\\DataCollection -> AllowTelemetry (Default: 3)
        """
        if winreg is None:
            return 3
        return self._read_dword(winreg.HKEY_LOCAL_MACHINE, r"SOFTWARE\Policies\Microsoft\Windows\DataCollection", "AllowTelemetry", default=3)

    def set_telemetry_level(self, level: int) -> bool:
        """
        Set Windows Telemetry Level.
          level: 0 (Security/Min), 1 (Basic), 3 (Full)
        Registry: HKLM\\SOFTWARE\\Policies\\Microsoft\\Windows\\DataCollection -> AllowTelemetry
        """
        if level not in (0, 1, 2, 3):
            logger.warning(f"Invalid telemetry level {level}, defaulting to 1 (Basic).")
            level = 1
        return self._write_dword(winreg.HKEY_LOCAL_MACHINE, "HKLM", r"SOFTWARE\Policies\Microsoft\Windows\DataCollection", "AllowTelemetry", level)

    def get_location_tracking(self) -> bool:
        """
        Returns True if Windows Location Tracking is ENABLED, False if DISABLED.
        Registry: HKLM\\SOFTWARE\\Policies\\Microsoft\\Windows\\LocationAndSensors -> DisableLocation (0=Enabled, 1=Disabled)
        """
        if winreg is None:
            return True
        val = self._read_dword(winreg.HKEY_LOCAL_MACHINE, r"SOFTWARE\Policies\Microsoft\Windows\LocationAndSensors", "DisableLocation", default=0)
        return val == 0

    def set_location_tracking(self, enable: bool) -> bool:
        """
        Enable (enable=True) or Disable (enable=False) Location Tracking.
        Registry: HKLM\\SOFTWARE\\Policies\\Microsoft\\Windows\\LocationAndSensors -> DisableLocation (1=Disabled, 0=Enabled)
        """
        val = 0 if enable else 1
        return self._write_dword(winreg.HKEY_LOCAL_MACHINE, "HKLM", r"SOFTWARE\Policies\Microsoft\Windows\LocationAndSensors", "DisableLocation", val)

    def get_app_diagnostics(self) -> bool:
        """
        Returns True if App Diagnostics / Tailored Experiences are ENABLED, False if DISABLED.
        Registry: HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\Privacy -> TailoredExperiencesWithDiagnosticDataEnabled (1=Enabled, 0=Disabled)
        """
        if winreg is None:
            return True
        val = self._read_dword(winreg.HKEY_CURRENT_USER, r"Software\Microsoft\Windows\CurrentVersion\Privacy", "TailoredExperiencesWithDiagnosticDataEnabled", default=1)
        return val == 1

    def set_app_diagnostics(self, enable: bool) -> bool:
        """
        Enable (enable=True) or Disable (enable=False) App Diagnostics / Tailored Experiences.
        Registry: HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\Privacy -> TailoredExperiencesWithDiagnosticDataEnabled (1=Enabled, 0=Disabled)
        """
        val = 1 if enable else 0
        return self._write_dword(winreg.HKEY_CURRENT_USER, "HKCU", r"Software\Microsoft\Windows\CurrentVersion\Privacy", "TailoredExperiencesWithDiagnosticDataEnabled", val)

    # --------------------------------------------------------------------------
    # Bulk State Inspection & Presets
    # --------------------------------------------------------------------------

    def get_all_states(self) -> Dict[str, Any]:
        """
        Returns a dictionary containing the state of all privacy toggles.
        """
        return {
            "bing_start_search": self.get_bing_start_search(),
            "copilot_recall": self.get_copilot_recall(),
            "advertising_id": self.get_advertising_id(),
            "telemetry_level": self.get_telemetry_level(),
            "location_tracking": self.get_location_tracking(),
            "app_diagnostics": self.get_app_diagnostics(),
        }

    def apply_privacy_preset(self, preset: str, callback_out: Optional[Callable[[str], None]] = None) -> int:
        """
        Applies a named privacy preset.
        Presets:
          - 'maximum_privacy': Disables Bing search, Copilot/Recall, Advertising ID,
                               Location tracking, App diagnostics, sets Telemetry to Security (0).
          - 'balanced_privacy': Disables Bing search, Copilot/Recall, Advertising ID,
                                App diagnostics, sets Telemetry to Basic (1), keeps Location enabled.
          - 'restore_defaults': Enables Bing search, Copilot/Recall, Advertising ID,
                                Location tracking, App diagnostics, sets Telemetry to Full (3).

        Returns:
          int: Number of toggles successfully applied.
        """
        def log(msg: str):
            logger.info(msg)
            if callback_out and callable(callback_out):
                try:
                    callback_out(msg)
                except Exception as e:
                    logger.debug(f"Callback exception: {e}")

        preset_key = preset.lower().strip()
        log(f"--- Applying Privacy Preset: {preset} ---")

        applied_count = 0

        if preset_key == "maximum_privacy":
            log("Configuring Maximum Privacy preset...")
            if self.set_bing_start_search(False):
                log(" [✓] Bing Start Menu Search disabled.")
                applied_count += 1
            else:
                log(" [✗] Failed to disable Bing Start Search.")

            if self.set_copilot_recall(False):
                log(" [✓] Windows Copilot & AI Recall disabled.")
                applied_count += 1
            else:
                log(" [✗] Failed to disable Copilot & Recall.")

            if self.set_advertising_id(False):
                log(" [✓] Windows Advertising ID disabled.")
                applied_count += 1
            else:
                log(" [✗] Failed to disable Advertising ID.")

            if self.set_telemetry_level(0):
                log(" [✓] Telemetry level set to 0 (Security / Minimum).")
                applied_count += 1
            else:
                log(" [✗] Failed to set Telemetry level.")

            if self.set_location_tracking(False):
                log(" [✓] Location tracking disabled.")
                applied_count += 1
            else:
                log(" [✗] Failed to disable Location tracking.")

            if self.set_app_diagnostics(False):
                log(" [✓] App diagnostics / Tailored Experiences disabled.")
                applied_count += 1
            else:
                log(" [✗] Failed to disable App diagnostics.")

        elif preset_key == "balanced_privacy":
            log("Configuring Balanced Privacy preset...")
            if self.set_bing_start_search(False):
                log(" [✓] Bing Start Menu Search disabled.")
                applied_count += 1
            else:
                log(" [✗] Failed to disable Bing Start Search.")

            if self.set_copilot_recall(False):
                log(" [✓] Windows Copilot & AI Recall disabled.")
                applied_count += 1
            else:
                log(" [✗] Failed to disable Copilot & Recall.")

            if self.set_advertising_id(False):
                log(" [✓] Windows Advertising ID disabled.")
                applied_count += 1
            else:
                log(" [✗] Failed to disable Advertising ID.")

            if self.set_telemetry_level(1):
                log(" [✓] Telemetry level set to 1 (Basic).")
                applied_count += 1
            else:
                log(" [✗] Failed to set Telemetry level.")

            if self.set_location_tracking(True):
                log(" [✓] Location tracking enabled (for Maps/Services).")
                applied_count += 1
            else:
                log(" [✗] Failed to configure Location tracking.")

            if self.set_app_diagnostics(False):
                log(" [✓] App diagnostics / Tailored Experiences disabled.")
                applied_count += 1
            else:
                log(" [✗] Failed to disable App diagnostics.")

        elif preset_key == "restore_defaults":
            log("Restoring Windows Default Privacy settings...")
            if self.set_bing_start_search(True):
                log(" [✓] Bing Start Menu Search restored (Enabled).")
                applied_count += 1
            else:
                log(" [✗] Failed to restore Bing Start Search.")

            if self.set_copilot_recall(True):
                log(" [✓] Windows Copilot & AI Recall restored (Enabled).")
                applied_count += 1
            else:
                log(" [✗] Failed to restore Copilot & Recall.")

            if self.set_advertising_id(True):
                log(" [✓] Windows Advertising ID restored (Enabled).")
                applied_count += 1
            else:
                log(" [✗] Failed to restore Advertising ID.")

            if self.set_telemetry_level(3):
                log(" [✓] Telemetry level restored to 3 (Full).")
                applied_count += 1
            else:
                log(" [✗] Failed to restore Telemetry level.")

            if self.set_location_tracking(True):
                log(" [✓] Location tracking restored (Enabled).")
                applied_count += 1
            else:
                log(" [✗] Failed to restore Location tracking.")

            if self.set_app_diagnostics(True):
                log(" [✓] App diagnostics restored (Enabled).")
                applied_count += 1
            else:
                log(" [✗] Failed to restore App diagnostics.")

        else:
            log(f" [!] Unknown privacy preset: '{preset}'. Valid options: 'maximum_privacy', 'balanced_privacy', 'restore_defaults'.")

        log(f"Preset '{preset}' application completed ({applied_count}/6 toggles applied).")
        return applied_count


if __name__ == "__main__":
    print("==========================================================================")
    print(" WinCare Pro - Privacy Shield Standalone Verification")
    print("==========================================================================")
    print(f"Platform: {sys.platform}")
    print(f"Is Admin: {is_admin()}")
    
    shield = PrivacyShield()
    states = shield.get_all_states()
    
    print("\n--- Current Privacy & Telemetry States ---")
    for k, v in states.items():
        print(f"  {k}: {v}")
        
    print("\n--- Standalone Verification Complete ---")
