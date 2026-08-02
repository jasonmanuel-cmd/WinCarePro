"""
WinCare Pro - Core system information helpers.
"""
from __future__ import annotations

import os
import platform
import sys
import time

try:
    import psutil
except ImportError:
    psutil = None

try:
    import winreg
except ImportError:
    winreg = None

from core.platform import IS_WINDOWS
from core.shell import human_bytes


class SysInfo:
    """Static, read-only system inspection helpers."""

    @staticmethod
    def windows_edition() -> str:
        """'Windows 11 Pro 23H2 (build 22631)' from the registry."""
        if not (IS_WINDOWS and winreg):
            return platform.platform()
        try:
            key = winreg.OpenKey(winreg.HKEY_LOCAL_MACHINE,
                                 r"SOFTWARE\Microsoft\Windows NT\CurrentVersion")
            product = winreg.QueryValueEx(key, "ProductName")[0]
            try:
                disp = winreg.QueryValueEx(key, "DisplayVersion")[0]
            except OSError:
                disp = ""
            build = winreg.QueryValueEx(key, "CurrentBuild")[0]
            winreg.CloseKey(key)
            # Registry still says "Windows 10" on Win11 - fix by build number.
            if int(build) >= 22000 and "Windows 10" in product:
                product = product.replace("Windows 10", "Windows 11")
            return f"{product} {disp} (build {build})".strip()
        except OSError:
            return platform.platform()

    @staticmethod
    def build_number() -> int:
        try:
            return sys.getwindowsversion().build if IS_WINDOWS else 0
        except Exception:
            return 0

    @staticmethod
    def cpu_name() -> str:
        if IS_WINDOWS and winreg:
            try:
                key = winreg.OpenKey(winreg.HKEY_LOCAL_MACHINE,
                                     r"HARDWARE\DESCRIPTION\System\CentralProcessor\0")
                name = winreg.QueryValueEx(key, "ProcessorNameString")[0].strip()
                winreg.CloseKey(key)
                return name
            except OSError:
                pass
        return platform.processor() or "Unknown CPU"

    @staticmethod
    def uptime_str() -> str:
        try:
            b_time = psutil.boot_time() if psutil else 0
            if b_time > 0:
                secs = int(time.time() - b_time)
                if secs < 0:
                    secs = 0
            else:
                secs = 0
        except Exception:
            secs = 0
        d, rem = divmod(secs, 86400)
        h, rem = divmod(rem, 3600)
        m = rem // 60
        return f"{d}d {h}h {m}m" if d else f"{h}h {m}m"

    @staticmethod
    def summary() -> dict:
        """One-shot dict used by the Dashboard info panel and reports."""
        if not psutil:
            return {
                "os": SysInfo.windows_edition(),
                "hostname": platform.node(),
                "cpu": SysInfo.cpu_name(),
                "cores": "? cores / ? threads",
                "ram_total": "-",
                "ram_used_pct": 0,
                "disk_total": "-",
                "disk_free": "-",
                "disk_free_pct": 0,
                "uptime": SysInfo.uptime_str(),
                "boot_time": "Unknown",
            }
        vm = psutil.virtual_memory()
        try:
            du = psutil.disk_usage(os.environ.get("SystemDrive", "C:") + "\\")
        except OSError:
            du = None
        boot_time_str = "Unknown"
        try:
            b_time = psutil.boot_time()
            if b_time > 0:
                from datetime import datetime as dt
                boot_time_str = dt.fromtimestamp(b_time).strftime("%Y-%m-%d %H:%M")
        except Exception:
            pass
        return {
            "os": SysInfo.windows_edition(),
            "hostname": platform.node(),
            "cpu": SysInfo.cpu_name(),
            "cores": f"{psutil.cpu_count(logical=False) or '?'} cores / "
                     f"{psutil.cpu_count() or '?'} threads",
            "ram_total": human_bytes(vm.total),
            "ram_used_pct": vm.percent,
            "disk_total": human_bytes(du.total) if du else "-",
            "disk_free": human_bytes(du.free) if du else "-",
            "disk_free_pct": round(100 - du.percent, 1) if du else 0,
            "uptime": SysInfo.uptime_str(),
            "boot_time": boot_time_str,
        }