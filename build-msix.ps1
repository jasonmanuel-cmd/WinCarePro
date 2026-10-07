<#
.SYNOPSIS
    Builds the Microsoft Store MSIX package for WinCare Pro.

.DESCRIPTION
    Packs the same published output the MSI uses into an MSIX, so the Store
    build and the direct-download build ship identical binaries.

    Why this exists: the MSI is unsigned, so SmartScreen warns every user on
    first launch. The Store signs the package for us, which removes that
    warning at no cost. That is the whole point of this script.

    The script also self-signs with a throwaway certificate so the package can
    be sideloaded and tested locally. A self-signed package proves the build
    works; it does NOT make SmartScreen trust it. Only a real certificate or
    Store submission does that.

.PARAMETER Configuration
    Debug or Release. Release is the default and what should ship.

.PARAMETER Version
    Four-part version for the package. Defaults to the assembly version.

.EXAMPLE
    .\build-msix.ps1
    .\build-msix.ps1 -Version 1.0.1.0
#>
[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [string]$Version
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$project = Join-Path $root 'WinCareDesktop.csproj'
$publishDir = Join-Path $root 'publish'
$stageDir = Join-Path $root 'msix-stage'
$outDir = Join-Path $root 'installer'
$manifestSrc = Join-Path $outDir 'AppXManifest.xml'
$manifestOut = Join-Path $outDir 'AppXManifest.build.xml'
$msixPath = Join-Path $outDir 'WinCare.msix'
$assetsDir = Join-Path $outDir 'AppxAssets'

Write-Host '==> Locating packaging tools' -ForegroundColor Cyan

# MakeAppx lives in the Windows SDK, which a clean dev box does not have. The
# same NuGet package that provides signtool also provides makeappx and makecat,
# so the Store build needs no SDK install at all.
$sdkPkg = Join-Path $env:USERPROFILE '.nuget\packages\microsoft.windows.sdk.buildtools'
$makeappx = Get-ChildItem $sdkPkg -Recurse -Filter 'makeappx.exe' -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -match '\\x64\\' } |
    Sort-Object FullName -Descending | Select-Object -First 1

if (-not $makeappx) {
    throw @'
makeappx.exe not found.

Install the SDK build tools (no full SDK needed):

  dotnet new classlib -o tools\_sdk
  cd tools\_sdk
  dotnet add package Microsoft.Windows.SDK.BuildTools --prerelease
'@
}
Write-Host "    makeappx: $($makeappx.FullName)" -ForegroundColor DarkGray

Write-Host '==> Building and testing' -ForegroundColor Cyan
& dotnet test (Join-Path $root 'WinCareDesktop.Tests\WinCareDesktop.Tests.csproj') `
    -c $Configuration --nologo -v quiet
if ($LASTEXITCODE -ne 0) { throw 'Tests failed. Refusing to package.' }

Write-Host '==> Publishing' -ForegroundColor Cyan
if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }

# Identical flags to build-installer.ps1. If these ever diverge the two
# packages ship different binaries, which is the kind of bug nobody notices
# until a user reports the wrong version.
& dotnet publish $project `
    -c $Configuration `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:DebugType=none `
    -o $publishDir `
    --nologo -v minimal
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }

if (-not $Version) {
    $asm = Join-Path $publishDir 'WinCare Pro.dll'
    if (Test-Path $asm) {
        $Version = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($asm).FileVersion
    }
    if (-not $Version) { $Version = '1.0.0.0' }
}
# MSIX requires four numeric parts. A three-part assembly version is not valid
# here and makeappx rejects it with a message that does not mention the version.
if ($Version -notmatch '^\d+\.\d+\.\d+\.\d+$') { $Version = "$Version.0" }
Write-Host "    package version: $Version" -ForegroundColor DarkGray

Write-Host '==> Staging payload' -ForegroundColor Cyan
if (Test-Path $stageDir) { Remove-Item $stageDir -Recurse -Force }
New-Item -ItemType Directory -Force -Path $stageDir | Out-Null

Copy-Item (Join-Path $publishDir '*') $stageDir -Recurse -Force

# Assets go to Assets\ inside the package, matching the manifest paths.
$stageAssets = Join-Path $stageDir 'Assets'
New-Item -ItemType Directory -Force -Path $stageAssets | Out-Null
Copy-Item (Join-Path $assetsDir '*.png') $stageAssets -Force

# The manifest's Publisher must equal the signing certificate's subject, or
# sideload refuses to install the package. It is substituted here rather than
# committed, so the repo copy stays free of machine-specific identity.
$publisher = if ($env:WINCARE_MSX_PUBLISHER) { $env:WINCARE_MSX_PUBLISHER } else { 'CN=WinCare Pro' }
$publisherDisplay = if ($env:WINCARE_MSX_PUBLISHER_DISPLAY) { $env:WINCARE_MSX_PUBLISHER_DISPLAY } else { 'WinCare' }

$manifest = Get-Content $manifestSrc -Raw
$manifest = $manifest -replace 'CN=PLACEHOLDER', $publisher
$manifest = $manifest -replace '<PublisherDisplayName>PLACEHOLDER</PublisherDisplayName>',
    "<PublisherDisplayName>$publisherDisplay</PublisherDisplayName>"
$manifest = $manifest -replace 'Version="1\.0\.0\.0"', "Version=`"$Version`""
Set-Content -Path $manifestOut -Value $manifest -Encoding UTF8
Copy-Item $manifestOut (Join-Path $stageDir 'AppxManifest.xml') -Force

