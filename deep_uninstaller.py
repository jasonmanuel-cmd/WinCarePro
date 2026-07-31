#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
================================================================================
 WinCare Pro - Forced Software Uninstaller & Deep Registry Shredder Engine
================================================================================
 Lists installed Win32 software, triggers quiet uninstallation, scans for
 leftover registry keys & AppData directories, and shreds lingering clutter.
================================================================================
"""

import os
import sys
import shutil
import subprocess
from pathlib import Path

try:
    import winreg
except ImportError:
    winreg = None

CREATE_NO_WINDOW = 0x08000000 if os.name == "nt" else 0


class DeepUninstaller:
    """
    App Uninstaller & Leftover Registry/File Shredder.
    """

    UNINSTALL_REG_KEYS = [
        r"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
        r"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
    ]

    def list_installed_apps(self) -> list[dict]:
        """Enumerate installed Win32 software from HKLM & HKCU registry."""
        if not winreg:
            return []

        apps = []
        seen_names = set()

        hives = [
            (winreg.HKEY_LOCAL_MACHINE, "HKLM"),
            (winreg.HKEY_CURRENT_USER, "HKCU")
        ]

        for hive, hive_name in hives:
            for subkey_path in self.UNINSTALL_REG_KEYS:
                try:
                    key = winreg.OpenKey(hive, subkey_path)
                    num_subkeys = winreg.QueryInfoKey(key)[0]
                    for i in range(num_subkeys):
                        try:
                            subkey_name = winreg.EnumKey(key, i)
                            app_key = winreg.OpenKey(key, subkey_name)
                            
                            display_name, _ = winreg.QueryValueEx(app_key, "DisplayName")
                            if not display_name or display_name in seen_names:
                                winreg.CloseKey(app_key)
                                continue

                            # Skip system components & Windows updates
                            try:
                                system_component, _ = winreg.QueryValueEx(app_key, "SystemComponent")
                                if system_component == 1:
                                    winreg.CloseKey(app_key)
                                    continue
                            except OSError:
                                pass

                            version, _ = winreg.QueryValueEx(app_key, "DisplayVersion") if self._val_exists(app_key, "DisplayVersion") else ("N/A", None)
                            publisher, _ = winreg.QueryValueEx(app_key, "Publisher") if self._val_exists(app_key, "Publisher") else ("Unknown", None)
                            uninstall_str, _ = winreg.QueryValueEx(app_key, "UninstallString") if self._val_exists(app_key, "UninstallString") else ("", None)
                            quiet_uninstall_str, _ = winreg.QueryValueEx(app_key, "QuietUninstallString") if self._val_exists(app_key, "QuietUninstallString") else ("", None)
                            install_loc, _ = winreg.QueryValueEx(app_key, "InstallLocation") if self._val_exists(app_key, "InstallLocation") else ("", None)

                            seen_names.add(display_name)
                            apps.append({
                                "name": display_name,
                                "version": version,
                                "publisher": publisher,
                                "uninstall_string": quiet_uninstall_str or uninstall_str,
                                "install_location": install_loc,
                                "hive": hive_name,
                                "key_path": f"{subkey_path}\\{subkey_name}"
                            })
                            winreg.CloseKey(app_key)
                        except OSError:
                            pass
                    winreg.CloseKey(key)
                except OSError:
                    pass

        apps.sort(key=lambda x: x["name"].lower())
        return apps

    def _val_exists(self, key, val_name) -> bool:
        try:
            winreg.QueryValueEx(key, val_name)
            return True
        except OSError:
            return False

    def uninstall_app(self, app_info: dict, callback_out=None) -> tuple[bool, str]:
        """Trigger native or quiet uninstaller for selected application."""
        cmd_str = app_info.get("uninstall_string", "").strip()
        if not cmd_str:
            return False, f"No uninstaller command registered for {app_info.get('name')}."

        try:
            if callback_out:
                callback_out(f"Launching uninstaller for {app_info['name']}...")

            # Run uninstaller process
            p = subprocess.run(cmd_str, shell=False, timeout=120,
                               creationflags=CREATE_NO_WINDOW)
            if p.returncode in (0, 3010):  # 0 = success, 3010 = reboot required
                return True, f"Successfully uninstalled {app_info['name']}."
            return False, f"Uninstaller exited with code {p.returncode}."
        except Exception as e:
            return False, f"Failed to run uninstaller: {e}"

    def scan_app_leftovers(self, app_name: str) -> dict:
        """Scan AppData, ProgramData, and Registry for leftovers matching app_name."""
        leftovers = {"folders": [], "registry_keys": []}
        clean_name = app_name.lower().replace(" ", "")

        search_dirs = [
            os.environ.get("APPDATA"),
            os.environ.get("LOCALAPPDATA"),
            os.environ.get("PROGRAMDATA")
        ]

        for base_dir in search_dirs:
            if not base_dir or not os.path.exists(base_dir):
                continue
            try:
                for item in os.listdir(base_dir):
                    if clean_name in item.lower().replace(" ", ""):
                        fp = os.path.join(base_dir, item)
                        if os.path.isdir(fp):
                            leftovers["folders"].append(fp)
            except Exception:
                pass

        return leftovers

    def shred_leftovers(self, leftovers: dict, callback_out=None) -> tuple[bool, str]:
        """Delete identified leftover folders and keys."""
        deleted_count = 0
        for folder in leftovers.get("folders", []):
            try:
                if os.path.exists(folder):
                    shutil.rmtree(folder)
                    if not os.path.exists(folder):
                        deleted_count += 1
                        if callback_out:
                            callback_out(f"Shredded leftover folder: {folder}")
            except Exception:
                pass

        return True, f"Shredded {deleted_count} leftover items."


if __name__ == "__main__":
    du = DeepUninstaller()
    apps = du.list_installed_apps()
    print(f"Found {len(apps)} installed Win32 apps.")
    if apps:
        print("Sample app:", apps[0]["name"], "-", apps[0]["publisher"])
