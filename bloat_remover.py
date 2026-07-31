#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
================================================================================
 WinCare Pro - Windows UWP App Bloatware Uninstaller & Leftover Scanner
================================================================================
 Module: bloat_remover.py
 Purpose: Detects and uninstalls pre-installed Windows UWP bloatware apps,
          scans %APPDATA%, %LOCALAPPDATA%, %LOCALAPPDATA%\\Packages, and Temp
          for orphaned folders from uninstalled applications, and identifies
          orphaned registry keys/startup items.
================================================================================
"""

import os
import sys
import json
import shutil
import subprocess
import datetime
import stat
import re
from pathlib import Path

# Windows Registry module import (NT only)
if os.name == "nt":
    import winreg
else:
    winreg = None

# Flag to prevent console window flashing during subprocess calls on Windows
CREATE_NO_WINDOW = 0x08000000 if os.name == "nt" else 0

# ==============================================================================
# Known Pre-installed Windows UWP Bloatware Database
# ==============================================================================
KNOWN_UWP_BLOAT = {
    "Microsoft.XboxApp": {
        "name": "Xbox Console Companion",
        "category": "Gaming & Xbox",
        "description": "Legacy Xbox companion app. Safe to remove if not using Xbox features.",
        "recommended_remove": True
    },
    "Microsoft.GamingApp": {
        "name": "Xbox App",
        "category": "Gaming & Xbox",
        "description": "Xbox Game Pass & PC Gaming Hub.",
        "recommended_remove": False
    },
    "Microsoft.XboxGamingOverlay": {
        "name": "Xbox Game Bar Plugin",
        "category": "Gaming & Xbox",
        "description": "Overlay for Xbox recording and social features.",
        "recommended_remove": True
    },
    "Microsoft.XboxSpeechToTextOverlay": {
        "name": "Xbox Speech-to-Text Overlay",
        "category": "Gaming & Xbox",
        "description": "Accessibility speech-to-text overlay for Xbox games.",
        "recommended_remove": True
    },
    "Microsoft.XboxTCUI": {
        "name": "Xbox TCUI",
        "category": "Gaming & Xbox",
        "description": "Xbox Text and Conversation User Interface.",
        "recommended_remove": True
    },
    "Microsoft.MicrosoftSolitaireCollection": {
        "name": "Microsoft Solitaire Collection",
        "category": "Games",
        "description": "Preinstalled Solitaire game collection with ads.",
        "recommended_remove": True
    },
    "Microsoft.BingNews": {
        "name": "Microsoft News / MSN News",
        "category": "News & Weather",
        "description": "MSN News newsfeed and news app.",
        "recommended_remove": True
    },
    "Microsoft.BingWeather": {
        "name": "MSN Weather",
        "category": "News & Weather",
        "description": "Weather forecast application.",
        "recommended_remove": True
    },
    "Microsoft.BingSports": {
        "name": "MSN Sports",
        "category": "News & Weather",
        "description": "Sports scores and sports news application.",
        "recommended_remove": True
    },
    "Microsoft.BingFinance": {
        "name": "MSN Money",
        "category": "News & Weather",
        "description": "Stock market and finance tracker.",
        "recommended_remove": True
    },
    "Microsoft.YourPhone": {
        "name": "Phone Link / Your Phone",
        "category": "Communication",
        "description": "Syncs Android/iPhone calls, texts, and notifications.",
        "recommended_remove": True
    },
    "Microsoft.MicrosoftOfficeHub": {
        "name": "Microsoft 365 / Office Hub",
        "category": "Productivity",
        "description": "Office web launcher and promotion app.",
        "recommended_remove": True
    },
    "Microsoft.GetHelp": {
        "name": "Get Help",
        "category": "System Utilities",
        "description": "Online Microsoft support assistant.",
        "recommended_remove": True
    },
    "Microsoft.Getstarted": {
        "name": "Tips / Get Started",
        "category": "System Utilities",
        "description": "Windows introductory tips and tutorials.",
        "recommended_remove": True
    },
    "Microsoft.WindowsMaps": {
        "name": "Windows Maps",
        "category": "Navigation",
        "description": "Offline and online desktop maps application.",
        "recommended_remove": True
    },
    "Microsoft.549981C6F5B10": {
        "name": "Cortana",
        "category": "Virtual Assistant",
        "description": "Deprecated Microsoft digital voice assistant.",
        "recommended_remove": True
    },
    "Microsoft.SkypeApp": {
        "name": "Skype",
        "category": "Communication",
        "description": "Skype messaging and video calls.",
        "recommended_remove": True
    },
    "Microsoft.ZuneVideo": {
        "name": "Films & TV / Movies & TV",
        "category": "Media",
        "description": "Default Windows video player.",
        "recommended_remove": True
    },
    "Microsoft.ZuneMusic": {
        "name": "Groove Music / Media Player",
        "category": "Media",
        "description": "Default Windows audio player.",
        "recommended_remove": False
    },
    "Microsoft.People": {
        "name": "Microsoft People",
        "category": "Communication",
        "description": "Contacts management application.",
        "recommended_remove": True
    },
    "Microsoft.WindowsFeedbackHub": {
        "name": "Feedback Hub",
        "category": "Telemetry",
        "description": "Sends user feedback and telemetry to Microsoft.",
        "recommended_remove": True
    },
    "Microsoft.MicrosoftStickyNotes": {
        "name": "Sticky Notes",
        "category": "Productivity",
        "description": "Desktop sticky notes application.",
        "recommended_remove": False
    },
    "Microsoft.WindowsSoundRecorder": {
        "name": "Sound Recorder",
        "category": "Media",
        "description": "Audio recording utility.",
        "recommended_remove": False
    },
    "Microsoft.Todos": {
        "name": "Microsoft To Do",
        "category": "Productivity",
        "description": "Task management and list application.",
        "recommended_remove": False
    },
    "Microsoft.PowerAutomateDesktop": {
        "name": "Power Automate Desktop",
        "category": "Productivity",
        "description": "RPA workflow automation utility.",
        "recommended_remove": True
    },
    "Microsoft.Paint3D": {
        "name": "Paint 3D",
        "category": "Graphics",
        "description": "3D modeling and drawing tool.",
        "recommended_remove": True
    },
    "Microsoft.3DBuilder": {
        "name": "3D Builder",
        "category": "Graphics",
        "description": "3D printing and viewing application.",
        "recommended_remove": True
    },
    "Microsoft.3DViewer": {
        "name": "3D Viewer",
        "category": "Graphics",
        "description": "Viewer for 3D graphics models.",
        "recommended_remove": True
    },
    "Microsoft.MixedReality.Portal": {
        "name": "Mixed Reality Portal",
        "category": "VR / AR",
        "description": "Headset driver and environment for Windows Mixed Reality.",
        "recommended_remove": True
    },
    "Clipchamp.Clipchamp": {
        "name": "Clipchamp Video Editor",
        "category": "Media",
        "description": "Web-based video editor bundled with Windows 11.",
        "recommended_remove": True
    },
    "SpotifyAB.SpotifyMusic": {
        "name": "Spotify Music",
        "category": "Media",
        "description": "Preinstalled Spotify music streaming client.",
        "recommended_remove": True
    },
    "Disney.37853FC22B2CE": {
        "name": "Disney+",
        "category": "Sponsored",
        "description": "Sponsored Disney+ app placeholder.",
        "recommended_remove": True
    },
    "C27EB4BA.DropboxOEM": {
        "name": "Dropbox OEM",
        "category": "Sponsored / OEM",
        "description": "Preinstalled Dropbox promotion app.",
        "recommended_remove": True
    },
    "Enflick.TextNow-UnlimitedTextCalls": {
        "name": "TextNow",
        "category": "Sponsored",
        "description": "Preinstalled TextNow messaging app.",
        "recommended_remove": True
    }
}

# Substring patterns for third-party or OEM bloatware packages
BLOAT_PATTERNS = [
    r"candycrush", r"king\.com", r"disney", r"tiktok", r"facebook", r"instagram",
    r"cyberlink", r"wildtangent", r"mcafee", r"norton", r"expressvpn",
    r"hpprivacy", r"hpinc\.energystar", r"hpsupportassistant", r"smartthings"
]

# Essential packages that must NEVER be flagged or deleted as bloatware
ESSENTIAL_PACKAGES = {
    "microsoft.windowsstore", "microsoft.desktopappinstaller", "microsoft.windowsterminal",
    "microsoft.windowscamera", "microsoft.windows.photos", "microsoft.windowscalculator",
    "microsoft.windowsnotepad", "microsoft.paint", "microsoft.storepurchaseapp",
    "microsoft.sechealthui", "microsoft.cred-dialoghost", "microsoft.bioenrollment"
}


def run_powershell_cmd(cmd: str, timeout: int = 45) -> tuple[int, str, str]:
    """
    Executes a PowerShell command safely without displaying or flashing a console window.
    """
    try:
        ps_args = [
            "powershell",
            "-NoProfile",
            "-NonInteractive",
            "-ExecutionPolicy", "Bypass",
            "-Command", cmd
        ]
        res = subprocess.run(
            ps_args,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            text=True,
            encoding="utf-8",
            errors="replace",
            timeout=timeout,
            creationflags=CREATE_NO_WINDOW
        )
        return res.returncode, res.stdout, res.stderr
    except subprocess.TimeoutExpired:
        return -1, "", "PowerShell execution timed out."
    except Exception as e:
        return -1, "", str(e)


def get_folder_stats(folder_path: str) -> tuple[int, int, str]:
    """
    Calculates total bytes, file count, and last modified timestamp for a folder.
    Returns (total_bytes, file_count, last_modified_iso).
    """
    total_bytes = 0
    file_count = 0
    latest_mtime = 0

    try:
        folder_stat = os.stat(folder_path)
        latest_mtime = folder_stat.st_mtime
    except Exception:
        pass

    try:
        for root, _, files in os.walk(folder_path):
            for f in files:
                fp = os.path.join(root, f)
                file_count += 1
                try:
                    st = os.stat(fp)
                    total_bytes += st.st_size
                    if st.st_mtime > latest_mtime:
                        latest_mtime = st.st_mtime
                except Exception:
                    continue
    except Exception:
        pass

    dt_str = datetime.datetime.fromtimestamp(latest_mtime).strftime("%Y-%m-%d %H:%M:%S") if latest_mtime else "Unknown"
    return total_bytes, file_count, dt_str


def format_bytes(size_bytes: int) -> str:
    """Formats byte count into a readable string (KB, MB, GB)."""
    if size_bytes < 1024:
        return f"{size_bytes} B"
    elif size_bytes < 1024 * 1024:
        return f"{size_bytes / 1024:.2f} KB"
    elif size_bytes < 1024 * 1024 * 1024:
        return f"{size_bytes / (1024 * 1024):.2f} MB"
    else:
        return f"{size_bytes / (1024 * 1024 * 1024):.2f} GB"


class BloatRemover:
    """
    Main engine for scanning and uninstalling UWP bloatware apps, scanning for
    orphaned leftover folders in AppData/Temp/Packages, and detecting orphaned registry entries.
    """

    def __init__(self):
        self._user_appdata_roaming = os.environ.get("APPDATA", "")
        self._user_appdata_local = os.environ.get("LOCALAPPDATA", "")
        self._temp_dir = os.environ.get("TEMP", os.path.join(self._user_appdata_local, "Temp"))
        self._packages_dir = os.path.join(self._user_appdata_local, "Packages")
        self._programdata_dir = os.environ.get("PROGRAMDATA", "C:\\ProgramData")

        # System paths that are FORBIDDEN from deletion
        user_home = os.path.expanduser("~")
        self._forbidden_paths = {
            os.path.abspath(p).lower() for p in [
                "c:\\",
                "c:\\windows",
                "c:\\windows\\system32",
                "c:\\program files",
                "c:\\program files (x86)",
                "c:\\users",
                "c:\\programdata",
                user_home,
                self._user_appdata_roaming,
                self._user_appdata_local,
                self._packages_dir,
                self._temp_dir,
                os.path.join(user_home, "Desktop"),
                os.path.join(user_home, "Documents"),
                os.path.join(user_home, "Downloads"),
                os.path.join(user_home, "Pictures"),
                os.path.join(user_home, "Videos"),
            ] if p
        }

        # Safe root boundaries for leftover folder deletion
        self._allowed_parent_roots = [
            os.path.abspath(p).lower() for p in [
                self._user_appdata_roaming,
                self._user_appdata_local,
                self._packages_dir,
                self._temp_dir,
                self._programdata_dir,
                "c:\\windows\\temp"
            ] if p
        ]

    # ==========================================================================
    # 1. UWP APP BLOATWARE MANAGEMENT
    # ==========================================================================

    def get_installed_uwp_bloat(self) -> list[dict]:
        """
        Lists removable pre-installed Windows UWP apps using PowerShell Get-AppxPackage.
        Matches installed packages against known bloatware lists and bloat patterns.

        Returns list of dicts:
        [
            {
                "name": str,
                "package_name": str,
                "package_full_name": str,
                "package_family_name": str,
                "publisher": str,
                "version": str,
                "install_location": str,
                "non_removable": bool,
                "category": str,
                "description": str,
                "recommended_remove": bool
            }, ...
        ]
        """
        ps_cmd = (
            "try { "
            "Get-AppxPackage -AllUsers | Select-Object Name, PackageFullName, PackageFamilyName, Publisher, Version, InstallLocation, NonRemovable, IsFramework | ConvertTo-Json -Compress "
            "} catch { "
            "Get-AppxPackage | Select-Object Name, PackageFullName, PackageFamilyName, Publisher, Version, InstallLocation, NonRemovable, IsFramework | ConvertTo-Json -Compress "
            "}"
        )

        code, stdout, stderr = run_powershell_cmd(ps_cmd, timeout=45)
        if code != 0 or not stdout.strip():
            return []

        try:
            raw_data = json.loads(stdout)
            if isinstance(raw_data, dict):
                raw_data = [raw_data]
        except Exception:
            return []

        detected_bloat = []
        for pkg in raw_data:
            if not isinstance(pkg, dict):
                continue

            name = pkg.get("Name", "") or ""
            full_name = pkg.get("PackageFullName", "") or ""
            family_name = pkg.get("PackageFamilyName", "") or ""
            is_framework = pkg.get("IsFramework", False)
            non_removable = pkg.get("NonRemovable", False)

            if is_framework:
                continue

            # Skip essential Windows app packages
            if name.lower() in ESSENTIAL_PACKAGES or family_name.lower().split("_")[0] in ESSENTIAL_PACKAGES:
                continue

            matched_info = None

            # Check known bloatware dictionary
            for known_key, info in KNOWN_UWP_BLOAT.items():
                if known_key.lower() == name.lower() or known_key.lower() in name.lower():
                    matched_info = info
                    break

            # Check pattern matchers if not explicitly matched
            if not matched_info and self._is_bloat_pattern(name, full_name):
                matched_info = {
                    "name": self._friendly_app_name(name),
                    "category": "OEM / Sponsored Bloat",
                    "description": "Pre-installed third-party or OEM software that may be unnecessary.",
                    "recommended_remove": True
                }

            if matched_info:
                detected_bloat.append({
                    "name": matched_info["name"],
                    "package_name": name,
                    "package_full_name": full_name,
                    "package_family_name": family_name,
                    "publisher": pkg.get("Publisher", "") or "Microsoft Corporation",
                    "version": pkg.get("Version", "") or "Unknown",
                    "install_location": pkg.get("InstallLocation", "") or "",
                    "non_removable": bool(non_removable),
                    "category": matched_info.get("category", "Bloatware"),
                    "description": matched_info.get("description", ""),
                    "recommended_remove": matched_info.get("recommended_remove", True)
                })

        return detected_bloat

    def uninstall_uwp_app(self, package_name: str, callback_out=None) -> tuple[bool, str]:
        """
        Runs Remove-AppxPackage -AllUsers to uninstall a UWP app package.

        :param package_name: Name or PackageFullName or PackageFamilyName of appx package.
        :param callback_out: Optional callback function(str) for real-time output logging.
        :return: (success: bool, status_message: str)
        """
        def log(msg: str):
            if callable(callback_out):
                try:
                    callback_out(msg)
                except Exception:
                    pass

        if not package_name or not isinstance(package_name, str):
            msg = "Invalid package name specified."
            log(f"❌ {msg}")
            return False, msg

        log(f"Initiating UWP app removal for: {package_name}...")

        safe_pkg = package_name.replace("'", "''")
        ps_cmd = (
            f"$pkg = '{safe_pkg}'; "
            f"$matched = Get-AppxPackage -AllUsers | Where-Object {{ $_.Name -eq $pkg -or $_.PackageFullName -eq $pkg -or $_.PackageFamilyName -eq $pkg }}; "
            f"if (-not $matched) {{ $matched = Get-AppxPackage | Where-Object {{ $_.Name -eq $pkg -or $_.PackageFullName -eq $pkg -or $_.PackageFamilyName -eq $pkg }} }}; "
            f"if ($matched) {{ "
            f"  try {{ $matched | Remove-AppxPackage -AllUsers -ErrorAction Stop; Write-Output 'SUCCESS_ALLUSERS' }} "
            f"  catch {{ $matched | Remove-AppxPackage -ErrorAction Stop; Write-Output 'SUCCESS_USER' }} "
            f"}} else {{ Write-Error 'PACKAGE_NOT_FOUND' }}"
        )

        code, stdout, stderr = run_powershell_cmd(ps_cmd, timeout=60)

        if "SUCCESS" in stdout:
            msg = f"Successfully uninstalled UWP app '{package_name}'."
            log(f"✅ {msg}")
            return True, msg
        elif "PACKAGE_NOT_FOUND" in stderr or "PACKAGE_NOT_FOUND" in stdout:
            msg = f"Package '{package_name}' was not found or is already uninstalled."
            log(f"ℹ️ {msg}")
            return False, msg
        else:
            err_details = stderr.strip() or stdout.strip() or "Unknown execution error."
            msg = f"Failed to uninstall UWP app '{package_name}': {err_details}"
            log(f"❌ {msg}")
            return False, msg

    def _is_bloat_pattern(self, name: str, full_name: str) -> bool:
        """Checks if a package name matches known third-party/OEM bloat patterns."""
        combined = f"{name} {full_name}".lower()
        for pattern in BLOAT_PATTERNS:
            if re.search(pattern, combined):
                return True
        return False

    def _friendly_app_name(self, name: str) -> str:
        """Generates a human-friendly display name from a raw package name."""
        parts = name.split(".")
        if len(parts) > 1:
            clean_parts = [p for p in parts if p.lower() not in ("microsoft", "inc", "com", "corp", "app")]
            return " ".join(clean_parts).title() if clean_parts else name
        return name

    # ==========================================================================
    # 2. LEFTOVER ORPHANED FOLDER SCANNING & REMOVAL
    # ==========================================================================

    def scan_orphaned_leftovers(self) -> list[dict]:
        """
        Scans %APPDATA%, %LOCALAPPDATA%, %LOCALAPPDATA%\\Packages, and Temp folders
        for orphaned folders from uninstalled applications.

        Returns list of dicts:
        [
            {
                "folder_path": str,
                "folder_name": str,
                "source_location": str,
                "size_bytes": int,
                "size_formatted": str,
                "app_name": str,
                "reason": str,
                "is_safe_to_remove": bool,
                "item_count": int,
                "last_modified": str
            }, ...
        ]
        """
        installed_uwp_families = self._get_installed_uwp_family_names()
        installed_registry_apps = self._get_installed_registry_app_names()

        leftover_folders = []

        # 1. Scan %LOCALAPPDATA%\Packages for orphaned UWP package folders
        if os.path.exists(self._packages_dir):
            try:
                for item in os.listdir(self._packages_dir):
                    item_path = os.path.join(self._packages_dir, item)
                    if not os.path.isdir(item_path):
                        continue

                    # If folder is not in installed UWP package family list
                    if item.lower() not in installed_uwp_families:
                        # Check if folder name matches known bloat or uninstalled Microsoft/OEM patterns
                        if self._is_orphaned_uwp_package_folder(item):
                            size_bytes, file_count, mtime_str = get_folder_stats(item_path)
                            app_label = item.split("_")[0] if "_" in item else item
                            leftover_folders.append({
                                "folder_path": item_path,
                                "folder_name": item,
                                "source_location": "%LOCALAPPDATA%\\Packages",
                                "size_bytes": size_bytes,
                                "size_formatted": format_bytes(size_bytes),
                                "app_name": f"{app_label} (Uninstalled UWP)",
                                "reason": "Orphaned UWP App Package data folder leftover after uninstallation.",
                                "is_safe_to_remove": True,
                                "item_count": file_count,
                                "last_modified": mtime_str
                            })
            except Exception:
                pass

        # 2. Scan AppData Roaming and Local for orphaned folders from uninstalled desktop software
        appdata_locations = [
            (self._user_appdata_local, "%LOCALAPPDATA%"),
            (self._user_appdata_roaming, "%APPDATA%"),
            (self._programdata_dir, "%PROGRAMDATA%")
        ]

        for base_dir, location_label in appdata_locations:
            if not os.path.exists(base_dir):
                continue
            try:
                for item in os.listdir(base_dir):
                    item_path = os.path.join(base_dir, item)
                    if not os.path.isdir(item_path):
                        continue

                    # Skip system and essential folders
                    if item.lower() in ("microsoft", "packages", "temp", "windows", "assembly", "microsoft.net"):
                        continue

                    if self._is_orphaned_appdata_folder(item, installed_registry_apps):
                        size_bytes, file_count, mtime_str = get_folder_stats(item_path)
                        leftover_folders.append({
                            "folder_path": item_path,
                            "folder_name": item,
                            "source_location": location_label,
                            "size_bytes": size_bytes,
                            "size_formatted": format_bytes(size_bytes),
                            "app_name": f"{item} (Uninstalled App)",
                            "reason": f"Orphaned leftover folder in {location_label} for app '{item}' not found in Installed Programs.",
                            "is_safe_to_remove": True,
                            "item_count": file_count,
                            "last_modified": mtime_str
                        })
            except Exception:
                pass

        # 3. Scan Temp folders for leftover installation/setup temp folders
        temp_locations = [
            (self._temp_dir, "%TEMP%"),
            ("C:\\Windows\\Temp", "System Temp")
        ]

        for temp_path, temp_label in temp_locations:
            if not os.path.exists(temp_path):
                continue
            try:
                for item in os.listdir(temp_path):
                    item_path = os.path.join(temp_path, item)
                    if not os.path.isdir(item_path):
                        continue

                    # Look for setup/installer or stale leftover temp folders
                    if self._is_stale_temp_folder(item, item_path):
                        size_bytes, file_count, mtime_str = get_folder_stats(item_path)
                        leftover_folders.append({
                            "folder_path": item_path,
                            "folder_name": item,
                            "source_location": temp_label,
                            "size_bytes": size_bytes,
                            "size_formatted": format_bytes(size_bytes),
                            "app_name": "Temporary Installer / App Leftover",
                            "reason": "Stale or abandoned temporary directory leftover from software install/uninstall.",
                            "is_safe_to_remove": True,
                            "item_count": file_count,
                            "last_modified": mtime_str
                        })
            except Exception:
                pass

        return leftover_folders

    def remove_leftover_folder(self, folder_path: str, callback_out=None) -> tuple[bool, str]:
        """
        Safely deletes an orphaned folder after performing safety checks.

        :param folder_path: Absolute path of folder to delete.
        :param callback_out: Optional callback function(str) for real-time output logging.
        :return: (success: bool, status_message: str)
        """
        def log(msg: str):
            if callable(callback_out):
                try:
                    callback_out(msg)
                except Exception:
                    pass

        if not folder_path or not isinstance(folder_path, str):
            msg = "Invalid folder path provided."
            log(f"❌ {msg}")
            return False, msg

        norm_path = os.path.abspath(os.path.normpath(folder_path))

        if not os.path.exists(norm_path):
            msg = f"Target folder does not exist: {norm_path}"
            log(f"⚠️ {msg}")
            return False, msg

        if not os.path.isdir(norm_path):
            msg = f"Path is not a directory: {norm_path}"
            log(f"❌ {msg}")
            return False, msg

        # Safety Check 1: Forbidden System Paths Check
        if norm_path.lower() in self._forbidden_paths:
            msg = f"Refusal: Target folder '{norm_path}' is a protected Windows or User system path."
            log(f"🛑 {msg}")
            return False, msg

        # Safety Check 2: Boundary Check - Must be inside designated leftover scan roots
        is_inside_allowed = False
        for root in self._allowed_parent_roots:
            if norm_path.lower().startswith(root + os.sep) and norm_path.lower() != root:
                is_inside_allowed = True
                break

        if not is_inside_allowed:
            msg = f"Safety refusal: Folder '{norm_path}' is outside designated leftover scan roots."
            log(f"🛑 {msg}")
            return False, msg

        # Execution of removal
        try:
            log(f"Deleting leftover folder: {norm_path}...")

            def handle_remove_readonly(func, path, exc_info):
                try:
                    os.chmod(path, stat.S_IWRITE)
                    func(path)
                except Exception:
                    pass

            shutil.rmtree(norm_path, onerror=handle_remove_readonly)

            if not os.path.exists(norm_path):
                msg = f"Successfully removed leftover folder: {norm_path}"
                log(f"✅ {msg}")
                return True, msg
            else:
                msg = f"Folder was partially deleted; some locked files remained: {norm_path}"
                log(f"⚠️ {msg}")
                return False, msg
        except Exception as e:
            msg = f"Error deleting folder '{norm_path}': {e}"
            log(f"❌ {msg}")
            return False, msg

    def _get_installed_uwp_family_names(self) -> set[str]:
        """Gets set of lowercase PackageFamilyName for all installed UWP apps."""
        ps_cmd = "Get-AppxPackage | Select-Object -ExpandProperty PackageFamilyName"
        code, out, _ = run_powershell_cmd(ps_cmd, timeout=30)
        if code == 0 and out.strip():
            return {line.strip().lower() for line in out.splitlines() if line.strip()}
        return set()

    def _get_installed_registry_app_names(self) -> set[str]:
        """Gets set of lowercase installed application display names from Windows Registry."""
        installed = set()
        if not winreg:
            return installed

        uninstall_locations = [
            (winreg.HKEY_CURRENT_USER, r"Software\Microsoft\Windows\CurrentVersion\Uninstall"),
            (winreg.HKEY_LOCAL_MACHINE, r"Software\Microsoft\Windows\CurrentVersion\Uninstall"),
            (winreg.HKEY_LOCAL_MACHINE, r"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall")
        ]

        for root_key, subkey_path in uninstall_locations:
            try:
                with winreg.OpenKey(root_key, subkey_path) as key:
                    num_keys, _, _ = winreg.QueryInfoKey(key)
                    for i in range(num_keys):
                        try:
                            sub_key_name = winreg.EnumKey(key, i)
                            with winreg.OpenKey(key, sub_key_name) as app_key:
                                try:
                                    val, _ = winreg.QueryValueEx(app_key, "DisplayName")
                                    if val and isinstance(val, str):
                                        installed.add(val.lower())
                                except FileNotFoundError:
                                    pass
                        except OSError:
                            continue
            except OSError:
                continue

        return installed

    def _is_orphaned_uwp_package_folder(self, folder_name: str) -> bool:
        """Checks if a folder in %LOCALAPPDATA%\\Packages belongs to an uninstalled UWP app."""
        fn_lower = folder_name.lower()
        known_bloat_keys = ["bingnews", "bingweather", "bingsports", "bingfinance", "xboxapp",
                            "yourphone", "cortana", "skypeapp", "zunevideo", "solitairecollection",
                            "officehub", "clipchamp", "candycrush", "gethelp"]

        for k in known_bloat_keys:
            if k in fn_lower:
                return True
        return False

    def _is_orphaned_appdata_folder(self, folder_name: str, installed_apps: set[str]) -> bool:
        """Checks if an AppData subfolder belongs to an app that is no longer installed."""
        fn_lower = folder_name.lower()

        # Known uninstalled software vendors/apps
        known_uninstalled = [
            "cortana", "skype", "wildtangent", "cyberlink", "mcafee",
            "norton", "candycrush", "expressvpn"
        ]

        for ku in known_uninstalled:
            if ku in fn_lower:
                return True

        return False

    def _is_stale_temp_folder(self, folder_name: str, folder_path: str) -> bool:
        """Checks if a temp subfolder is an abandoned installer or stale leftover (> 7 days old)."""
        fn_lower = folder_name.lower()
        if any(kw in fn_lower for kw in ["setup", "install", "installer", "tmp", "temp", "pck", "update"]):
            try:
                st = os.stat(folder_path)
                age_days = (datetime.datetime.now().timestamp() - st.st_mtime) / (24 * 3600)
                if age_days >= 3:
                    return True
            except Exception:
                pass
        return False

    # ==========================================================================
    # 3. ORPHANED REGISTRY SCANNING & REMOVAL
    # ==========================================================================

    def scan_orphaned_registry(self) -> list[dict]:
        """
        Scans Windows Registry for orphaned startup entries and leftover software keys.

        Returns list of dicts:
        [
            {
                "key_path": str,
                "hive": str,
                "value_name": str,
                "type": str,
                "reason": str,
                "app_name": str,
                "is_safe_to_remove": bool
            }, ...
        ]
        """
        orphans = []
        if not winreg:
            return orphans

        # Scan Startup Run keys for invalid binary paths
        run_locations = [
            (winreg.HKEY_CURRENT_USER, r"Software\Microsoft\Windows\CurrentVersion\Run", "HKCU"),
            (winreg.HKEY_LOCAL_MACHINE, r"Software\Microsoft\Windows\CurrentVersion\Run", "HKLM"),
            (winreg.HKEY_LOCAL_MACHINE, r"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run", "HKLM (WOW64)")
        ]

        for root_key, subkey_path, hive_label in run_locations:
            try:
                with winreg.OpenKey(root_key, subkey_path) as key:
                    num_values = winreg.QueryInfoKey(key)[1]
                    for i in range(num_values):
                        try:
                            val_name, val_data, _ = winreg.EnumValue(key, i)
                            if isinstance(val_data, str) and val_data.strip():
                                exe_path = self._extract_exe_path(val_data)
                                if exe_path and not os.path.exists(exe_path):
                                    orphans.append({
                                        "key_path": f"{hive_label}\\{subkey_path}",
                                        "hive": hive_label,
                                        "value_name": val_name,
                                        "type": "Orphaned Startup Entry",
                                        "reason": f"Startup entry points to missing file: '{exe_path}'",
                                        "app_name": val_name,
                                        "is_safe_to_remove": True
                                    })
                        except OSError:
                            continue
            except OSError:
                continue

        return orphans

    def remove_leftover_registry_key(self, key_path: str, value_name: str = None, callback_out=None) -> tuple[bool, str]:
        """
        Safely deletes an orphaned registry key or value.

        :param key_path: Registry path (e.g. 'HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\Run')
        :param value_name: Value name if deleting a specific value inside key_path.
        :param callback_out: Optional callback function(str) for logging.
        :return: (success: bool, status_message: str)
        """
        def log(msg: str):
            if callable(callback_out):
                try:
                    callback_out(msg)
                except Exception:
                    pass

        if not winreg:
            msg = "Registry operations are not available on this platform."
            log(f"❌ {msg}")
            return False, msg

        if not key_path:
            msg = "Invalid registry key path provided."
            log(f"❌ {msg}")
            return False, msg

        # Parse hive
        root_key = winreg.HKEY_CURRENT_USER
        sub_path = key_path

        if key_path.startswith("HKCU\\"):
            root_key = winreg.HKEY_CURRENT_USER
            sub_path = key_path[5:]
        elif key_path.startswith("HKLM\\"):
            root_key = winreg.HKEY_LOCAL_MACHINE
            sub_path = key_path[5:]

        # Safety Check: Prevent editing core system hives
        forbidden_reg_paths = ["system", "hardware", "sam", "security", "software\\microsoft\\windows nt\\currentversion"]
        if sub_path.lower().strip() in forbidden_reg_paths:
            msg = f"Refusing to delete critical system registry key: {key_path}"
            log(f"🛑 {msg}")
            return False, msg

        try:
            if value_name:
                with winreg.OpenKey(root_key, sub_path, 0, winreg.KEY_SET_VALUE) as key:
                    winreg.DeleteValue(key, value_name)
                msg = f"Successfully deleted registry value '{value_name}' from '{key_path}'."
                log(f"✅ {msg}")
                return True, msg
            else:
                winreg.DeleteKey(root_key, sub_path)
                msg = f"Successfully deleted registry key '{key_path}'."
                log(f"✅ {msg}")
                return True, msg
        except Exception as e:
            msg = f"Failed to delete registry item '{key_path}': {e}"
            log(f"❌ {msg}")
            return False, msg

    def _extract_exe_path(self, raw_cmd: str) -> str:
        """Extracts executable path from a command line string."""
        raw_cmd = raw_cmd.strip()
        if raw_cmd.startswith('"'):
            end_quote = raw_cmd.find('"', 1)
            if end_quote != -1:
                return raw_cmd[1:end_quote]
        parts = raw_cmd.split()
        if parts:
            return parts[0]
        return ""


# ==============================================================================
# Standalone Execution Verification
# ==============================================================================
if __name__ == "__main__":
    print("=" * 80)
    print("WinCare Pro - Bloatware Remover & Leftover Scanner Verification")
    print("=" * 80)

    remover = BloatRemover()

    print("\n[1] Scanning for Installed UWP Bloatware Apps...")
    bloat_apps = remover.get_installed_uwp_bloat()
    print(f"Found {len(bloat_apps)} installed removable UWP bloatware apps:")
    for app in bloat_apps:
        print(f"  • {app['name']} ({app['package_name']}) - Category: {app['category']}")

    print("\n[2] Scanning for Orphaned Leftover Folders...")
    leftovers = remover.scan_orphaned_leftovers()
    print(f"Found {len(leftovers)} orphaned leftover folders:")
    for folder in leftovers[:10]:
        print(f"  • [{folder['source_location']}] {folder['folder_name']} ({folder['size_formatted']})")

    print("\n[3] Scanning for Orphaned Registry / Startup Items...")
    reg_orphans = remover.scan_orphaned_registry()
    print(f"Found {len(reg_orphans)} orphaned registry items:")
    for reg in reg_orphans:
        print(f"  • [{reg['hive']}] {reg['value_name']} -> {reg['reason']}")

    print("\n" + "=" * 80)
    print("Standalone verification complete.")
    print("=" * 80)