Write-Host '==> Building MSIX' -ForegroundColor Cyan
if (Test-Path $msixPath) { Remove-Item $msixPath -Force }

# /d packs a directory tree. Do not pass /l or /f here: /l is unpack-only and
# /f needs a mapping file that this script does not generate. Passing either
# makes makeappx exit non-zero with "Unknown command line option".
$packArgs = @(
    'pack'
    '/d', $stageDir
    '/p', $msixPath
    '/o'
    # Validation here is the point: it confirms every file the manifest
    # references exists, before a package ships that installs and then fails.
    '/v'
)
& $makeappx.FullName @packArgs
if ($LASTEXITCODE -ne 0) { throw 'makeappx pack failed. Run with -Verbose for makeappx output.' }
Write-Host "    built: $msixPath  ($([math]::Round((Get-Item $msixPath).Length / 1MB, 2)) MB)" -ForegroundColor DarkGray

Write-Host '==> Signing' -ForegroundColor Cyan
$signtool = Get-ChildItem $sdkPkg -Recurse -Filter 'signtool.exe' -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -match '\\x64\\' } |
    Sort-Object FullName -Descending | Select-Object -First 1

if (-not $signtool) { throw 'signtool.exe not found; cannot sign the MSIX.' }

$certName = 'CN=' + $publisherDisplay + ' MSIX'

# Prefer a real certificate if one is supplied. That is the only signature a
# customer's machine will trust.
$pfxPath = $env:WINCARE_SIGN_PFX
$pfxPassword = $env:WINCARE_SIGN_PWD
$usingRealCert = [bool]$pfxPath

