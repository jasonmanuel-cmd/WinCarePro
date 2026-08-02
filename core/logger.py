"""
WinCare Pro - Core logging, settings, and change backup.
"""
from __future__ import annotations

import json
import threading
from datetime import datetime, timedelta
from pathlib import Path

from core.platform import get_appdata_local


LOG_DIR = get_appdata_local() / "WinCarePro" / "logs"
SETTINGS_FILE = get_appdata_local() / "WinCarePro" / "settings.json"
BACKUP_FILE = get_appdata_local() / "WinCarePro" / "change_backups.json"


# ============================================================================
# FOUNDATION: LOGGING
# ============================================================================
class AppLogger:
    """
    Dual-format action logger.
      * JSON lines  -> wincare_YYYYMMDD.jsonl   (machine readable)
      * Plain text  -> wincare_YYYYMMDD.log     (human readable)
    Old files beyond the retention window are purged at startup.
    """

    def __init__(self, retention_days: int = 30):
        LOG_DIR.mkdir(parents=True, exist_ok=True)
        self._lock = threading.Lock()
        self.retention_days = retention_days
        self.purge_old_logs()

    def _paths(self):
        stamp = datetime.now().strftime("%Y%m%d")
        return LOG_DIR / f"wincare_{stamp}.jsonl", LOG_DIR / f"wincare_{stamp}.log"

    def log(self, action: str, detail: str = "", level: str = "INFO"):
        """Append one entry to both log files. Never raises."""
        entry = {
            "ts": datetime.now().isoformat(timespec="seconds"),
            "level": level, "action": action, "detail": detail,
        }
        with self._lock:
            try:
                jpath, tpath = self._paths()
                with open(jpath, "a", encoding="utf-8") as f:
                    f.write(json.dumps(entry, ensure_ascii=False) + "\n")
                with open(tpath, "a", encoding="utf-8") as f:
                    f.write(f"[{entry['ts']}] {level:<8} {action}"
                            + (f" | {detail}" if detail else "") + "\n")
            except OSError:
                pass  # logging must never crash the app

    def purge_old_logs(self):
        """Delete log files older than the retention window."""
        cutoff = datetime.now() - timedelta(days=max(1, self.retention_days))
        try:
            for f in LOG_DIR.glob("wincare_*.*"):
                try:
                    if datetime.fromtimestamp(f.stat().st_mtime) < cutoff:
                        f.unlink()
                except OSError:
                    continue
        except OSError:
            pass

    def tail(self, max_lines: int = 400) -> str:
        """Return the tail of today's readable log (for the UI log viewer)."""
        _, tpath = self._paths()
        try:
            lines = tpath.read_text(encoding="utf-8", errors="ignore").splitlines()
            return "\n".join(lines[-max_lines:])
        except OSError:
            return "(no log entries today)"


# ============================================================================
# FOUNDATION: SETTINGS
# ============================================================================
DEFAULT_SETTINGS = {
    "accepted_disclaimer": False,
    "theme": "Dark",                    # Dark | Light | System
    "auto_restore_point": True,         # auto-create RP before repairs
    "log_retention_days": 30,
    "custom_clean_paths": [],           # extra user-approved cleanup folders
    "clean_browser_cache": False,       # opt-in browser cache cleaning
    "scan_interval_days": 7,            # scheduled scan reminder
    "last_scan": None,                  # iso timestamp of last full scan
}

# Folders we refuse to accept as "custom cleanup paths" - protecting users
# from wiping their own system even intentionally.
PROTECTED_ROOTS = [
    "c:\\windows", "c:\\program files", "c:\\program files (x86)",
    "c:\\programdata", "c:\\users",  # exact roots only (see validation)
]


class SettingsManager:
    """JSON-backed settings with safe defaults and atomic-ish writes."""

    def __init__(self):
        self.data = dict(DEFAULT_SETTINGS)
        self.load()

    def load(self):
        try:
            if SETTINGS_FILE.exists():
                on_disk = json.loads(SETTINGS_FILE.read_text(encoding="utf-8"))
                if isinstance(on_disk, dict):
                    self.data.update({k: v for k, v in on_disk.items()
                                      if k in DEFAULT_SETTINGS})
        except (OSError, json.JSONDecodeError):
            pass  # fall back to defaults on corruption

    def save(self):
        try:
            tmp = SETTINGS_FILE.with_suffix(".tmp")
            tmp.write_text(json.dumps(self.data, indent=2), encoding="utf-8")
            tmp.replace(SETTINGS_FILE)
        except OSError:
            pass

    def get(self, key, default=None):
        return self.data.get(key, DEFAULT_SETTINGS.get(key, default))

    def set(self, key, value):
        self.data[key] = value
        self.save()

    @staticmethod
    def validate_custom_path(p: str):
        """
        Return (ok, reason). Reject protected system locations so a custom
        cleanup path can never nuke Windows or whole profile trees.
        """
        try:
            path = Path(p).resolve()
        except (OSError, ValueError):
            return False, "Path cannot be resolved."
        if not path.exists() or not path.is_dir():
            return False, "Folder does not exist."
        low = str(path).lower().rstrip("\\")
        drive_root = len(low) <= 3          # e.g. "c:\"
        if drive_root:
            return False, "Refusing a drive root."
        for root in PROTECTED_ROOTS:
            if low == root:
                return False, f"Refusing protected folder: {path}"
        if low.startswith("c:\\windows") and "temp" not in low:
            return False, "Only Temp folders inside C:\\Windows are allowed."
        if low == str(Path.home()).lower():
            return False, "Refusing your entire user profile folder."
        return True, "OK"


# ============================================================================
# CHANGE BACKUP (rollback info for startup items / services)
# ============================================================================
class ChangeBackup:
    """
    Records the pre-change state of anything we modify (startup entries,
    service start types) so the user can restore it later.
    Structure: {"startup": {name: {...}}, "services": {name: {...}}}
    """

    def __init__(self):
        self.data = {"startup": {}, "services": {}}
        try:
            if BACKUP_FILE.exists():
                loaded = json.loads(BACKUP_FILE.read_text(encoding="utf-8"))
                if isinstance(loaded, dict):
                    self.data.update(loaded)
        except (OSError, json.JSONDecodeError):
            pass

    def save(self):
        try:
            BACKUP_FILE.write_text(json.dumps(self.data, indent=2), encoding="utf-8")
        except OSError:
            pass

    def remember(self, category: str, key: str, state: dict):
        """Store original state only once (first change wins = true original)."""
        self.data.setdefault(category, {})
        if key not in self.data[category]:
            self.data[category][key] = state
            self.save()

    def recall(self, category: str, key: str):
        return self.data.get(category, {}).get(key)