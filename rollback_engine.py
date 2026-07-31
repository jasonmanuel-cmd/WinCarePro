#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""WinCare Pro restore-point and verified registry rollback ledger."""

from __future__ import annotations

import json
import os
import subprocess
from datetime import datetime
from pathlib import Path
from typing import Any

try:
    import winreg
except ImportError:  # pragma: no cover - exercised with injected test backend
    winreg = None

APP_DIR = Path(os.environ.get("LOCALAPPDATA", str(Path.home()))) / "WinCarePro"
BACKUP_FILE = APP_DIR / "change_backups.json"
CREATE_NO_WINDOW = 0x08000000 if os.name == "nt" else 0


class RollbackEngine:
    """Records typed registry state and only reports independently verified undo."""

    def __init__(self, backup_file: Path | None = None, registry_backend=None):
        self.backup_file = Path(backup_file or BACKUP_FILE)
        self.registry = registry_backend if registry_backend is not None else winreg
        self.backup_file.parent.mkdir(parents=True, exist_ok=True)
        self.ledger = self._load_ledger()

    def _load_ledger(self) -> list[dict[str, Any]]:
        if not self.backup_file.exists():
            return []
        try:
            data = json.loads(self.backup_file.read_text(encoding="utf-8"))
            return data if isinstance(data, list) else []
        except (OSError, json.JSONDecodeError):
            return []

    def _save_ledger(self) -> bool:
        """Atomically persist the ledger so a crash cannot truncate rollback data."""
        temp_file = self.backup_file.with_suffix(self.backup_file.suffix + ".tmp")
        try:
            temp_file.write_text(json.dumps(self.ledger, indent=2), encoding="utf-8")
            os.replace(temp_file, self.backup_file)
            return True
        except OSError:
            try:
                temp_file.unlink(missing_ok=True)
            except OSError:
                pass
            return False

    def create_system_restore_point(self, description="WinCare Pro Auto-Restore Point") -> tuple[bool, str]:
        """Create a native Windows System Restore Point using PowerShell."""
        try:
            safe_description = str(description).replace("'", "").replace('"', "")[:80]
            ps_cmd = (
                f"Checkpoint-Computer -Description '{safe_description}' "
                "-RestorePointType 'MODIFY_SETTINGS' -ErrorAction Stop"
            )
            process = subprocess.run(
                ["powershell", "-NoProfile", "-NonInteractive", "-Command", ps_cmd],
                capture_output=True, text=True, timeout=30, creationflags=CREATE_NO_WINDOW,
            )
            if process.returncode == 0:
                return True, "Successfully created System Restore Point."
            return False, f"System Restore failed: {process.stderr.strip() or 'Disabled in Windows Settings.'}"
        except Exception as exc:
            return False, f"Failed to create System Restore Point: {exc}"

    def record_change(
        self,
        category: str,
        item_name: str,
        key_path: str,
        old_value: Any,
        new_value: Any,
        value_type=None,
        value_existed: bool = True,
    ) -> bool:
        """Durably record original typed state before a registry mutation."""
        if value_type is None:
            if not self.registry:
                return False
            value_type = self.registry.REG_DWORD
        elif isinstance(value_type, str):
            if not self.registry or not hasattr(self.registry, value_type):
                return False
            value_type = getattr(self.registry, value_type)
        entry = {
            "timestamp": datetime.now().isoformat(timespec="seconds"),
            "category": category,
            "item_name": item_name,
            "key_path": key_path,
            "old_value": old_value,
            "new_value": new_value,
            "value_type": value_type,
            "value_existed": bool(value_existed),
        }
        self.ledger.append(entry)
        if self._save_ledger():
            return True
        self.ledger.pop()
        return False

    def _registry_target(self, key_path: str):
        if not self.registry or not key_path or "\\" not in key_path:
            raise ValueError("Invalid or unsupported registry target")
        hive_name, subkey = key_path.split("\\", 1)
        hives = {
            "HKCU": self.registry.HKEY_CURRENT_USER,
            "HKLM": self.registry.HKEY_LOCAL_MACHINE,
        }
        if hive_name not in hives:
            raise ValueError(f"Unsupported registry hive: {hive_name}")
        return hives[hive_name], subkey

    def _restore_entry(self, entry: dict[str, Any]) -> tuple[bool, str]:
        if "value_existed" not in entry:
            return False, "Legacy entry lacks original value-existence metadata."
        try:
            hive, subkey = self._registry_target(str(entry.get("key_path", "")))
            item_name = str(entry.get("item_name", ""))
            if not item_name:
                return False, "Rollback entry has no value name."
            if entry["value_existed"]:
                value_type = entry.get("value_type")
                with_key = self.registry.CreateKeyEx(hive, subkey, 0, self.registry.KEY_SET_VALUE)
                try:
                    self.registry.SetValueEx(with_key, item_name, 0, value_type, entry.get("old_value"))
                finally:
                    self.registry.CloseKey(with_key)
                query_key = self.registry.OpenKey(hive, subkey, 0, self.registry.KEY_QUERY_VALUE)
                try:
                    actual_value, actual_type = self.registry.QueryValueEx(query_key, item_name)
                finally:
                    self.registry.CloseKey(query_key)
                if actual_value != entry.get("old_value") or actual_type != value_type:
                    return False, "Read-back verification did not match the original typed value."
            else:
                with_key = self.registry.OpenKey(hive, subkey, 0, self.registry.KEY_SET_VALUE)
                try:
                    try:
                        self.registry.DeleteValue(with_key, item_name)
                    except FileNotFoundError:
                        pass
                finally:
                    self.registry.CloseKey(with_key)
                query_key = self.registry.OpenKey(hive, subkey, 0, self.registry.KEY_QUERY_VALUE)
                try:
                    try:
                        self.registry.QueryValueEx(query_key, item_name)
                        return False, "Read-back verification found a value that should be absent."
                    except FileNotFoundError:
                        pass
                finally:
                    self.registry.CloseKey(query_key)
            return True, "Verified rollback."
        except Exception as exc:
            return False, str(exc)

    def undo_all_changes(self, callback_out=None) -> tuple[bool, str]:
        """Restore each entry in reverse order; retain every failed record."""
        if not self.ledger:
            return True, "No recorded changes to roll back."
        if any(entry.get("rollback_status") == "in_progress" for entry in self.ledger):
            return False, (
                "Recovery required: a previous rollback was interrupted after changes were "
                "applied. No entries were replayed automatically."
            )

        # Persist an intent marker before changing Windows. If a later save fails,
        # the next launch refuses to replay these records over newer user changes.
        pending = [dict(entry, rollback_status="in_progress") for entry in self.ledger]
        self.ledger = pending
        if not self._save_ledger():
            return False, "Rollback was not started because its safety marker could not be saved."

        retained: list[dict[str, Any]] = []
        restored = 0
        failures: list[str] = []
        for entry in reversed(pending):
            ok, detail = self._restore_entry(entry)
            name = str(entry.get("item_name", "unknown"))
            if ok:
                restored += 1
                if callback_out:
                    callback_out(f"Verified rollback: {name}")
            else:
                failed_entry = dict(entry)
                failed_entry.pop("rollback_status", None)
                retained.append(failed_entry)
                failures.append(f"{name}: {detail}")
                if callback_out:
                    callback_out(f"Rollback failed (record retained): {name}: {detail}")

        completed_ledger = list(reversed(retained))
        self.ledger = completed_ledger
        if not self._save_ledger():
            # Keep the in-progress marker in memory as well as on disk. A later
            # call must require manual recovery instead of replaying stale state.
            self.ledger = pending
            return False, (
                "Rollback changes ran, but the completed rollback ledger could not be saved. "
                "Recovery required before any retry."
            )
        if failures:
            return False, f"{restored} restored; {len(failures)} failed and retained. " + "; ".join(failures)
        return True, f"Successfully verified rollback of {restored} setting(s)."


if __name__ == "__main__":
    engine = RollbackEngine()
    print("Rollback Ledger Entries:", len(engine.ledger))
