#!/usr/bin/env python3
"""Old installer/archive and duplicate-file scanning for WinCare Pro."""

from __future__ import annotations

import hashlib
import os
import time
from pathlib import Path
from typing import Callable, Iterable

from core.platform import get_appdata_roaming, get_appdata_local, get_temp_dir, get_packages_dir, get_programdata


OLD_FILE_EXTENSIONS = {
    ".exe", ".msi", ".msix", ".appx", ".zip", ".7z", ".rar", ".iso",
}
REPARSE_POINT = 0x400


class FileCleaner:
    """Review-first file finder with validated permanent deletion."""

    @staticmethod
    def default_scan_paths() -> list[str]:
        """Resolve redirected Desktop and Downloads folders on Windows."""
        home = Path.home()
        fallbacks = {"Desktop": home / "Desktop", "Downloads": home / "Downloads"}
        if os.name != "nt":
            return [str(p) for p in fallbacks.values() if p.is_dir()]

        try:
            import winreg

            key_path = r"Software\Microsoft\Windows\CurrentVersion\Explorer\User Shell Folders"
            names = {"Desktop": "Desktop", "Downloads": "{374DE290-123F-4565-9164-39C4925E467B}"}
            with winreg.OpenKey(winreg.HKEY_CURRENT_USER, key_path) as key:
                for label, value_name in names.items():
                    try:
                        value, _ = winreg.QueryValueEx(key, value_name)
                        fallbacks[label] = Path(os.path.expandvars(value))
                    except OSError:
                        pass
        except OSError:
            pass
        return [str(p) for p in fallbacks.values() if p.is_dir()]

    @staticmethod
    def _is_reparse(path: Path) -> bool:
        try:
            return path.is_symlink() or bool(path.stat(follow_symlinks=False).st_file_attributes & REPARSE_POINT)
        except (AttributeError, OSError):
            return path.is_symlink()

    @staticmethod
    def _inside(path: Path, roots: Iterable[Path]) -> bool:
        resolved = path.resolve(strict=False)
        return any(resolved == root or root in resolved.parents for root in roots)

    def get_file_hash(self, filepath: str, chunk_size: int = 65536) -> str:
        hasher = hashlib.sha256()
        try:
            with open(filepath, "rb") as handle:
                for chunk in iter(lambda: handle.read(chunk_size), b""):
                    hasher.update(chunk)
            return hasher.hexdigest()
        except OSError:
            return ""

    def find_duplicates(
        self,
        scan_paths: list[str],
        min_size_mb: float = 1.0,
        callback_out: Callable[[str], None] | None = None,
    ) -> dict:
        """Compatibility API: group duplicate files by SHA-256."""
        records = self.scan_candidates(
            scan_paths, old_days=None, min_duplicate_bytes=int(min_size_mb * 1024 * 1024)
        )
        groups: dict[str, list[dict]] = {}
        for record in records:
            if record["duplicate_group"]:
                groups.setdefault(record["sha256"], []).append({
                    "path": record["path"],
                    "filename": Path(record["path"]).name,
                    "size_mb": round(record["size_bytes"] / (1024**2), 2),
                })
        if callback_out:
            callback_out(f"Found {len(groups)} duplicate group(s).")
        return groups

    def scan_candidates(
        self,
        scan_paths: list[str] | None = None,
        *,
        old_days: int | None = 180,
        min_duplicate_bytes: int = 1,
        exclude_paths: list[str] | None = None,
        cancel_event=None,
        progress_cb: Callable[[int, str], None] | None = None,
        now: float | None = None,
    ) -> list[dict]:
        """Return old installer/archive files plus byte-identical duplicates."""
        now = time.time() if now is None else now
        cutoff = None if old_days is None else now - old_days * 86400
        excluded = [Path(p).resolve(strict=False) for p in (exclude_paths or [])]
        files: list[dict] = []
        seen: set[Path] = set()

        for raw_root in scan_paths or self.default_scan_paths():
            root = Path(raw_root)
            if not root.is_dir() or self._is_reparse(root) or self._inside(root, excluded):
                continue
            for current, dirs, names in os.walk(root, topdown=True, followlinks=False):
                if cancel_event is not None and cancel_event.is_set():
                    return []
                current_path = Path(current)
                dirs[:] = [
                    name for name in dirs
                    if not self._is_reparse(current_path / name)
                    and not self._inside(current_path / name, excluded)
                ]
                for name in names:
                    if cancel_event is not None and cancel_event.is_set():
                        return []
                    path = current_path / name
                    try:
                        resolved = path.resolve(strict=True)
                        if resolved in seen or self._is_reparse(path) or self._inside(path, excluded):
                            continue
                        stat = path.stat()
                        if not path.is_file():
                            continue
                    except OSError:
                        continue
                    seen.add(resolved)
                    files.append({
                        "path": str(path),
                        "size_bytes": stat.st_size,
                        "mtime_ns": stat.st_mtime_ns,
                        "modified": stat.st_mtime,
                        "old": cutoff is not None
                        and stat.st_mtime <= cutoff
                        and path.suffix.lower() in OLD_FILE_EXTENSIONS,
                    })
                    if progress_cb and len(files) % 100 == 0:
                        progress_cb(len(files), str(path))

        size_groups: dict[int, list[dict]] = {}
        for record in files:
            if record["size_bytes"] >= min_duplicate_bytes:
                size_groups.setdefault(record["size_bytes"], []).append(record)

        duplicate_number = 0
        for same_size in size_groups.values():
            if len(same_size) < 2:
                continue
            hashes: dict[str, list[dict]] = {}
            for record in same_size:
                if cancel_event is not None and cancel_event.is_set():
                    return []
                digest = self.get_file_hash(record["path"])
                record["sha256"] = digest
                if digest:
                    hashes.setdefault(digest, []).append(record)
            for matches in hashes.values():
                if len(matches) < 2:
                    continue
                duplicate_number += 1
                for record in matches:
                    record["duplicate_group"] = duplicate_number

        candidates = []
        for record in files:
            record.setdefault("sha256", "")
            record.setdefault("duplicate_group", None)
            if not record["old"] and not record["duplicate_group"]:
                continue
            kinds = []
            if record["old"]:
                kinds.append("Old installer/archive")
            if record["duplicate_group"]:
                kinds.append("Duplicate")
            record["category"] = " + ".join(kinds)
            record["age_days"] = max(0, int((now - record["modified"]) / 86400))
            candidates.append(record)
        return sorted(candidates, key=lambda item: (-item["size_bytes"], item["path"].lower()))

    def delete_candidates(
        self,
        selected: list[dict],
        all_candidates: list[dict],
        callback_out: Callable[[str], None] | None = None,
    ) -> dict:
        """Permanently delete validated selections while preserving one duplicate."""
        known = {item["path"]: item for item in all_candidates}
        rejected = [
            {"path": str(item.get("path", "")), "ok": False,
             "error": "file was not returned by this scan"}
            for item in selected if known.get(item.get("path")) is not item
        ]
        selected = [item for item in selected if known.get(item.get("path")) is item]
        selected_paths = {item["path"] for item in selected}
        groups: dict[int, list[dict]] = {}
        for item in all_candidates:
            if item.get("duplicate_group"):
                groups.setdefault(item["duplicate_group"], []).append(item)

        protected: set[str] = set()
        for members in groups.values():
            deleting = [item for item in members if item["path"] in selected_paths]
            if not deleting:
                continue
            remaining_copy = any(
                item["path"] not in selected_paths
                and self.get_file_hash(item["path"]) == item.get("sha256")
                for item in members
            )
            if not remaining_copy:
                protected.add(sorted(
                    deleting, key=lambda item: item["path"].lower()
                )[0]["path"])

        results = rejected
        freed = 0
        for item in selected:
            path = Path(item["path"])
            if item["path"] in protected:
                results.append({"path": str(path), "ok": False, "error": "preserved last duplicate copy"})
                continue
            try:
                if self._is_reparse(path):
                    raise OSError("refusing symbolic link or reparse point")
                stat = path.stat()
                if stat.st_size != item["size_bytes"] or stat.st_mtime_ns != item["mtime_ns"]:
                    raise OSError("file changed after scan")
                path.unlink()
                freed += stat.st_size
                results.append({"path": str(path), "ok": True, "error": ""})
                if callback_out:
                    callback_out(f"Permanently deleted: {path}")
            except OSError as exc:
                results.append({"path": str(path), "ok": False, "error": str(exc)})
                if callback_out:
                    callback_out(f"Could not delete {path}: {exc}")
        return {
            "results": results,
            "deleted_count": sum(item["ok"] for item in results),
            "failed_count": sum(not item["ok"] for item in results),
            "freed_bytes": freed,
        }

    def purge_duplicates(
        self, paths_to_delete: list[str], callback_out: Callable[[str], None] | None = None
    ) -> tuple[bool, str, float]:
        """Legacy permanent-delete API retained for compatibility."""
        freed = 0
        deleted = 0
        for raw_path in paths_to_delete:
            path = Path(raw_path)
            try:
                size = path.stat().st_size
                path.unlink()
                freed += size
                deleted += 1
                if callback_out:
                    callback_out(f"Deleted duplicate: {path.name}")
            except OSError:
                pass
        freed_mb = round(freed / (1024**2), 2)
        return True, f"Successfully purged {deleted} duplicate files. Reclaimed {freed_mb} MB.", freed_mb


if __name__ == "__main__":
    cleaner = FileCleaner()
    print(f"Found {len(cleaner.scan_candidates())} review candidate(s).")
