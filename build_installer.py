#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
================================================================================
 WinCare Pro - Commercial Standalone Installer & PyInstaller Builder
================================================================================
 Compiles WinCare Pro into a single standalone Windows executable (WinCarePro.exe)
 with UAC administrator manifest and generates Inno Setup installer scripts.
================================================================================
"""

import os
import sys
import subprocess
from pathlib import Path

APP_DIR = Path(__file__).resolve().parent


def generate_pyinstaller_spec():
    """Generate PyInstaller spec file with UAC admin elevation manifest."""
    spec_content = f'''# -*- mode: python ; coding: utf-8 -*-

block_cipher = None

a = Analysis(
    ['main.py'],
    pathex=[],
    binaries=[],
    datas=[],
    hiddenimports=[
        'winreg',
        'ctypes',
        'psutil',
        'customtkinter',
        'licensing',
        'disk_analyzer',
        'deep_uninstaller',
        'file_cleaner',
        'rollback_engine',
        'wincare_tray',
        'win_baseline',
        'ai_engine',
        'privacy_engine',
        'bloat_remover',
        'performance_booster',
        'commerce',
        'updater'
    ],
    hookspath=[],
    hooksconfig={{}},
    runtime_hooks=[],
    excludes=[],
    win_no_prefer_redirects=False,
    win_private_assemblies=False,
    cipher=block_cipher,
    noarchive=False,
)

pyz = PYZ(a.pure, a.zipped_data, cipher=block_cipher)

exe = EXE(
    pyz,
    a.scripts,
    a.binaries,
    a.zipfiles,
    a.datas,
    [],
    name='WinCarePro',
    debug=False,
    bootloader_ignore_signals=False,
    strip=False,
    upx=True,
    upx_exclude=[],
    runtime_tmpdir=None,
    console=False,
    disable_windowed_traceback=False,
    target_arch=None,
    codesign_identity=None,
    entitlements_file=None,
    uac_admin=True,  # Request UAC Administrator Elevation on launch
)
'''
    spec_path = APP_DIR / "WinCarePro.spec"
    with open(spec_path, "w", encoding="utf-8") as f:
        f.write(spec_content)
    print(f"[✓] Generated PyInstaller spec file: {spec_path}")
    return spec_path


def build_executable():
    """Invoke PyInstaller to compile WinCarePro.exe."""
    spec_file = generate_pyinstaller_spec()
    cmd = [sys.executable, "-m", "PyInstaller", "--clean", str(spec_file)]
    print("[*] Compiling WinCarePro.exe via PyInstaller...")
    p = subprocess.run(cmd, cwd=str(APP_DIR), capture_output=True, text=True)
    if p.returncode == 0:
        print(f"[✓] Build Successful! Output located at {APP_DIR / 'dist' / 'WinCarePro.exe'}")
        return True
    else:
        print(f"[X] Build failed with error:\n{p.stderr[:500]}")
        return False


if __name__ == "__main__":
    generate_pyinstaller_spec()
