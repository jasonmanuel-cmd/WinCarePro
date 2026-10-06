<#
.SYNOPSIS
    Signs the WinCare Pro installer and executable.

.DESCRIPTION
    An unsigned Windows executable triggers SmartScreen on first launch and
    builds no reputation. That is the single biggest source of abandoned
    installs, so signing is not optional for a distributed build.

    The certificate is read from environment variables rather than from disk or
    a parameter, so it never ends up in a script, in a shell history file, or in
    a CI log. .gitignore also blocks *.pfx as a second line of defence.

    Set these before running:
        $env:WINCARE_SIGN_PFX = 'C:\secure\wincare.pfx'
        $env:WINCARE_SIGN_PWD = '<password>'
        $env:WINCARE_SIGN_URL = 'https://timestamp.digicert.com'   # optional

.PARAMETER SkipTimestamp
    Disables RFC 3161 timestamping. Only for local test builds. A signature
    without a timestamp stops validating once the certificate expires, which for
    a two-year certificate means the app appears broken in year three.
#>
[CmdletBinding()]
param(
    [switch]$SkipTimestamp,
    [string]$TimestampUrl = 'https://timestamp.digicert.com'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$root = $PSScriptRoot

# ── Locate signtool ──────────────────────────────────────────────────────
# The Windows SDK is not present on a clean machine and is a large install. The
# NuGet package is a few megabytes and drops the exe where we can find it, which
# keeps this script runnable without the full SDK.
function Find-SignTool {
    $fromSdk = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin" `
        -Recurse -Filter 'signtool.exe' -ErrorAction SilentlyContinue |
        Select-Object -First 1
    if ($fromSdk) { return $fromSdk.FullName }

    $nuget = "$env:USERPROFILE\.nuget\packages\microsoft.windows.sdk.buildtools"
    if (Test-Path $nuget) {
        $fromNuget = Get-ChildItem $nuget -Recurse -Filter 'signtool.exe' -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -match 'x64' } | Select-Object -First 1
        if ($fromNuget) { return $fromNuget.FullName }
    }

    return $null
}

$signTool = Find-SignTool
if (-not $signTool) {
    Write-Host 'signtool.exe was not found.' -ForegroundColor Yellow
    Write-Host ''
    Write-Host 'Install the Windows SDK, or fetch the build tools package:' -ForegroundColor Cyan
    Write-Host '  dotnet add package Microsoft.Windows.SDK.BuildTools --prerelease'
    Write-Host ''
    Write-Host 'and re-run this script.' -ForegroundColor Cyan
    exit 2
}

Write-Host "Using: $signTool" -ForegroundColor DarkGray

# ── Credentials ──────────────────────────────────────────────────────────
$pfx = $env:WINCARE_SIGN_PFX
$password = $env:WINCARE_SIGN_PWD

if (-not $pfx -or -not (Test-Path $pfx)) {
    Write-Host 'No signing certificate configured.' -ForegroundColor Yellow
    Write-Host ''
    Write-Host 'Set the certificate path and password:' -ForegroundColor Cyan
    Write-Host '  $env:WINCARE_SIGN_PFX = "C:\secure\wincare.pfx"'
    Write-Host '  $env:WINCARE_SIGN_PWD = "<password>"'
    Write-Host ''
    Write-Host 'See RELEASE.md for what to buy and why EV is the better choice.' -ForegroundColor Cyan
    exit 2
}

$timestamp = if ($SkipTimestamp) { $null } else { $TimestampUrl }

# ── What to sign ────────────────────────────────────────────────────────
# The MSI first, then the executable inside it. Signing in this order means the
# MSI's own signature covers an already-signed payload. Signing the exe after
# the MSI would require rebuilding the MSI to embed the new signature.
$targets = @()
$msi = Join-Path $root 'installer\WinCare.msi'
if (Test-Path $msi) { $targets += $msi }

$exe = Get-ChildItem (Join-Path $root 'publish') -Filter '*.exe' -ErrorAction SilentlyContinue |
    Select-Object -First 1
if ($exe) { $targets += $exe.FullName }

if ($targets.Count -eq 0) {
    throw 'Nothing to sign. Run build-installer.ps1 first.'
}

$failed = 0

foreach ($target in $targets) {
    $name = Split-Path $target -Leaf
    Write-Host "Signing $name" -ForegroundColor Cyan

    # Built as a list rather than a single array literal on purpose: comments
    # inside an array literal swallow the following element, and the previous
    # version silently lost its digest and timestamp arguments because of it.
    #
    # The variable is not called $args because that shadows PowerShell's
    # automatic $args, which holds the script's own unbound arguments.
    $signArgs = [System.Collections.Generic.List[string]]::new()

    $signArgs.Add('sign')

    # Authenticode digest algorithm for the signature itself.
    $signArgs.Add('/fd')
    $signArgs.Add('SHA256')

    if (-not $SkipTimestamp) {
        # RFC 3161 timestamp. Without it the signature stops validating when
        # the certificate expires, which for a two-year cert means the app
        # looks tampered with in year three.
        $signArgs.Add('/tr')
        $signArgs.Add($TimestampUrl)
        $signArgs.Add('/td')
        $signArgs.Add('SHA256')
    }

    $signArgs.Add('/f')
    $signArgs.Add($pfx)

    # signtool takes the password as a command-line argument and has no option
    # to read it from anywhere else short of importing into the certificate
    # store first. It therefore appears in the process argument list while the
    # command runs. It is not echoed here; on a shared or CI machine prefer
    # importing the certificate into the user's store and dropping /f and /p.
    $signArgs.Add('/p')
    $signArgs.Add($password)

    $signArgs.Add($target)

    & $signTool @signArgs
    if ($LASTEXITCODE -ne 0) {
        Write-Host "  FAILED (exit $LASTEXITCODE)" -ForegroundColor Red
        $failed++
    }
    else {
        Write-Host '  ok' -ForegroundColor Green
    }
}

if ($failed -gt 0) {
    throw "$failed file(s) failed to sign."
}

Write-Host ''
Write-Host 'Verifying signatures' -ForegroundColor Cyan

foreach ($target in $targets) {
    $name = Split-Path $target -Leaf

    # /pa verifies against the default (Authenticode) policy, which is what
    # Windows applies to a downloaded file.
    $result = & $signTool verify /pa /v $target 2>&1

    if ($LASTEXITCODE -eq 0) {
        Write-Host "  $name - valid" -ForegroundColor Green
    }
    else {
        Write-Host "  $name - NOT VALID" -ForegroundColor Red
        $result | Select-Object -First 5 | ForEach-Object {
            Write-Host "    $_" -ForegroundColor DarkGray
        }
    }
}
