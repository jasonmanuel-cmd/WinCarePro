#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
================================================================================
 WinCare Pro - Windows System & File Baseline Intelligence Engine
================================================================================
 Provides knowledge of standard Windows 10/11 core files, required system
 processes, optional system services (Printers, Telemetry, Xbox, Remote), and
 background bloat. Provides safe 1-click optimization presets.
================================================================================
"""

import os
import sys
import json
import subprocess
import ctypes
from pathlib import Path
import psutil

CREATE_NO_WINDOW = 0x08000000 if os.name == "nt" else 0

# Core Windows System Executables that MUST NOT be killed or disabled
CORE_WINDOWS_PROCESSES = {
    "system": "Windows OS Kernel Process",
    "registry": "Windows Registry System Process",
    "smss.exe": "Session Manager Subsystem (Core Windows)",
    "csrss.exe": "Client Server Runtime Process (Core Windows Graphics/Threads)",
    "wininit.exe": "Windows Initialization Process",
    "services.exe": "Services and Controller App (Windows Service Host)",
    "lsass.exe": "Local Security Authority Subsystem Service (Security/Logon)",
    "svchost.exe": "Service Host Process for Windows Services",
    "fontdrvhost.exe": "Usermode Font Driver Host",
    "wuauclt.exe": "Windows Update AutoUpdate Client",
    "explorer.exe": "Windows Shell / Taskbar / Desktop",
    "dwm.exe": "Desktop Window Manager (Graphics Compositor)",
    "ctfmon.exe": "Alternative User Input Services (Keyboard/Language)",
    "sihost.exe": "Shell Infrastructure Host",
    "taskhostw.exe": "Host Process for Windows Tasks",
    "searchhost.exe": "Windows Search Indexer Host",
    "startmenuexperiencehost.exe": "Windows Start Menu Process",
    "runtimebroker.exe": "Windows App Permission Manager",
    "securityhealthservice.exe": "Windows Security Health Service",
    "msseces.exe": "Microsoft Security Essentials / Defender",
    "mpcmdrun.exe": "Microsoft Defender Command Line",
    "smartscreen.exe": "Windows Defender SmartScreen Filter",
    "conhost.exe": "Console Window Host",
    "spoolsv.exe": "Print Spooler Service Executable",
    "audiodg.exe": "Windows Audio Device Graph Isolation",
}

# Optional Windows Services categorized by function
OPTIONAL_SERVICES = {
    "Spooler": {
        "display_name": "Print Spooler",
        "category": "Printers",
        "description": "Manages printing tasks. Disable if you do NOT use any printer.",
        "exec": "spoolsv.exe",
        "recommended_stop": True
    },
    "PrintNotify": {
        "display_name": "Printer Extensions and Notifications",
        "category": "Printers",
        "description": "Provides custom printer driver notifications.",
        "exec": "svchost.exe",
        "recommended_stop": True
    },
    "Fax": {
        "display_name": "Fax Service",
        "category": "Printers & Legacy",
        "description": "Allows sending and receiving faxes. Completely obsolete for most users.",
        "exec": "fxssvc.exe",
        "recommended_stop": True
    },
    "DiagTrack": {
        "display_name": "Connected User Experiences and Telemetry",
        "category": "Telemetry & Tracking",
        "description": "Collects diagnostic and usage data sent to Microsoft.",
        "exec": "svchost.exe",
        "recommended_stop": True
    },
    "dmwappushservice": {
        "display_name": "Device Management Wireless Application Protocol Push",
        "category": "Telemetry & Tracking",
        "description": "WAP Push Message Routing for telemetry collection.",
        "exec": "svchost.exe",
        "recommended_stop": True
    },
    "XblAuthManager": {
        "display_name": "Xbox Live Auth Manager",
        "category": "Gaming & Xbox",
        "description": "Authentication for Xbox Live. Disable if you do not play Xbox games.",
        "exec": "svchost.exe",
        "recommended_stop": True
    },
    "XblGameSave": {
        "display_name": "Xbox Live Game Save",
        "category": "Gaming & Xbox",
        "description": "Syncs Xbox game save data. Safe to pause if not using Xbox Live.",
        "exec": "svchost.exe",
        "recommended_stop": True
    },
    "XboxNetApiSvc": {
        "display_name": "Xbox Live Networking Service",
        "category": "Gaming & Xbox",
        "description": "Xbox Live multiplayer networking assistance.",
        "exec": "svchost.exe",
        "recommended_stop": True
    },
    "XboxGipSvc": {
        "display_name": "Xbox Accessory Management Service",
        "category": "Gaming & Xbox",
        "description": "Wired and wireless Xbox controller driver management.",
        "exec": "svchost.exe",
        "recommended_stop": False
    },
    "RemoteRegistry": {
        "display_name": "Remote Registry",
        "category": "Security & Remote",
        "description": "Enables remote users to modify registry settings. Security risk if enabled.",
        "exec": "svchost.exe",
        "recommended_stop": True
    },
    "TermService": {
        "display_name": "Remote Desktop Services",
        "category": "Security & Remote",
        "description": "Allows remote desktop connections to this machine.",
        "exec": "svchost.exe",
        "recommended_stop": False
    },
    "bthserv": {
        "display_name": "Bluetooth Support Service",
        "category": "Hardware & Devices",
        "description": "Manages Bluetooth devices. Safe to stop if you do not use Bluetooth.",
        "exec": "svchost.exe",
        "recommended_stop": False
    },
    "TouchKeyboard": {
        "display_name": "Touch Keyboard and Handwriting Panel Service (TabletInputService)",
        "category": "Hardware & Devices",
        "description": "Touch screen input support. Safe to stop on desktop PCs.",
        "exec": "svchost.exe",
        "recommended_stop": False
    },
    "WSearch": {
        "display_name": "Windows Search",
        "category": "Index & Performance",
        "description": "Provides content indexing and search results. High disk/CPU usage.",
        "exec": "SearchIndexer.exe",
        "recommended_stop": False
    },
    "SysMain": {
        "display_name": "SysMain (SuperFetch)",
        "category": "Index & Performance",
        "description": "Maintains and improves system performance over time. High RAM/Disk usage on SSDs.",
        "exec": "svchost.exe",
        "recommended_stop": False
    }
}

# Known non-essential background updater / helper processes
BACKGROUND_BLOAT_PROCESSES = {
    "googleupdate.exe": "Google Software Update Service",
    "edgeupdate.exe": "Microsoft Edge Update Daemon",
    "edgeupdatem.exe": "Microsoft Edge Update Medium Service",
    "adobeuptest.exe": "Adobe Update Testing Utility",
    "adobeupdateservice.exe": "Adobe Acrobat / Creative Cloud Updater",
    "armsvc.exe": "Adobe Acrobat Update Service",
    "onedrivestandaloneupdater.exe": "Microsoft OneDrive Standalone Updater",
    "jusched.exe": "Java Update Scheduler",
    "ccleaner64.exe": "CCleaner Background Monitor",
    "epicgameslauncher.exe": "Epic Games Background Helper",
    "steamwebhelper.exe": "Steam Web Helper Background Process",
    "discord.exe": "Discord Background Daemon",
    "spotify.exe": "Spotify Web / Update Daemon",
}


class WindowsBaselineAnalyzer:
    """
    Analyzes system files, running processes, and services against the Windows
    native baseline. Categorizes processes into Core System, Optional System,
    and Third-Party Bloatware. Provides signature verification and preset actions.
    """

    def __init__(self, logger=None):
        self.logger = logger

    def _log(self, action: str, detail: str = "", level: str = "INFO"):
        if self.logger:
            self.logger.log(action, detail, level)

    def scan_processes(self):
        """
        Scan all running processes, compare with the baseline, verify execution path
        and signature status. Returns categorized dictionary.
        """
        results = {
            "core_system": [],
            "optional_system": [],
            "third_party_apps": [],
            "background_bloat": [],
            "suspicious_impostors": []
        }

        win_dir = os.environ.get("WINDIR", "C:\\Windows").lower()
        sys32_dir = os.path.join(win_dir, "system32")

        for proc in psutil.process_iter(['pid', 'name', 'exe', 'cpu_percent', 'memory_info', 'username']):
            try:
                pinfo = proc.info
                pname = (pinfo['name'] or '').lower()
                pexe = (pinfo['exe'] or '').lower()
                pid = pinfo['pid']

                mem_mb = (pinfo['memory_info'].rss / (1024 * 1024)) if pinfo['memory_info'] else 0.0

                item = {
                    "pid": pid,
                    "name": pinfo['name'] or 'Unknown',
                    "exe": pinfo['exe'] or 'N/A',
                    "mem_mb": round(mem_mb, 1),
                    "cpu_percent": pinfo['cpu_percent'] or 0.0,
                    "user": pinfo['username'] or 'N/A',
                    "status": "Normal",
                    "category": "Unknown",
                    "description": ""
                }

                # 1. Check for Fake / Impostor System Processes (e.g. svchost running outside Windows/System32)
                if pname in CORE_WINDOWS_PROCESSES:
                    if pexe and not pexe.startswith(win_dir):
                        item["status"] = "SUSPICIOUS (Non-System Location)"
                        item["category"] = "Suspicious"
                        item["description"] = f"CRITICAL: System file running outside Windows folder ({pexe})"
                        results["suspicious_impostors"].append(item)
                        continue
                    else:
                        item["category"] = "Core System"
                        item["description"] = CORE_WINDOWS_PROCESSES[pname]
                        results["core_system"].append(item)
                        continue

                # 2. Background Bloat Processes
                if pname in BACKGROUND_BLOAT_PROCESSES:
                    item["category"] = "Background Bloat"
                    item["description"] = BACKGROUND_BLOAT_PROCESSES[pname]
                    results["background_bloat"].append(item)
                    continue

                # 3. Check if file is inside Windows directory
                if pexe and pexe.startswith(win_dir):
                    item["category"] = "Optional System"
                    item["description"] = "Windows System Utility / Driver Host"
                    results["optional_system"].append(item)
                else:
                    item["category"] = "Third-Party App"
                    item["description"] = "User installed / Third-party process"
                    results["third_party_apps"].append(item)

            except (psutil.NoSuchProcess, psutil.AccessDenied, psutil.ZombieProcess):
                continue

        return results

    def verify_authenticode(self, file_path: str) -> dict:
        """
        Verify the Authenticode digital signature of a Windows executable file.
        Returns signature status dict.
        """
        if not file_path or not os.path.exists(file_path):
            return {"status": "File Not Found", "signer": "N/A", "valid": False}

        # SECURITY: Pass the path through an isolated child-process environment
        # instead of interpolating an untrusted filename into PowerShell code.
        ps_script = (
            "$s = Get-AuthenticodeSignature -LiteralPath $env:WINCAREPRO_SIGNATURE_TARGET; "
            "[pscustomobject]@{ Status = $s.Status.ToString(); "
            "Subject = $s.SignerCertificate.Subject } | ConvertTo-Json -Compress"
        )
        try:
            p = subprocess.run(
                ["powershell", "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass",
                 "-Command", ps_script],
                capture_output=True, text=True, timeout=10, creationflags=CREATE_NO_WINDOW,
                env={**os.environ,
                     "SystemRoot": os.environ.get("SystemRoot", r"C:\Windows"),
                     "WINDIR": os.environ.get("WINDIR", r"C:\Windows"),
                     "PSModulePath": r"C:\Program Files\WindowsPowerShell\Modules;C:\Windows\System32\WindowsPowerShell\v1.0\Modules",
                     "WINCAREPRO_SIGNATURE_TARGET": file_path},
            )
            if p.returncode == 0 and p.stdout.strip():
                data = json.loads(p.stdout)
                status_str = str(data.get("Status", "Unknown"))
                subject = str(data.get("Subject", "Unknown")) or "Unknown"
                valid = status_str == "Valid" or "Valid" in status_str
                return {"status": status_str, "signer": subject, "valid": valid}
        except Exception:
            pass

        return {"status": "Unknown", "signer": "N/A", "valid": False}

    def apply_optimization_preset(self, preset_name: str, callback_out=None):
        """
        Apply 1-click optimization presets:
          - 'disable_printers': Stops and disables Print Spooler and Fax
          - 'game_mode': Disables Printer Spooler, Telemetry, Xbox, Updaters, and frees RAM
          - 'clean_background_bloat': Terminates background updater tasks
          - 'disable_telemetry': Completely disables Connected User Experiences and WAP push
        """
        def out(text):
            if callback_out:
                callback_out(text)
            self._log("Preset Apply", text)

        def configure_service(service: str, start_mode: str) -> bool:
            configured = subprocess.run(
                ["sc", "config", service, "start=", start_mode],
                capture_output=True, text=True, creationflags=CREATE_NO_WINDOW,
            )
            if configured.returncode != 0:
                out(f"   [-] {service} configuration failed: {(configured.stderr or configured.stdout).strip()}")
                return False
            subprocess.run(
                ["sc", "stop", service], capture_output=True, text=True,
                creationflags=CREATE_NO_WINDOW,
            )
            return True

        out(f"=== Applying Optimization Preset: [{preset_name.upper()}] ===")
        actions_taken = 0

        if preset_name in ("disable_printers", "game_mode"):
            out(">> Disabling Print Spooler & Printer Services...")
            for svc in ["Spooler", "PrintNotify", "Fax"]:
                try:
                    if configure_service(svc, "disabled"):
                        out(f"   [+] Disabled service: {svc}")
                        actions_taken += 1
                except Exception as e:
                    out(f"   [-] Error setting service {svc}: {e}")

        if preset_name in ("disable_telemetry", "game_mode"):
            out(">> Disabling Microsoft Telemetry & Tracking Services...")
            for svc in ["DiagTrack", "dmwappushservice"]:
                try:
                    if configure_service(svc, "disabled"):
                        out(f"   [+] Disabled telemetry service: {svc}")
                        actions_taken += 1
                except Exception as e:
                    out(f"   [-] Error setting service {svc}: {e}")

        if preset_name == "game_mode":
            out(">> Disabling Non-Essential Xbox Services...")
            for svc in ["XblAuthManager", "XblGameSave", "XboxNetApiSvc"]:
                try:
                    if configure_service(svc, "demand"):
                        out(f"   [+] Set Xbox service to Manual/Demand: {svc}")
                        actions_taken += 1
                except Exception as e:
                    out(f"   [-] Error setting Xbox service {svc}: {e}")

        if preset_name in ("clean_background_bloat", "game_mode"):
            out(">> Cleaning up background bloat processes & updaters...")
            for proc in psutil.process_iter(['pid', 'name']):
                try:
                    pname = (proc.info['name'] or '').lower()
                    if pname in BACKGROUND_BLOAT_PROCESSES:
                        pid = proc.info['pid']
                        proc.kill()
                        out(f"   [+] Terminated Background Bloat: {pname} (PID {pid})")
                        actions_taken += 1
                except (psutil.NoSuchProcess, psutil.AccessDenied):
                    continue

        out(f"=== Preset [{preset_name.upper()}] completed. {actions_taken} actions taken. ===")
        return actions_taken
