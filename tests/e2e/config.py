"""E2E test configuration for WinCarePro.Desktop."""

import os
from pathlib import Path

# Project root (three levels up from this file)
ROOT = Path(__file__).resolve().parent.parent.parent

# Path to the WPF application executable
APP_PATH = str(ROOT / "WinCarePro.Desktop" / "bin" / "Release" / "net8.0-windows" / "WinCarePro.Desktop.exe")

# Fallback to Debug build if Release is not available
if not os.path.exists(APP_PATH):
    APP_PATH = str(ROOT / "WinCarePro.Desktop" / "bin" / "Debug" / "net8.0-windows" / "WinCarePro.Desktop.exe")

# Window title from WPF XAML.
APP_TITLE = "WinCare Pro Guided Care"

# Timeouts in seconds
LAUNCH_TIMEOUT = 30
ACTION_TIMEOUT = 10

# Directory for E2E artifacts (screenshots, reports)
ARTIFACT_DIR = os.environ.get("E2E_ARTIFACT_DIR", str(ROOT / "tests" / "e2e" / "artifacts"))
