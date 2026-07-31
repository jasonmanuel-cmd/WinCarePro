@echo off
:: ============================================================================
:: WinCare Pro - Auto-Elevated Administrator Launcher
:: ============================================================================
title WinCare Pro Launcher

:: Check for Administrator privileges
net session >nul 2>&1
if %errorLevel% neq 0 (
    echo [WinCare Pro] Elevating to Administrator...
    powershell -NoProfile -ExecutionPolicy Bypass -Command "Start-Process '%~f0' -Verb RunAs"
    exit /b
)

cd /d "%~dp0"

echo ============================================================================
echo   WinCare Pro - Windows 11 Optimization & Maintenance Suite
echo ============================================================================
echo.

:: Check Python installation
python --version >nul 2>&1
if %errorlevel% neq 0 (
    echo [ERROR] Python 3.11+ is not installed or not in PATH!
    echo Please install Python 3.11+ from https://python.org and check "Add to PATH".
    echo.
    pause
    exit /b 1
)

:: Ensure dependencies are installed
echo [*] Checking runtime dependencies...
python -c "import customtkinter, psutil" >nul 2>&1
if %errorlevel% neq 0 (
    echo [*] Installing required packages from requirements.txt...
    python -m pip install -r requirements.txt --quiet
)

echo [*] Launching WinCare Pro...
start "" pythonw main.py

exit /b 0
