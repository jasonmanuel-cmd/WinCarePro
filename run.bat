@echo off
setlocal

set "ROOT_DIR=%~dp0"
set "REQ_FILE=%ROOT_DIR%requirements.txt"

where python >nul 2>nul
if errorlevel 1 (
  echo Python was not found on PATH.
  echo Install Python 3.11+ and try again.
  exit /b 1
)

python -c "import ctypes, sys; sys.exit(0 if ctypes.windll.shell32.IsUserAnAdmin() else 1)" >nul 2>nul
if errorlevel 1 (
  powershell -NoProfile -ExecutionPolicy Bypass -Command "Start-Process -FilePath '%ComSpec%' -Verb RunAs -ArgumentList '/c', '""%~f0""'"
  exit /b
)

python -m pip install --upgrade pip
python -m pip install -r "%REQ_FILE%"

python "%ROOT_DIR%main.py"

endlocal