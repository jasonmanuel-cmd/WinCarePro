param(
    [string]$Version = "1.3.0",
    [string]$DownloadUrl = $env:WINCAREPRO_DOWNLOAD_URL,
    [switch]$SkipBuild
)
$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

if (-not $DownloadUrl) {
    throw "Set -DownloadUrl or `$env:WINCAREPRO_DOWNLOAD_URL to the hosted installer URL (used in the update manifest)."
}

if (-not $SkipBuild) {
    & (Join-Path $PSScriptRoot "build.ps1")
}

$venvPython = Join-Path $PSScriptRoot ".venv\Scripts\python.exe"
$exePath = Join-Path $PSScriptRoot "dist\WinCarePro.exe"
if (-not (Test-Path -LiteralPath $exePath)) { throw "$exePath not found. Run build.ps1 first." }

if (-not $env:WINCAREPRO_SIGN_CERT_THUMBPRINT) {
    Write-Host "[!] WINCAREPRO_SIGN_CERT_THUMBPRINT is not set - building an UNSIGNED release."
}

Write-Host "[*] Signing engine exe (if configured)..."
$scratchManifest = Join-Path $env:TEMP "wincarepro-engine-manifest.json"
& $venvPython release_tools.py $exePath --version $Version --url $DownloadUrl --manifest $scratchManifest
if ($LASTEXITCODE -ne 0) { throw "Engine exe signing/processing failed." }
Remove-Item -LiteralPath $scratchManifest -ErrorAction SilentlyContinue

$iscc = "C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
if (-not (Test-Path -LiteralPath $iscc)) { throw "Inno Setup not found at $iscc. Install it from https://jrsoftware.org/isinfo.php." }

Write-Host "[*] Compiling installer..."
& $iscc (Join-Path $PSScriptRoot "installer\WinCarePro.iss")
if ($LASTEXITCODE -ne 0) { throw "Inno Setup compile failed." }

$installerPath = Join-Path $PSScriptRoot "dist\WinCarePro-Setup-$Version.exe"
if (-not (Test-Path -LiteralPath $installerPath)) { throw "Installer not found at $installerPath." }

Write-Host "[*] Signing installer and writing update manifest..."
& $venvPython release_tools.py $installerPath --version $Version --url $DownloadUrl
if ($LASTEXITCODE -ne 0) { throw "Installer signing/manifest step failed." }

$signed = [bool]$env:WINCAREPRO_SIGN_CERT_THUMBPRINT
Write-Host "[OK] PACKAGE_COMPLETE installer=$installerPath manifest=$(Join-Path $PSScriptRoot 'dist\update.json') signed=$signed"
