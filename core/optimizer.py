"""
WinCare Pro - Core optimizer (startup, services, power, visuals, background apps).

Every mutation records prior state in ChangeBackup for the Undo Center.
"""
from __future__ import annotations

import os
import shutil
import time
from pathlib import Path

try:
    import psutil
except ImportError:
    psutil = None

try:
    import winreg
except ImportError:
    winreg = None

from core.shell import run_cmd, run_ps, human_bytes
from core.platform import APP_DIR
from core.logger import AppLogger, ChangeBackup


class Optimizer:
    """Every mutation records prior state in ChangeBackup for the Undo Center."""

    # Curated services that are safe *candidates* to disable, with honest
    # explanations of the trade-off. Nothing here is disabled automatically.
    OPTIONAL_SERVICES = [
        {"name": "DiagTrack", "display": "Connected User Experiences & Telemetry",
         "why": "Microsoft telemetry uploader. Disabling saves I/O; no user-facing loss."},
        {"name": "SysMain", "display": "SysMain (Superfetch)",
         "why": "Preloads apps into RAM. On SSD systems disabling rarely hurts and can calm disk usage."},
        {"name": "WSearch", "display": "Windows Search Indexer",
         "why": "Powers instant search. Disable ONLY if you never use Start-menu file search - searches become slow."},
        {"name": "Fax", "display": "Fax",
         "why": "Legacy fax service. Safe to disable unless you fax from this PC."},
        {"name": "RemoteRegistry", "display": "Remote Registry",
         "why": "Lets remote users edit this registry. Disabling improves security; rarely needed."},
        {"name": "dmwappushservice", "display": "Device Management WAP Push",
         "why": "WAP push message routing (telemetry-adjacent). Safe to disable on desktops."},
        {"name": "MapsBroker", "display": "Downloaded Maps Manager",
         "why": "Updates offline maps. Safe to disable if you don't use the Maps app."},
        {"name": "WerSvc", "display": "Windows Error Reporting",
         "why": "Sends crash reports to Microsoft. Disabling loses automatic crash-fix lookups."},
    ]

    # Startup names commonly measured as high boot-impact.
    HIGH_IMPACT_HINTS = ("onedrive", "teams", "steam", "discord", "spotify",
                         "adobe", "ccxprocess", "epicgames", "skype", "cortana",
                         "icloud", "itunes", "utorrent", "wallpaper")

    STARTUP_APPROVED_RUN = r"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run"
    STARTUP_APPROVED_FOLDER = r"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\StartupFolder"
    RUN_KEY = r"Software\Microsoft\Windows\CurrentVersion\Run"
    DISABLED_FOLDER = APP_DIR / "disabled_startup"

    def __init__(self, logger: AppLogger, backup: ChangeBackup):
        self.log = logger
        self.backup = backup
        self.DISABLED_FOLDER.mkdir(exist_ok=True)

    # ---- startup: enumeration --------------------------------------------
    @staticmethod
    def extract_exe_path(command: str):
        """
        Pull the executable path out of a registry Run command line.
        Env vars are expanded first (%windir%\\...\\SecurityHealthSystray.exe
        must not be misread as an orphaned entry).
        """
        if not command:
            return None
        command = os.path.expandvars(command.strip())
        if command.startswith('"'):
            end = command.find('"', 1)
            return command[1:end] if end > 0 else None
        cut = command.lower().find(".exe")
        return command[:cut + 4] if cut > 0 else command.split(" ")[0]

    @staticmethod
    def _read_approved_state(hive, name):
        """True/False from StartupApproved binary; None if no entry (=enabled)."""
        if not winreg:
            return None
        try:
            key = winreg.OpenKey(hive, Optimizer.STARTUP_APPROVED_RUN)
            raw, _ = winreg.QueryValueEx(key, name)
            winreg.CloseKey(key)
            if isinstance(raw, bytes) and raw:
                return raw[0] % 2 == 0        # even first byte = enabled
        except OSError:
            return None
        return None

    @staticmethod
    def _startup_folders():
        user = Path(os.environ.get("APPDATA", "")) / \
            "Microsoft/Windows/Start Menu/Programs/Startup"
        common = Path(os.environ.get("PROGRAMDATA", "")) / \
            "Microsoft/Windows/Start Menu/Programs/Startup"
        return [("Folder (user)", user), ("Folder (all users)", common)]

    @staticmethod
    def list_startup_items():
        """
        Enumerate startup programs from HKCU/HKLM Run keys + Startup folders,
        including items WinCare previously disabled (so they can be re-enabled).
        Returns list of dicts: name, command, source, enabled, impact.
        """
        items = []
        if winreg:
            reg_sources = [
                ("HKCU", winreg.HKEY_CURRENT_USER, Optimizer.RUN_KEY),
                ("HKLM", winreg.HKEY_LOCAL_MACHINE, Optimizer.RUN_KEY),
                ("HKLM32", winreg.HKEY_LOCAL_MACHINE,
                 r"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run"),
            ]
            for label, hive, subkey in reg_sources:
                try:
                    key = winreg.OpenKey(hive, subkey)
                except OSError:
                    continue
                i = 0
                while True:
                    try:
                        name, cmd, _ = winreg.EnumValue(key, i); i += 1
                    except OSError:
                        break
                    state = Optimizer._read_approved_state(hive, name)
                    enabled = True if state is None else state
                    items.append(Optimizer._mk_item(name, str(cmd), label, enabled))
                winreg.CloseKey(key)
        for label, folder in Optimizer._startup_folders():
            if folder.exists():
                for f in folder.iterdir():
                    if f.suffix.lower() in (".lnk", ".exe", ".bat", ".cmd", ".url"):
                        items.append(Optimizer._mk_item(f.stem, str(f), label, True))
        # items we parked in disabled_startup (still restorable)
        if Optimizer.DISABLED_FOLDER.exists():
            for f in Optimizer.DISABLED_FOLDER.iterdir():
                items.append(Optimizer._mk_item(f.stem, str(f),
                                                "Folder (disabled by WinCare)", False))
        return items

    @staticmethod
    def _mk_item(name, command, source, enabled):
        impact = "High" if any(h in (name + command).lower()
                               for h in Optimizer.HIGH_IMPACT_HINTS) else "Normal"
        exe = Optimizer.extract_exe_path(command)
        if exe and not Path(exe).exists() and "Folder" not in source:
            impact = "Broken"
        return {"name": name, "command": command, "source": source,
                "enabled": enabled, "impact": impact}

    # ---- startup: toggle ----------------------------------------------------
    def set_startup_enabled(self, item, enable: bool):
        """
        Enable/disable a startup entry the same way Task Manager does
        (StartupApproved binary flag) - the Run entry itself is untouched,
        which makes this 100% reversible. Folder items are moved to/from a
        parking folder inside APP_DIR. Returns (ok, message).
        """
        try:
            src = item["source"]
            if src.startswith("Folder"):
                return self._toggle_folder_item(item, enable)
            hive = winreg.HKEY_CURRENT_USER if src == "HKCU" else winreg.HKEY_LOCAL_MACHINE
            self.backup.remember("startup", f"{src}\\{item['name']}",
                                 {"source": src, "command": item["command"],
                                  "was_enabled": item["enabled"]})
            key = winreg.CreateKeyEx(hive, self.STARTUP_APPROVED_RUN, 0,
                                     winreg.KEY_SET_VALUE)
            if enable:
                blob = bytes([0x02]) + b"\x00" * 11
            else:
                # 0x03 + 8-byte FILETIME timestamp, matching Task Manager format
                filetime = int(time.time() * 10_000_000) + 116_444_736_000_000_000
                blob = bytes([0x03]) + b"\x00" * 3 + filetime.to_bytes(8, "little")
            winreg.SetValueEx(key, item["name"], 0, winreg.REG_BINARY, blob)
            winreg.CloseKey(key)
            self.log.log("Startup " + ("enabled" if enable else "disabled"),
                         f"{item['name']} ({src})")
            return True, f"{item['name']} {'enabled' if enable else 'disabled'}."
        except PermissionError:
            return False, "Access denied - HKLM entries require Administrator."
        except Exception as e:
            return False, f"Failed: {e}"

    def _toggle_folder_item(self, item, enable: bool):
        path = Path(item["command"])
        try:
            if enable:
                # restore from parking folder to the user Startup folder
                dest = self._startup_folders()[0][1] / path.name
                dest.parent.mkdir(parents=True, exist_ok=True)
                shutil.move(str(path), str(dest))
            else:
                self.backup.remember("startup", f"Folder\\{item['name']}",
                                     {"source": item["source"],
                                      "command": item["command"], "was_enabled": True})
                shutil.move(str(path), str(self.DISABLED_FOLDER / path.name))
            self.log.log("Startup folder item "
                         + ("restored" if enable else "parked"), item["name"])
            return True, f"{item['name']} {'enabled' if enable else 'disabled'}."
        except (OSError, shutil.Error) as e:
            return False, f"Move failed: {e}"

    # ---- services -----------------------------------------------------------
    @staticmethod
    def service_state(name):
        """Return (status, start_type) or (None, None) if missing."""
        try:
            s = psutil.win_service_get(name)
            return s.status(), s.start_type()
        except Exception:
            return None, None

    def set_service(self, name, disable: bool, out):
        """
        Stop+disable (or restore) a service via sc.exe. Original start type
        is recorded first so Undo Center can restore it. Returns (ok, msg).
        """
        status, start_type = self.service_state(name)
        if status is None:
            return False, f"Service '{name}' not found."
        if disable:
            self.backup.remember("services", name, {"start_type": start_type})
            out(f">> Stopping service {name} ...")
            run_cmd(["sc", "stop", name], timeout=60)
            rc, o = run_cmd(["sc", "config", name, "start=", "disabled"], timeout=30)
            ok = rc == 0
            self.log.log("Service disabled", name, "INFO" if ok else "ERROR")
            return ok, (f"{name} stopped & disabled." if ok
                        else f"Failed (admin required?): {o[:120]}")
        # restore path
        orig = (self.backup.recall("services", name) or {}).get("start_type", "manual")
        sc_map = {"automatic": "auto", "automatic (delayed start)": "delayed-auto",
                  "manual": "demand", "disabled": "demand"}
        mode = sc_map.get(str(orig).lower(), "demand")
        rc, o = run_cmd(["sc", "config", name, "start=", mode], timeout=30)
        run_cmd(["sc", "start", name], timeout=60)
        ok = rc == 0
        self.log.log("Service restored", f"{name} -> {mode}",
                      "INFO" if ok else "ERROR")
        return ok, (f"{name} restored to '{mode}' and started."
                    if ok else f"Failed: {o[:120]}")

    # ---- power plans ---------------------------------------------------------
    @staticmethod
    def list_power_plans():
        """[{guid, name, active}] parsed from 'powercfg /list'."""
        rc, out = run_cmd(["powercfg", "/list"], timeout=30)
        plans = []
        for ln in out.splitlines():
            if "GUID" in ln.upper() and ":" in ln:
                try:
                    rest = ln.split(":", 1)[1].strip()
                    guid = rest.split(" ")[0].strip()
                    name = rest[rest.find("(") + 1: rest.find(")")]
                    plans.append({"guid": guid, "name": name,
                                  "active": ln.rstrip().endswith("*")})
                except (IndexError, ValueError):
                    continue
        return plans

    def set_power_plan(self, guid, name):
        rc, out = run_cmd(["powercfg", "/setactive", guid], timeout=30)
        ok = rc == 0
        self.log.log("Power plan changed", name, "INFO" if ok else "ERROR")
        return ok, (f"Active plan: {name}" if ok else f"Failed: {out[:120]}")

    def enable_ultimate_performance(self):
        """Clone the hidden Ultimate Performance scheme if not present."""
        if any("ultimate" in p["name"].lower() for p in self.list_power_plans()):
            return True, "Ultimate Performance plan already exists."
        rc, out = run_cmd(["powercfg", "-duplicatescheme",
                           "e9a42b02-d5df-448d-aa66-1dbb1c69be0f"], timeout=30)
        ok = rc == 0
        self.log.log("Ultimate Performance plan added", "", "INFO" if ok else "ERROR")
        return ok, ("Ultimate Performance plan added - select it above."
                    if ok else f"Failed (admin required): {out[:120]}")

    # ---- visual effects (safe registry tweaks, all reversible) ----------------
    VFX_KEY = r"Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects"
    ADV_KEY = r"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced"
    DESKTOP_KEY = r"Control Panel\Desktop"
    BAM_KEY = r"Software\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications"

    def _set_hkcu(self, subkey, name, value, kind):
        key = winreg.CreateKeyEx(winreg.HKEY_CURRENT_USER, subkey, 0,
                                 winreg.KEY_SET_VALUE)
        winreg.SetValueEx(key, name, 0, kind, value)
        winreg.CloseKey(key)

    def apply_performance_visuals(self):
        """
        'Adjust for best performance' preset + faster menus. Font smoothing
        is left ON (turning it off makes text ugly for zero real gain).
        Takes full effect after sign-out/restart of Explorer.
        """
        try:
            self._set_hkcu(self.VFX_KEY, "VisualFXSetting", 2, winreg.REG_DWORD)
            self._set_hkcu(self.DESKTOP_KEY, "MenuShowDelay", "150", winreg.REG_SZ)
            self._set_hkcu(self.ADV_KEY, "TaskbarAnimations", 0, winreg.REG_DWORD)
            self.log.log("Visual effects set to performance preset")
            return True, ("Performance visuals applied. Sign out and back in "
                          "(or reboot) for full effect.")
        except OSError as e:
            return False, f"Registry write failed: {e}"

    def restore_default_visuals(self):
        try:
            self._set_hkcu(self.VFX_KEY, "VisualFXSetting", 0, winreg.REG_DWORD)
            self._set_hkcu(self.DESKTOP_KEY, "MenuShowDelay", "400", winreg.REG_SZ)
            self._set_hkcu(self.ADV_KEY, "TaskbarAnimations", 1, winreg.REG_DWORD)
            self.log.log("Visual effects restored to Windows defaults")
            return True, "Windows default visuals restored (sign out to apply)."
        except OSError as e:
            return False, f"Registry write failed: {e}"

    def set_background_apps(self, allow: bool):
        """Global Win11 background-apps policy for the current user."""
        try:
            self._set_hkcu(self.BAM_KEY, "GlobalUserDisabled",
                           0 if allow else 1, winreg.REG_DWORD)
            self.log.log("Background apps " + ("enabled" if allow else "disabled"))
            return True, ("Background apps allowed again." if allow else
                          "Background apps disabled globally (saves RAM/battery; "
                          "some apps won't refresh until opened).")
        except OSError as e:
            return False, f"Registry write failed: {e}"

    def memory_pagefile_advice(self):
        """Read-only analysis: standby memory & pagefile guidance strings."""
        vm = psutil.virtual_memory()
        sw = psutil.swap_memory()
        lines = [
            f"Physical RAM : {human_bytes(vm.total)}  (used {vm.percent}%)",
            f"Available    : {human_bytes(vm.available)}",
            f"Pagefile     : {human_bytes(sw.total)}  (used {sw.percent}%)",
            "",
        ]
        if vm.percent > 85:
            lines.append("* RAM pressure is HIGH. Close heavy apps or add RAM.")
        if sw.percent > 60:
            lines.append("* Pagefile usage is high -> Windows is compensating for "
                         "low RAM. A RAM upgrade helps more than any tweak.")
        if vm.total < 8 * 1024**3:
            lines.append("* Under 8 GB RAM: keep pagefile 'System managed' "
                         "(Settings > System > About > Advanced system settings).")
        else:
            lines.append("* Keep the pagefile SYSTEM MANAGED unless you have a "
                         "specific measured reason - manual sizes cause crashes.")
        lines.append("* 'RAM cleaner' apps are snake oil: Windows standby memory "
                     "is already instantly reclaimable. We don't purge it.")
        return "\n".join(lines)

    # ---- fast startup (power-transition reliability) ------------------------
    FAST_STARTUP_KEY = r"SYSTEM\CurrentControlSet\Control\Session Manager\Power"

    @staticmethod
    def fast_startup_enabled():
        """True/False, or None if unreadable. Missing value = Windows default (enabled)."""
        if not winreg:
            return None
        try:
            key = winreg.OpenKey(winreg.HKEY_LOCAL_MACHINE,
                                 Optimizer.FAST_STARTUP_KEY)
            val, _ = winreg.QueryValueEx(key, "HiberbootEnabled")
            winreg.CloseKey(key)
            return bool(val)
        except FileNotFoundError:
            return True
        except OSError:
            return None

    def set_fast_startup(self, enable: bool):
        """
        Toggle Windows Fast Startup (HiberbootEnabled). Disabling it is the
        standard mitigation for Kernel-Power 41 / HAL 12 'memory corrupted
        across power transition' faults. Fully reversible; original state is
        backed up for the Undo Center.
        """
        original = self.fast_startup_enabled()
        try:
            self.backup.remember("services", "FastStartup(HiberbootEnabled)",
                                 {"start_type": "enabled" if original else "disabled"})
            key = winreg.CreateKeyEx(winreg.HKEY_LOCAL_MACHINE,
                                     self.FAST_STARTUP_KEY, 0,
                                     winreg.KEY_SET_VALUE)
            winreg.SetValueEx(key, "HiberbootEnabled", 0, winreg.REG_DWORD,
                              1 if enable else 0)
            winreg.CloseKey(key)
            self.log.log("Fast Startup " + ("enabled" if enable else "disabled"))
            return True, ("Fast Startup enabled again."
                          if enable else
                          "Fast Startup disabled. Shutdowns are now full "
                          "shutdowns — boots take a few seconds longer, but "
                          "sleep/power-transition crashes usually stop.")
        except PermissionError:
            return False, "Access denied — requires Administrator."
        except OSError as e:
            return False, f"Registry write failed: {e}"

    def set_service_start_type(self, name: str, mode: str = "demand"):
        """
        Demote/promote ANY service's start type (used by Event Triage for
        chronic slow-starters like vendor licensing services). The service is
        NOT stopped — it just no longer blocks boot. Original type is backed
        up for the Undo Center. Returns (ok, msg).
        """
        status, start_type = self.service_state(name)
        if status is None:
            return False, f"Service '{name}' not found."
        self.backup.remember("services", name, {"start_type": start_type})
        rc, o = run_cmd(["sc", "config", name, "start=", mode], timeout=30)
        ok = rc == 0
        self.log.log("Service start type changed", f"{name} -> {mode}",
                     "INFO" if ok else "ERROR")
        return ok, (f"'{name}' start type set to '{mode}'. It no longer "
                    "delays boot; Windows starts it only when needed."
                    if ok else f"Failed (admin required?): {o[:120]}")
