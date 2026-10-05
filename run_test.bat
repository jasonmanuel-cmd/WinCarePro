@echo off
REM Launches the most recent Release build, building first if it is missing.
setlocal
cd /d "%~dp0"

set "EXE=bin\Release\net8.0-windows\WinCare Pro.exe"

if not exist "%EXE%" (
    echo Building Release...
    dotnet build WinCareDesktop.csproj -c Release --nologo -v quiet
    if errorlevel 1 (
        echo Build failed.
        pause
        exit /b 1
    )
)

start "" "%EXE%"
endlocal