if (-not $usingRealCert) {
    $cert = Get-ChildItem Cert:\CurrentUser\My | Where-Object { $_.Subject -eq $certName } |
        Sort-Object NotAfter -Descending | Select-Object -First 1
    if (-not $cert) {
        $pw = ConvertTo-SecureString -String 'wincare' -Force -AsPlainText
        New-SelfSignedCertificate -Type CodeSigningCert -Subject $certName `
            -CertStoreLocation 'Cert:\CurrentUser\My' `
            -NotAfter (Get-Date).AddYears(2) `
            -KeyExportPolicy Exportable `
            -KeyUsage DigitalSignature `
            -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3') | Out-Null
        $cert = Get-ChildItem Cert:\CurrentUser\My | Where-Object { $_.Subject -eq $certName } |
            Sort-Object NotAfter -Descending | Select-Object -First 1
        Write-Host "    created self-signed cert ($($cert.Thumbprint.Substring(0, 12))...)" -ForegroundColor DarkGray
    } else {
        Write-Host "    reusing self-signed cert ($($cert.Thumbprint.Substring(0, 12))...)" -ForegroundColor DarkGray
    }
    $pfxPath = Join-Path $env:TEMP 'wincare-msix-test.pfx'
    if (-not (Test-Path $pfxPath)) {
        Export-PfxCertificate -Cert $cert -FilePath $pfxPath -Password $pw -ChainOption EndEntityCertOnly | Out-Null
    }
    $pfxPassword = 'wincare'
}

# MSIX packages are signed, not just hashed, and signtool's MSIX path is
# stricter than its MSI path. Must be http: DigiCert's RFC 3161 endpoint rejects
# the TLS handshake and reports "Invalid Timestamp URL", which reads like a
# certificate problem rather than a transport one.
$signOut = & $signtool.FullName sign /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 `
    /f $pfxPath /p $pfxPassword $msixPath 2>&1
$signExit = $LASTEXITCODE

if ($signExit -ne 0) {
    Write-Host ''
    Write-Host '    SIGNING FAILED' -ForegroundColor Red
    $signOut | Select-Object -Last 6 | ForEach-Object { Write-Host "      $_" -ForegroundColor DarkYellow }
    Write-Host ''
    Write-Host '    The package was BUILT and VALIDATED. It is simply not signed, and' -ForegroundColor Yellow
    Write-Host '    Windows refuses to install an unsigned package (0x800B0100), so you' -ForegroundColor Yellow
    Write-Host '    cannot sideload this build yet.' -ForegroundColor Yellow
    Write-Host ''
    Write-Host '    For Store submission this does not matter: Partner Center' -ForegroundColor Cyan
    Write-Host '    accepts an unsigned .msix and Microsoft signs it on your behalf.' -ForegroundColor Cyan
    Write-Host ''
    Write-Host '    To sideload locally, sign with a real certificate:' -ForegroundColor Cyan
    Write-Host '      $env:WINCARE_SIGN_PFX = "<path to cert.pfx>"' -ForegroundColor DarkGray
    Write-Host '      $env:WINCARE_SIGN_PWD = "<password>"' -ForegroundColor DarkGray
    Write-Host '      .\build-msix.ps1' -ForegroundColor DarkGray
} else {
    $signOut | Where-Object { $_ -match 'Successfully signed|Done Adding' } |
        ForEach-Object { Write-Host "    $_" -ForegroundColor DarkGray }
    if (-not $usingRealCert) {
        Write-Host '    signed with a self-signed test certificate' -ForegroundColor DarkYellow
    } else {
        Write-Host '    signed with the supplied certificate' -ForegroundColor DarkGray
    }
}

Write-Host '==> Verifying package contents' -ForegroundColor Cyan

# A package that builds but is missing a file installs and then fails to
# launch. Checking the payload against the publish output catches that here
# instead of on a user's machine.
$verifyDir = Join-Path $env:TEMP ('wincare-msix-verify-' + [guid]::NewGuid().ToString('N').Substring(0, 8))
New-Item -ItemType Directory -Force -Path $verifyDir | Out-Null
try {
    # No /l here. /l selects the layout for an ENCRYPTED package and prompts for a
    # key on an unencrypted one, which fails and looks like a corrupt package.
    & $makeappx.FullName unpack /p $msixPath /d $verifyDir /o | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'makeappx unpack failed; cannot verify.' }

    $required = @('AppxManifest.xml', 'WinCare Pro.exe')
    $missing = @()
    foreach ($f in $required) {
        if (-not (Test-Path (Join-Path $verifyDir $f))) { $missing += $f }
    }

    # Every asset the manifest names must be present, or install fails.
    $manifestText = Get-Content (Join-Path $verifyDir 'AppxManifest.xml') -Raw
    $assetRefs = [regex]::Matches($manifestText, 'Assets\\[A-Za-z0-9]+\.png') |
        ForEach-Object { $_.Value } | Sort-Object -Unique
    foreach ($a in $assetRefs) {
        $p = Join-Path $verifyDir $a
        if (-not (Test-Path $p)) { $missing += $a }
    }

    # And every published runtime file must have made it in.
    $published = Get-ChildItem $publishDir -File | Select-Object -ExpandProperty Name
    foreach ($f in $published) {
        if (-not (Test-Path (Join-Path $verifyDir $f))) { $missing += $f }
    }

    if ($missing.Count -gt 0) {
        Write-Host "    MISSING from package:" -ForegroundColor Red
        $missing | ForEach-Object { Write-Host "      $_" -ForegroundColor Red }
        throw 'Package is incomplete. Refusing to report success.'
    }

    Write-Host "    manifest + $($assetRefs.Count) asset(s) + $($published.Count) payload file(s): all present" -ForegroundColor DarkGray

    # Confirm the identity actually landed, since it was substituted at build
    # time and a silent no-op here produces a package that will not install.
    $pkgManifest = [xml](Get-Content (Join-Path $verifyDir 'AppxManifest.xml') -Raw)
    $identity = $pkgManifest.Package.Identity
    Write-Host "    identity: $($identity.Name)  $($identity.Version)  $($identity.Publisher)" -ForegroundColor DarkGray
    if ($identity.Publisher -like '*PLACEHOLDER*') {
        throw 'Publisher substitution did not apply; the package cannot be installed.'
    }

    # The presence of this file is the single fact that decides whether the
    # package can be installed. Its absence is otherwise silent.
    $signed = Test-Path (Join-Path $verifyDir 'AppxSignature.p7x')
    if ($signed) {
        Write-Host '    AppxSignature.p7x present - package is signed and installable' -ForegroundColor DarkGray
    } else {
        Write-Host '    AppxSignature.p7x ABSENT - package is valid but not installable yet' -ForegroundColor DarkYellow
    }
} finally {
    Remove-Item $verifyDir -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host ''
Write-Host "Done. Package: $msixPath" -ForegroundColor Green
Write-Host ''
Write-Host 'To submit to the Store (this is the path that matters):'
Write-Host '  1. Reserve the Identity Name (WinCarePro.WinCareDesktop) in Partner Center.'
Write-Host '  2. Reserve the Publisher matching your Partner Center account.'
Write-Host '  3. Set WINCARE_MSX_PUBLISHER and WINCARE_MSX_PUBLISHER_DISPLAY to those values.'
Write-Host '  4. Upload this .msix. Microsoft signs it; you need no certificate of your own.'
Write-Host ''
Write-Host 'To sideload for local testing you must sign it first, because Windows'
Write-Host 'refuses to install an unsigned package:'
Write-Host "  `$env:WINCARE_SIGN_PFX = '<cert.pxf>'   # set both, then re-run this script"
Write-Host "  `$env:WINCARE_SIGN_PWD = '<password>'"
Write-Host "  Add-AppxPackage -Path `"$msixPath`""