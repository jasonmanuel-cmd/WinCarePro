"""
WinCare Pro - Core storage analyzer (read-only).
"""
from __future__ import annotations

import os
from pathlib import Path


class StorageAnalyzer:
    """Find the largest folders and files under a root path."""

    @staticmethod
    def scan(root: str, cancel_event=None, progress_cb=None,
             top_n=25, max_depth=3):
        """
        Returns (folders, files):
          folders: [(size, path)] - direct children aggregated to max_depth
          files:   [(size, path)] - largest individual files anywhere below
        """
        root_path = Path(root)
        big_files = []
        folder_sizes = {}

        def walk(path: Path, depth: int) -> int:
            if cancel_event is not None and cancel_event.is_set():
                return 0
            total = 0
            try:
                with os.scandir(path) as it:
                    for e in it:
                        if cancel_event is not None and cancel_event.is_set():
                            return total
                        try:
                            if e.is_symlink():
                                continue
                            if e.is_file(follow_symlinks=False):
                                sz = e.stat(follow_symlinks=False).st_size
                                total += sz
                                if sz > 50 * 1024 * 1024:  # track files > 50 MB
                                    big_files.append((sz, e.path))
                            elif e.is_dir(follow_symlinks=False):
                                sub = walk(Path(e.path), depth + 1)
                                total += sub
                                if depth < max_depth:
                                    folder_sizes[e.path] = sub
                        except OSError:
                            continue
            except OSError:
                pass
            if progress_cb and depth == 1:
                progress_cb(str(path))
            return total

        walk(root_path, 0)
        folders = sorted(((s, p) for p, s in folder_sizes.items()),
                         reverse=True)[:top_n]
        files = sorted(big_files, reverse=True)[:top_n]
        return folders, files
