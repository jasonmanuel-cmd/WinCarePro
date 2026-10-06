<#
.SYNOPSIS
    Builds the WinCare Pro MSI installer.

.DESCRIPTION
    Runs the full release gate and then packages the result. The gate is the
    same one build-release.ps1 runs, deliberately: an installer built from a
    tree that does not pass its own tests is worse than no installer.

    Requires the WiX tool:
        dotnet tool install --global wix

.PARAMETER Configuration
    Release by default.

.PARAMETER SkipTests
    Skips the test gate. Only for a local rebuild where tests have just run.

.PARAMETER UpdateSourceUrl
    Endpoint that the shipped update-source.txt points at. It must serve JSON
    containing a "version" key. Defaults to the version manifest committed at
    packaging/version.json, served straight from this repository.
#>
[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [switch]$SkipTests,
    [string]$UpdateSourceUrl = 'https://raw.githubusercontent.com/jasonmanuel-cmd/WinCarePro/master/packaging/version.json'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$root = $PSScriptRoot
$project = Join-Path $root 'WinCareDesktop.csproj'
$publishDir = Join-Path $root 'publish'
$installerDir = Join-Path $root 'installer'
$msiPath = Join-Path $installerDir 'WinCare.msi'

# The wix dotnet tool installs here and is not on PATH in a fresh shell.
$toolDir = Join-Path $env:USERPROFILE '.dotnet\tools'
if (Test-Path $toolDir) { $env:PATH = "$toolDir;$env:PATH" }

Write-Host '==> Checking prerequisites' -ForegroundColor Cyan

if (-not (Get-Command wix -ErrorAction SilentlyContinue)) {
    throw @'
WiX was not found.

Install it with:
    dotnet tool install --global wix
'@
}

Write-Host ("    WiX " + (wix --version)) -ForegroundColor DarkGray

if ($SkipTests) {
    Write-Host '==> Skipping tests (requested)' -ForegroundColor Yellow
}
else {
    Write-Host '==> Building and testing' -ForegroundColor Cyan

    # Test output is captured to a file and echoed afterwards. Streaming WPF
    # test output directly to the host deadlocks.
    $testLog = Join-Path $env:TEMP 'wincare-test.log'
    & dotnet clean $project -c $Configuration --nologo -v quiet | Out-Null
    & dotnet build $project -c $Configuration --no-incremental --nologo -v minimal
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }

    & dotnet test (Join-Path $root 'WinCareDesktop.Tests\WinCareDesktop.Tests.csproj') `
        -c $Configuration --nologo -v quiet *>&1 |
        Tee-Object -FilePath $testLog | Out-Null

    Get-Content $testLog
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed. Not packaging.' }
}

Write-Host '==> Publishing single-file executable' -ForegroundColor Cyan

if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }

& dotnet publish $project `
    -c $Configuration `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:DebugType=none `
    -o $publishDir `
    --nologo -v minimal

if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }

$exe = Get-ChildItem $publishDir -Filter '*.exe' | Select-Object -First 1
if (-not $exe) { throw 'Publish produced no executable.' }

$sizeMb = [math]::Round($exe.Length / 1MB, 1)
Write-Host ("    $($exe.Name)  ($sizeMb MB)") -ForegroundColor DarkGray

# ── Generate update-source.txt ──────────────────────────────────────────
# The app reads this from beside the exe when the user clicks "Check for
# updates". It is written here, after publish and before the harvest, for two
# reasons:
#
#   1. This script wipes publish/ at the start of every build. A hand-written
#      file placed there does not survive to the next build, which is how a
#      release once shipped an MSI whose update check could never find its
#      source. Generating it as a build step makes that unrepeatable.
#   2. The version is read back out of the .wxs rather than repeated as a
#      literal here, so the JSON cannot drift out of step with the version in
#      the MSI's Package table.
#
# Content is one JSON line and nothing else. The Diagnostics panel parses it and
# reads "url" out of it; any prose in the file is a parse error.
$wxsPath = Join-Path $installerDir 'WinCare.wxs'

if ($wxsText = Get-Content $wxsPath -Raw) {
    if ($wxsText -notmatch '<Package\b[^>]*\sVersion="([^"]+)"') {
        throw "Could not read the package Version from $wxsPath"
    }
    $packageVersion = $Matches[1]
}
else {
    throw "Missing $wxsPath"
}

$updateSourcePath = Join-Path $publishDir 'update-source.txt'
$updateSourceJson = [ordered]@{
    version = $packageVersion
    url     = $UpdateSourceUrl
} | ConvertTo-Json -Compress

# UTF-8 without a BOM. A BOM puts an invisible character ahead of the opening
# brace, which several JSON readers reject outright.
[System.IO.File]::WriteAllText(
    $updateSourcePath,
    $updateSourceJson,
    (New-Object System.Text.UTF8Encoding($false)))

Write-Host "    update-source.txt -> $UpdateSourceUrl" -ForegroundColor DarkGray

# WPF single-file publish is a misnomer: the managed assemblies are bundled
# into the exe, but the native WPF components (wpfgfx_cor3.dll,
# PresentationNative_cor3.dll, D3DCompiler_47_cor3.dll) cannot be and are left
# beside it. An installer that ships only the exe installs cleanly and then
# fails to launch, so the expected file list is derived from the publish
# directory rather than assumed.
$published = Get-ChildItem $publishDir -Recurse -File
Write-Host ("    $($published.Count) file(s) to package") -ForegroundColor DarkGray

Write-Host '==> Building MSI' -ForegroundColor Cyan

if (-not (Test-Path $installerDir)) { New-Item -ItemType Directory -Path $installerDir | Out-Null }

& wix build `
    -arch x64 `
    -d "SourceDir=$publishDir" `
    (Join-Path $installerDir 'WinCare.wxs') `
    -o $msiPath `
    -nologo

if ($LASTEXITCODE -ne 0) { throw 'MSI build failed.' }

$msiKb = [math]::Round((Get-Item $msiPath).Length / 1KB, 1)

# ── Verify the payload actually made it in ──────────────────────────────
# An administrative install extracts the MSI's files to a directory without
# touching the machine. It is used here rather than reading the File table via
# COM because it is the stronger check: it proves Windows Installer itself can
# open and walk the package, not merely that a script can parse it.
#
# A previous build shipped an MSI containing only the exe, which installed
# without error and then failed to launch on every machine. That is exactly the
# failure this catches.
Write-Host '==> Verifying MSI contents' -ForegroundColor Cyan

$extractDir = Join-Path $env:TEMP 'wincare-msi-verify'

if (Test-Path $extractDir) { Remove-Item $extractDir -Recurse -Force }
New-Item -ItemType Directory -Path $extractDir | Out-Null

$adminLog = Join-Path $env:TEMP 'wincare-msi-verify.log'
$admin = Start-Process msiexec -ArgumentList @(
    '/a', "`"$msiPath`"",
    '/qn',
    "TARGETDIR=`"$extractDir`"",
    '/l*v', "`"$adminLog`""
) -Wait -PassThru

if ($admin.ExitCode -ne 0) {
    throw "The MSI could not be read by Windows Installer (exit $($admin.ExitCode)). See $adminLog"
}

$extracted = Get-ChildItem $extractDir -Recurse -File

$missing = $published | Where-Object {
    $name = $_.Name
    -not ($extracted | Where-Object { $_.Name -eq $name })
}

Write-Host ("    published: $($published.Count)   extracted: $($extracted.Count)") -ForegroundColor DarkGray

if ($missing) {
    $missing | ForEach-Object { Write-Host "    MISSING: $($_.Name)" -ForegroundColor Red }
    throw "The MSI is missing $($missing.Count) published file(s). It would install and then fail to launch."
}

Remove-Item $extractDir -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item $adminLog -Force -ErrorAction SilentlyContinue

Write-Host '    every published file is packaged' -ForegroundColor DarkGray

Write-Host ''
Write-Host "Done. Installer: $msiPath  ($msiKb KB)" -ForegroundColor Green
Write-Host ''
Write-Host 'To install:' -ForegroundColor Cyan
Write-Host "  msiexec /i `"$msiPath`""
Write-Host ''
Write-Host 'To uninstall:' -ForegroundColor Cyan
Write-Host "  msiexec /x `"$msiPath`""
Write-Host ''
Write-Host 'The MSI is unsigned. See RELEASE.md before distributing it.' -ForegroundColor Yellow
