param(
    [switch]$SkipTests
)
$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

$venvPython = Join-Path $PSScriptRoot ".venv\Scripts\python.exe"
if (-not (Test-Path -LiteralPath $venvPython)) {
    Write-Host "[*] Creating virtual environment..."
    python -m venv (Join-Path $PSScriptRoot ".venv")
}

Write-Host "[*] Installing pinned dependencies..."
& $venvPython -m pip install --upgrade pip --quiet
& $venvPython -m pip install -r (Join-Path $PSScriptRoot "requirements.txt")
if ($LASTEXITCODE -ne 0) { throw "Dependency install failed." }

if (-not $SkipTests) {
    Write-Host "[*] Running test suite..."
    & $venvPython -m pytest -q
    if ($LASTEXITCODE -ne 0) { throw "Tests failed; aborting build." }
}

Write-Host "[*] Building WinCarePro.exe via PyInstaller..."
& $venvPython -m PyInstaller --clean --noconfirm WinCarePro.spec
if ($LASTEXITCODE -ne 0) { throw "PyInstaller build failed." }

$exePath = Join-Path $PSScriptRoot "dist\WinCarePro.exe"
if (-not (Test-Path -LiteralPath $exePath)) { throw "Build did not produce $exePath." }

# uac_admin=True in WinCarePro.spec means this launch triggers a UAC prompt
# unless this script itself is already running elevated. Poll by process name
# rather than the Start-Process -PassThru handle: for exes requiring
# elevation, the handle PowerShell returns doesn't always end up tracking the
# final elevated process.
Write-Host "[*] Smoke-launching the build..."
Start-Process -FilePath $exePath
try {
    $deadline = (Get-Date).AddSeconds(30)
    $match = $null
    do {
        Start-Sleep -Milliseconds 500
        $match = Get-Process -Name "WinCarePro" -ErrorAction SilentlyContinue |
            Where-Object { $_.MainWindowTitle -like "*WinCare Pro*" } |
            Select-Object -First 1
    } until ($match -or (Get-Date) -ge $deadline)
    if (-not $match) { throw "Smoke launch did not show the main window within 30s." }
    Write-Host "[OK] BUILD_SMOKE_PASS title=$($match.MainWindowTitle)"
} finally {
    Get-Process -Name "WinCarePro" -ErrorAction SilentlyContinue | Stop-Process -Force
}
