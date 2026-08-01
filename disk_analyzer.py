#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
================================================================================
 WinCare Pro - Visual Disk Treemap & Storage Analyzer Engine
================================================================================
 Analyzes drive space usage, identifies top space hogs, categories waste,
 and provides safe 1-click disk reclamation.
================================================================================
"""

import os
import shutil
import subprocess
from pathlib import Path

CREATE_NO_WINDOW = 0x08000000 if os.name == "nt" else 0


class DiskAnalyzer:
    """
    Disk Usage Scanner, Large File Hunter & Waste Cleanup Engine.
    """

    def __init__(self, target_drive="C:\\"):
        self.target_drive = target_drive

    @staticmethod
    def _allowed_waste_roots() -> list[str]:
        """Exact allowlist of directories the cleaner is permitted to purge."""
        windir = os.environ.get("WINDIR", "C:\\Windows")
        return [
            os.path.abspath(windir + "\\Temp").lower(),
            os.path.abspath(os.environ.get("TEMP", "")).lower() if os.environ.get("TEMP") else "",
            os.path.abspath(windir + "\\Prefetch").lower(),
            os.path.abspath(windir + "\\SoftwareDistribution\\Download").lower(),
        ]

    @staticmethod
    def _is_allowed_waste_root(path: str) -> bool:
        """True only if path sits strictly inside one of the allowed waste roots."""
        target = os.path.abspath(path).lower()
        return any(
            root and target.startswith(root + os.sep)
            for root in DiskAnalyzer._allowed_waste_roots()
        )

    def get_drive_overview(self) -> dict:
        """Return total, used, free drive space in GB and percentage."""
        try:
            total, used, free = shutil.disk_usage(self.target_drive)
            return {
                "drive": self.target_drive,
                "total_gb": round(total / (1024**3), 2),
                "used_gb": round(used / (1024**3), 2),
                "free_gb": round(free / (1024**3), 2),
                "used_pct": round((used / total) * 100, 1)
            }
        except Exception as e:
            return {"error": str(e)}

    def scan_large_folders(self, limit=10) -> list[dict]:
        """Scan top-level system directories for size distribution."""
        folders = []
        drive_path = Path(self.target_drive)
        if not drive_path.exists():
            return folders

        protected_folders = {"$Recycle.Bin", "System Volume Information"}

        for item in drive_path.iterdir():
            if item.name in protected_folders or not item.is_dir():
                continue
            try:
                # Calculate folder size using PowerShell for speed.
                # SECURITY: pass the path as an ARGUMENT via $args[0] and -LiteralPath
                # instead of interpolating it into a single-quoted script string.
                # This prevents PowerShell command injection (CWE-78): a folder name
                # containing a single-quote would otherwise break out of the -Path
                # argument and execute arbitrary commands. -LiteralPath is used so
                # wildcard/metacharacters in the path are treated literally.
                ps_cmd = "$p = $args[0]; (Get-ChildItem -LiteralPath $p -Recurse -ErrorAction SilentlyContinue | Measure-Object -Property Length -Sum).Sum"
                p = subprocess.run(["powershell", "-NoProfile", "-NonInteractive", "-Command", ps_cmd, str(item)], capture_output=True, text=True, timeout=5, creationflags=CREATE_NO_WINDOW)
                val = p.stdout.strip()
                size_bytes = int(val) if val.isdigit() else 0
                size_gb = round(size_bytes / (1024**3), 2)
                if size_gb > 0.1:
                    folders.append({"path": str(item), "name": item.name, "size_gb": size_gb})
            except Exception:
                pass

        folders.sort(key=lambda x: x["size_gb"], reverse=True)
        return folders[:limit]

    def get_top_largest_files(self, scan_path="C:\\Users", limit=20) -> list[dict]:
        """Scan user folders for top largest files (>100MB)."""
        large_files = []
        target = Path(scan_path)
        if not target.exists():
            return large_files

        try:
            for root, _, files in os.walk(target):
                for f in files:
                    fp = os.path.join(root, f)
                    try:
                        sz = os.path.getsize(fp)
                        sz_mb = sz / (1024**2)
                        if sz_mb >= 100:  # Files 100MB or larger
                            large_files.append({
                                "path": fp,
                                "name": f,
                                "size_mb": round(sz_mb, 1),
                                "size_gb": round(sz_mb / 1024, 2)
                            })
                    except Exception:
                        pass
                    if len(large_files) >= 500:
                        break
        except Exception:
            pass

        large_files.sort(key=lambda x: x["size_mb"], reverse=True)
        return large_files[:limit]

    def scan_reclaimable_waste(self) -> dict:
        """Scan system temp, Windows Update cache, and Recycle Bin waste."""
        waste_items = []
        total_waste_bytes = 0

        temp_dirs = [
            ("Windows Temp", os.environ.get("WINDIR", "C:\\Windows") + "\\Temp"),
            ("User Temp", os.environ.get("TEMP", "C:\\Users\\Default\\AppData\\Local\\Temp")),
            ("Prefetch Cache", os.environ.get("WINDIR", "C:\\Windows") + "\\Prefetch"),
            ("SoftwareDistribution (Update Cache)", os.environ.get("WINDIR", "C:\\Windows") + "\\SoftwareDistribution\\Download")
        ]

        for name, path in temp_dirs:
            p = Path(path)
            if p.exists():
                dir_sz = 0
                try:
                    for root, _, files in os.walk(p):
                        for f in files:
                            try:
                                dir_sz += os.path.getsize(os.path.join(root, f))
                            except Exception:
                                pass
                except Exception:
                    pass
                total_waste_bytes += dir_sz
                waste_items.append({
                    "name": name,
                    "path": path,
                    "size_mb": round(dir_sz / (1024**2), 1)
                })

        return {
            "waste_items": waste_items,
            "total_waste_mb": round(total_waste_bytes / (1024**2), 1),
            "total_waste_gb": round(total_waste_bytes / (1024**3), 2)
        }

    def clean_reclaimable_waste(self, callback_out=None) -> tuple[bool, str, float]:
        """Purge system temp files and update cache."""
        waste = self.scan_reclaimable_waste()
        freed_bytes = 0

        for item in waste["waste_items"]:
            p = Path(item["path"])
            if not p.exists():
                continue
            for root, dirs, files in os.walk(p):
                for f in files:
                    fp = os.path.join(root, f)
                    # SECURITY: refuse to delete anything that does not sit
                    # strictly inside the allowlisted temp/update roots.
                    if not self._is_allowed_waste_root(fp):
                        if callback_out:
                            callback_out(f"Safety refusal (outside allowed waste roots): {fp}")
                        continue
                    try:
                        sz = os.path.getsize(fp)
                        os.remove(fp)
                        freed_bytes += sz
                        if callback_out:
                            callback_out(f"Removed temp file: {f}")
                    except Exception:
                        pass

        freed_mb = round(freed_bytes / (1024**2), 1)
        msg = f"Successfully purged disk waste. Reclaimed {freed_mb} MB of space."
        return True, msg, freed_mb


if __name__ == "__main__":
    da = DiskAnalyzer()
    print("Drive Overview:", da.get_drive_overview())
    print("Reclaimable Waste:", da.scan_reclaimable_waste())
