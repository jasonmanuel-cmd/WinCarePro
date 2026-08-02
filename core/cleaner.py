"""
WinCare Pro - Core cleaner.

Every category can be size-analyzed (dry run) before anything is deleted.
"""
from __future__ import annotations

import ctypes
import os
import shutil
from pathlib import Path

try:
    import psutil
except ImportError:
    psutil = None

from core.shell import run_cmd, run_ps, human_bytes
from core.platform import is_admin
from core.logger import AppLogger, SettingsManager


class Cleaner:
    def __init__(self, logger: AppLogger, settings: SettingsManager):
        self.log = logger
        self.settings = settings

    # ---- category definitions ----------------------------------------------
    def categories(self):
        """
        Ordered list of cleanup categories. Each entry:
          key, label, needs_admin, paths() -> list[Path], note
        Recycle Bin & Update cache have dedicated handlers.
        """
        win = Path(os.environ.get("SystemRoot", r"C:\Windows"))
        local = Path(os.environ.get("LOCALAPPDATA", ""))
        cats = [
            {"key": "user_temp", "label": "User Temp files (%TEMP%)",
             "admin": False, "paths": [Path(os.environ.get("TEMP", ""))],
             "note": "Safe. Files in use are skipped automatically."},
            {"key": "win_temp", "label": "Windows Temp (C:\\Windows\\Temp)",
             "admin": True, "paths": [win / "Temp"],
             "note": "Safe. Requires Administrator."},
            {"key": "prefetch", "label": "Prefetch cache",
             "admin": True, "paths": [win / "Prefetch"],
             "note": "Rebuilt automatically; first boots after cleaning are slightly slower."},
            {"key": "thumbs", "label": "Thumbnail cache",
             "admin": False,
             "paths": [local / "Microsoft/Windows/Explorer"],
             "note": "Only thumbcache_*.db files are removed; Explorer rebuilds them.",
             "pattern": "thumbcache_*.db"},
            {"key": "wu_cache", "label": "Old Windows Update downloads",
             "admin": True, "paths": [win / "SoftwareDistribution/Download"],
             "note": "Update service is stopped during cleaning, then restarted."},
            {"key": "recycle", "label": "Recycle Bin",
             "admin": False, "paths": [],
             "note": "Empties the Recycle Bin for all drives. NOT recoverable."},
        ]
        if self.settings.get("clean_browser_cache"):
            cats.append({"key": "browser", "label": "Browser caches (Chrome/Edge)",
                         "admin": False, "paths": self._browser_cache_paths(),
                         "note": "Skipped automatically while the browser is running."})
        for i, p in enumerate(self.settings.get("custom_clean_paths", [])):
            cats.append({"key": f"custom_{i}", "label": f"Custom: {p}",
                         "admin": False, "paths": [Path(p)],
                         "note": "User-defined cleanup folder."})
        return cats

    @staticmethod
    def _browser_cache_paths():
        local = Path(os.environ.get("LOCALAPPDATA", ""))
        return [
            local / "Google/Chrome/User Data/Default/Cache",
            local / "Google/Chrome/User Data/Default/Code Cache",
            local / "Microsoft/Edge/User Data/Default/Cache",
            local / "Microsoft/Edge/User Data/Default/Code Cache",
        ]

    @staticmethod
    def _browser_running():
        names = {"chrome.exe", "msedge.exe"}
        for p in psutil.process_iter(["name"]):
            try:
                if (p.info["name"] or "").lower() in names:
                    return True
            except psutil.Error:
                continue
        return False

    # ---- size analysis (dry run) ---------------------------------------------
    @staticmethod
    def dir_size(path: Path, pattern=None) -> int:
        total = 0
        try:
            if pattern:
                for f in path.glob(pattern):
                    try:
                        total += f.stat().st_size
                    except OSError:
                        continue
                return total
            for root, dirs, files in os.walk(path, topdown=True, onerror=lambda e: None):
                for f in files:
                    try:
                        total += os.path.getsize(os.path.join(root, f))
                    except OSError:
                        continue
        except OSError:
            pass
        return total

    def analyze_category(self, cat) -> int:
        """Bytes reclaimable for a category (0 if unknown)."""
        if cat["key"] == "recycle":
            return self._recycle_bin_size()
        total = 0
        for p in cat["paths"]:
            if p and Path(p).exists():
                total += self.dir_size(Path(p), cat.get("pattern"))
        return total

    # ---- recycle bin via shell32 -----------------------------------------------
    @staticmethod
    def _recycle_bin_size() -> int:
        class SHQUERYRBINFO(ctypes.Structure):
            _fields_ = [("cbSize", ctypes.c_ulong),
                        ("i64Size", ctypes.c_longlong),
                        ("i64NumItems", ctypes.c_longlong)]
        try:
            info = SHQUERYRBINFO()
            info.cbSize = ctypes.sizeof(SHQUERYRBINFO)
            if ctypes.windll.shell32.SHQueryRecycleBinW(None, ctypes.byref(info)) == 0:
                return int(info.i64Size)
        except Exception:
            pass
        return 0

    def _empty_recycle_bin(self, out) -> int:
        size = self._recycle_bin_size()
        try:
            # 0x7 = no confirmation dialog + no progress UI + no sound
            rc = ctypes.windll.shell32.SHEmptyRecycleBinW(None, None, 0x7)
            ok = rc in (0, -2147418113)  # S_OK or already-empty variants
        except Exception:
            ok = False
        if not ok:  # PowerShell fallback
            rc2, _ = run_ps("Clear-RecycleBin -Force -ErrorAction SilentlyContinue")
            ok = rc2 == 0
        out(f"   Recycle Bin: {'emptied, ' + human_bytes(size) + ' freed' if ok else 'could not empty'}")
        self.log.log("Recycle Bin emptied", human_bytes(size))
        return size if ok else 0

    # ---- deletion core --------------------------------------------------------
    @staticmethod
    def _purge_dir(path: Path, out, pattern=None):
        """
        Delete contents of `path` (never the folder itself). Locked/in-use
        files are skipped silently - that is expected for temp folders.
        Returns bytes freed.
        """
        freed = 0
        if not path or not path.exists():
            return 0
        entries = path.glob(pattern) if pattern else path.iterdir()
        for entry in entries:
            try:
                if entry.is_symlink():
                    entry.unlink(missing_ok=True)
                elif entry.is_file():
                    sz = entry.stat().st_size
                    entry.unlink()
                    freed += sz
                elif entry.is_dir():
                    sz = Cleaner.dir_size(entry)
                    shutil.rmtree(entry, ignore_errors=False)
                    freed += sz
            except (OSError, shutil.Error):
                continue  # in use - skip, never force
        return freed

    def clean(self, selected_keys, out, progress_cb=None):
        """
        Execute cleanup for the selected category keys.
        Returns total bytes freed. All output goes to the live console.
        """
        cats = [c for c in self.categories() if c["key"] in selected_keys]
        total_freed = 0
        for i, cat in enumerate(cats):
            if progress_cb:
                progress_cb(cat["label"], i / max(1, len(cats)))
            out(f">> Cleaning: {cat['label']}")
            if cat["admin"] and not is_admin():
                out("   skipped - requires Administrator.")
                continue
            if cat["key"] == "recycle":
                total_freed += self._empty_recycle_bin(out)
                continue
            if cat["key"] == "browser" and self._browser_running():
                out("   skipped - close Chrome/Edge first (cache files are locked).")
                continue
            if cat["key"] == "wu_cache":
                out("   stopping Windows Update service ...")
                run_cmd(["net", "stop", "wuauserv"], timeout=90)
            freed = 0
            for p in cat["paths"]:
                freed += self._purge_dir(Path(p), out, cat.get("pattern"))
            if cat["key"] == "wu_cache":
                run_cmd(["net", "start", "wuauserv"], timeout=90)
                out("   Windows Update service restarted.")
            total_freed += freed
            out(f"   freed {human_bytes(freed)}")
            self.log.log("Cleanup", f"{cat['label']}: freed {human_bytes(freed)}")
        if progress_cb:
            progress_cb("Done", 1.0)
        out(f">> Cleanup complete. Total reclaimed: {human_bytes(total_freed)}")
        return total_freed
