param(
    [string]$Version = "1.3.0",
    [string]$DownloadUrl = $env:WINCAREPRO_DOWNLOAD_URL,
    [string]$CheckoutUrl = $env:WINCAREPRO_CHECKOUT_URL,
    [string]$ManifestUrl = $env:WINCAREPRO_UPDATE_MANIFEST_URL,
    [string]$SignerSubject = $env:WINCAREPRO_SIGNER_SUBJECT,
    [switch]$SkipBuild
)
$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

if (-not $DownloadUrl) {
    throw "Set -DownloadUrl or `$env:WINCAREPRO_DOWNLOAD_URL to the hosted installer URL (used in the update manifest)."
}
foreach ($setting in @{
    CheckoutUrl = $CheckoutUrl
    ManifestUrl = $ManifestUrl
    SignerSubject = $SignerSubject
}.GetEnumerator()) {
    if (-not $setting.Value) { throw "$($setting.Key) is required for a commercial package." }
}
foreach ($url in @($DownloadUrl, $CheckoutUrl, $ManifestUrl)) {
    $parsed = $null
    if (-not [Uri]::TryCreate($url, [UriKind]::Absolute, [ref]$parsed) -or $parsed.Scheme -ne "https" -or -not $parsed.Host) {
        throw "Commercial release URLs must be public HTTPS addresses: $url"
    }
}
if (-not $env:WINCAREPRO_SIGN_CERT_THUMBPRINT) {
    throw "Set WINCAREPRO_SIGN_CERT_THUMBPRINT; unsigned commercial packages are refused."
}

$releaseConfig = @{
    WINCAREPRO_CHECKOUT_URL = $CheckoutUrl
    WINCAREPRO_UPDATE_MANIFEST_URL = $ManifestUrl
    WINCAREPRO_SIGNER_SUBJECT = $SignerSubject
} | ConvertTo-Json
New-Item -ItemType Directory -Path (Join-Path $PSScriptRoot "build") -Force | Out-Null
Set-Content -LiteralPath (Join-Path $PSScriptRoot "build\release_config.json") -Value $releaseConfig -Encoding UTF8

if (-not $SkipBuild) {
    & (Join-Path $PSScriptRoot "build.ps1")
}

$venvPython = Join-Path $PSScriptRoot ".venv\Scripts\python.exe"
$exePath = Join-Path $PSScriptRoot "dist\WinCarePro.exe"
if (-not (Test-Path -LiteralPath $exePath)) { throw "$exePath not found. Run build.ps1 first." }

Write-Host "[*] Building bundled Guided Care bridge..."
& $venvPython -m PyInstaller --clean --noconfirm --onefile --name WinCarePro.GuidedCare guided_care_cli.py
if ($LASTEXITCODE -ne 0) { throw "Guided Care bridge build failed." }

$desktopOut = Join-Path $PSScriptRoot "dist\desktop"
Write-Host "[*] Publishing self-contained accessible desktop shell..."
dotnet publish .\WinCarePro.Desktop\WinCarePro.Desktop.csproj -c Release -r win-x64 --self-contained true -o $desktopOut
if ($LASTEXITCODE -ne 0) { throw "Accessible desktop publish failed." }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot "dist\WinCarePro.GuidedCare.exe") -Destination $desktopOut -Force
Copy-Item -LiteralPath $exePath -Destination $desktopOut -Force

Write-Host "[*] Signing product executables..."
$scratchManifest = Join-Path $env:TEMP "wincarepro-engine-manifest.json"
& $venvPython release_tools.py $exePath --version $Version --url $DownloadUrl --manifest $scratchManifest
if ($LASTEXITCODE -ne 0) { throw "Engine exe signing/processing failed." }
foreach ($artifact in @(
    (Join-Path $desktopOut "WinCarePro.Desktop.exe"),
    (Join-Path $desktopOut "WinCarePro.GuidedCare.exe"),
    (Join-Path $desktopOut "WinCarePro.exe")
)) {
    & $venvPython release_tools.py $artifact --version $Version --url $DownloadUrl --manifest $scratchManifest
    if ($LASTEXITCODE -ne 0) { throw "Signing failed for $artifact." }
}
Remove-Item -LiteralPath $scratchManifest -ErrorAction SilentlyContinue

$iscc = "C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
if (-not (Test-Path -LiteralPath $iscc)) { throw "Inno Setup not found at $iscc. Install it from https://jrsoftware.org/isinfo.php." }

Write-Host "[*] Compiling installer..."
& $iscc "/DMyAppVersion=$Version" (Join-Path $PSScriptRoot "installer\WinCarePro.iss")
if ($LASTEXITCODE -ne 0) { throw "Inno Setup compile failed." }

$installerPath = Join-Path $PSScriptRoot "dist\WinCarePro-Setup-$Version.exe"
if (-not (Test-Path -LiteralPath $installerPath)) { throw "Installer not found at $installerPath." }

Write-Host "[*] Signing installer and writing update manifest..."
& $venvPython release_tools.py $installerPath --version $Version --url $DownloadUrl
if ($LASTEXITCODE -ne 0) { throw "Installer signing/manifest step failed." }

Write-Host "[OK] PACKAGE_COMPLETE installer=$installerPath manifest=$(Join-Path $PSScriptRoot 'dist\update.json') signed=True"
