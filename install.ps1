<#
.SYNOPSIS
    Installs WinCare Pro for the current user.

.DESCRIPTION
    Per-user install, so no elevation is required and nothing is written to
    Program Files. Copies the published exe into %LOCALAPPDATA%\Programs,
    creates a Start Menu shortcut, and optionally registers an auto-start entry.

    Use -Uninstall to remove all of it again.

.EXAMPLE
    .\install.ps1
    .\install.ps1 -AutoStart
    .\install.ps1 -Uninstall
#>
[CmdletBinding()]
param(
    [switch]$AutoStart,
    [switch]$Uninstall
)

$ErrorActionPreference = 'Stop'

$exeName = 'WinCare Pro.exe'
$installRoot = Join-Path $env:LOCALAPPDATA 'Programs\WinCarePro'
$installedExe = Join-Path $installRoot $exeName
$startMenu = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs'
$shortcut = Join-Path $startMenu 'WinCare Pro.lnk'
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$runValue = 'WinCarePro'

function Remove-Autostart {
    if (Get-ItemProperty -Path $runKey -Name $runValue -ErrorAction SilentlyContinue) {
        Remove-ItemProperty -Path $runKey -Name $runValue
        Write-Host 'Removed auto-start entry.'
    }
}

if ($Uninstall) {
    Get-Process -Name 'WinCare Pro' -ErrorAction SilentlyContinue | Stop-Process -Force

    if (Test-Path $installedExe) { Remove-Item $installRoot -Recurse -Force }
    if (Test-Path $shortcut)     { Remove-Item $shortcut -Force }
    Remove-Autostart

    Write-Host 'WinCare Pro uninstalled.' -ForegroundColor Green
    Write-Host "History left untouched at: $env:LOCALAPPDATA\WinCarePro\history.json"
    return
}

# Prefer the published single-file build; fall back to the Release output.
$source = Join-Path $PSScriptRoot 'publish\WinCare Pro.exe'
if (-not (Test-Path $source)) {
    $source = Join-Path $PSScriptRoot 'bin\Release\net8.0-windows\WinCare Pro.exe'
}
if (-not (Test-Path $source)) {
    throw "No build found. Run .\build-release.ps1 first."
}

Write-Host "Installing from: $source" -ForegroundColor Cyan
New-Item -ItemType Directory -Path $installRoot -Force | Out-Null

# A running instance holds a lock on the installed exe, and Copy-Item then fails
# with a bare "being used by another process" that does not say what to do about
# it. Close it first and say so.
$running = Get-Process -Name 'WinCare Pro' -ErrorAction SilentlyContinue
if ($running) {
    Write-Host "Closing running WinCare Pro (pid $($running.Id -join ', '))..." -ForegroundColor Yellow
    foreach ($proc in $running) { $proc.CloseMainWindow() | Out-Null }
    Start-Sleep -Seconds 4
    $still = Get-Process -Name 'WinCare Pro' -ErrorAction SilentlyContinue
    if ($still) {
        Write-Host "  It did not close on request; forcing." -ForegroundColor Yellow
        $still | Stop-Process -Force
        Start-Sleep -Seconds 2
    }
}

Copy-Item $source $installedExe -Force

$ws = New-Object -ComObject WScript.Shell
$link = $ws.CreateShortcut($shortcut)
$link.TargetPath = $installedExe
$link.WorkingDirectory = $installRoot
$link.Description = 'System health optimizer for Windows'
$link.IconLocation = "$installedExe,0"
$link.Save()

if ($AutoStart) {
    New-ItemProperty -Path $runKey -Name $runValue -Value "`"$installedExe`"" -PropertyType String -Force | Out-Null
    Write-Host 'Auto-start enabled.'
}
else {
    Remove-Autostart
}

Write-Host ''
Write-Host 'Installed.' -ForegroundColor Green
Write-Host "  exe:      $installedExe"
Write-Host "  shortcut: $shortcut"
Write-Host ''
Write-Host 'Launch it from the Start Menu, or run:'
Write-Host "  & `"$installedExe`""
Write-Host ''
Write-Host 'Re-run with -AutoStart to start with Windows, or -Uninstall to remove.'