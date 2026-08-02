"""
WinCare Pro - Shared platform constants and path resolution.

Centralizes Windows system paths, environment variable fallbacks, and WinUI constants
to avoid duplication and hardcoded strings across modules.
"""
import ctypes
import os
from pathlib import Path


# -----------------------------------------------------------------------------
# Platform identity
# -----------------------------------------------------------------------------
IS_WINDOWS = (os.name == "nt")


def is_admin() -> bool:
    """True when the current process has administrator privileges."""
    if not IS_WINDOWS:
        return False
    try:
        return bool(ctypes.windll.shell32.IsUserAnAdmin())
    except Exception:
        return False


# -----------------------------------------------------------------------------
# App identity / shared constants
# -----------------------------------------------------------------------------
APP_NAME = "WinCare Pro"
APP_VERSION = "1.3.0"

SEVERITY_ORDER = {"Critical": 0, "Warning": 1, "Info": 2, "OK": 3}
SEV_COLORS = {"Critical": "#E5484D", "Warning": "#F5A524",
              "Info": "#4A9EFF", "OK": "#2ECC71"}


# -----------------------------------------------------------------------------
# Windows System Temp / Cache Paths
# -----------------------------------------------------------------------------
def get_system_temp_roots() -> list[tuple[str, str]]:
    """
    Return list of (label, path) for system temp/cache directories that are
    safe to scan/clean. Uses environment variables with safe fallbacks.
    """
    windir = os.environ.get("WINDIR", r"C:\Windows")
    return [
        ("Windows Temp", os.path.join(windir, "Temp")),
        ("User Temp", os.environ.get("TEMP", os.path.join(os.path.expanduser("~"), "AppData", "Local", "Temp"))),
        ("Prefetch Cache", os.path.join(windir, "Prefetch")),
        ("SoftwareDistribution (Update Cache)", os.path.join(windir, "SoftwareDistribution", "Download")),
    ]


# -----------------------------------------------------------------------------
# WinUI / Build Constants
# -----------------------------------------------------------------------------
# Target Windows SDK / WinUI version constants
WINUI_VERSION = "3"
WINDOWS_APP_SDK_VERSION = "1.6"
TARGET_WINDOWS_VERSION = "10.0.22621"  # Windows 11 22H2 baseline

# Application identity constants
APP_DISPLAY_NAME = "WinCare Pro"
APP_PUBLISHER = "WinCare Pro Team"
APP_PACKAGE_NAME = "WinCarePro"


# -----------------------------------------------------------------------------
# Path Helpers
# -----------------------------------------------------------------------------
def get_user_profile_root() -> Path:
    """Return expanded user profile root (handles redirected folders)."""
    return Path.home()


def get_appdata_roaming() -> Path:
    """Return %APPDATA% (roaming app data)."""
    return Path(os.environ.get("APPDATA", str(Path.home() / "AppData" / "Roaming")))


def get_appdata_local() -> Path:
    """Return %LOCALAPPDATA% (local app data)."""
    return Path(os.environ.get("LOCALAPPDATA", str(Path.home() / "AppData" / "Local")))


def get_temp_dir() -> Path:
    """Return %TEMP% directory."""
    return Path(os.environ.get("TEMP", str(get_appdata_local() / "Temp")))


def get_packages_dir() -> Path:
    """Return %LOCALAPPDATA%\\Packages (UWP app data)."""
    return get_appdata_local() / "Packages"


def get_programdata() -> Path:
    """Return %PROGRAMDATA% (all-users app data)."""
    return Path(os.environ.get("PROGRAMDATA", r"C:\ProgramData"))


def get_windir() -> Path:
    """Return %WINDIR% (Windows directory)."""
    return Path(os.environ.get("WINDIR", r"C:\Windows"))


APP_DIR = get_appdata_local() / "WinCarePro"
LOG_DIR = APP_DIR / "logs"
REPORT_DIR = APP_DIR / "reports"