# -*- mode: python ; coding: utf-8 -*-

import os
import warnings

block_cipher = None
release_config = 'build/release_config.json'
datas = [('data/bloatware.json', 'data')]
if os.path.isfile(release_config):
    datas.append((release_config, '.'))

a = Analysis(
    ['main.py'],
    pathex=[],
    binaries=[],
    datas=datas,
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
        'performance_booster'
        ,'commerce'
        ,'updater'
        ,'release_config'
    ],
    hookspath=[],
    hooksconfig={},
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
    codesign_identity=os.environ.get("WINCAREPRO_CODESIGN_IDENTITY", None),
    entitlements_file=None,
    uac_admin=True,  # Request UAC Administrator Elevation on launch
    icon='assets/WinCarePro.ico',
)

if not os.environ.get("WINCAREPRO_CODESIGN_IDENTITY") and not os.environ.get("CI"):
    import warnings
    warnings.warn(
        "codesign_identity is None — the resulting build is NOT signed. "
        "For production releases set WINCAREPRO_CODESIGN_IDENTITY to your "
        "Authenticode certificate subject and re-run PyInstaller."
    )
