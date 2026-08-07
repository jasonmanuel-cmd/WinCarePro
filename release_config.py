"""Load non-secret release settings bundled beside the executable."""

from __future__ import annotations

import json
import os
from pathlib import Path
import sys


def _config_path() -> Path:
    root = Path(getattr(sys, "_MEIPASS", Path(__file__).resolve().parent))
    return root / "release_config.json"


def load_release_config() -> dict[str, str]:
    try:
        data = json.loads(_config_path().read_text(encoding="utf-8"))
        if isinstance(data, dict):
            return {str(key): str(value) for key, value in data.items() if value}
    except (OSError, ValueError, TypeError):
        pass
    return {}


def release_setting(name: str) -> str:
    return os.environ.get(name, "") or load_release_config().get(name, "")
