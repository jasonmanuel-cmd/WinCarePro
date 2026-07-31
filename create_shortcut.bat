@echo off
:: Create Desktop Shortcut for WinCare Pro
title Create WinCare Pro Desktop Shortcut
cd /d "%~dp0"

echo [*] Creating Desktop Shortcut for WinCare Pro...

powershell -NoProfile -ExecutionPolicy Bypass -Command "$ws = New-Object -ComObject WScript.Shell; $s = $ws.CreateShortcut([System.IO.Path]::Combine([Environment]::GetFolderPath('Desktop'), 'WinCare Pro.lnk')); $s.TargetPath = '%~dp0run.bat'; $s.WorkingDirectory = '%~dp0'; $s.Description = 'WinCare Pro - Windows 11 Optimization Suite'; $s.Save()"

if %errorlevel% equ 0 (
    echo [+] WinCare Pro Desktop Shortcut created successfully!
) else (
    echo [-] Failed to create Desktop Shortcut.
)

pause
