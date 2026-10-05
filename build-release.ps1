<#
.SYNOPSIS
    Builds, tests and publishes WinCare Pro as a single self-contained exe.

.DESCRIPTION
    Produces a single WinCare Pro.exe that runs on any Windows 10/11 x64 box
    with no .NET runtime installed. Run from the project root.

.EXAMPLE
    .\build-release.ps1
    .\build-release.ps1 -Configuration Debug
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [string]$OutputDir
)

# $PSScriptRoot is not populated while param() defaults are evaluated, so
# resolve it here instead.
if (-not $OutputDir) { $OutputDir = Join-Path $PSScriptRoot 'publish' }

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$project = Join-Path $root 'WinCareDesktop.csproj'
$tests = Join-Path $root 'WinCareDesktop.Tests\WinCareDesktop.Tests.csproj'

Write-Host '==> Cleaning' -ForegroundColor Cyan
dotnet clean $project -c $Configuration --nologo -v quiet | Out-Null

Write-Host '==> Building' -ForegroundColor Cyan
dotnet build $project -c $Configuration --no-incremental --nologo -v minimal
if ($LASTEXITCODE -ne 0) { throw "Build failed ($LASTEXITCODE)." }

# The test project references the WPF app, so it can only run where the SDK is.
if (Test-Path $tests) {
    Write-Host '==> Testing' -ForegroundColor Cyan
    # Captured then echoed rather than streamed: the WPF smoke tests spin up an
    # Application, and interleaving that with a live redirected handle can wedge
    # the runner's output pipe.
    $testLog = & dotnet test $tests -c $Configuration --nologo -v quiet 2>&1 | Out-String
    if ($testLog.Trim()) { Write-Host $testLog.Trim() }
    if ($LASTEXITCODE -ne 0) { throw "Tests failed ($LASTEXITCODE). Not publishing." }
}

Write-Host '==> Publishing (self-contained, single file)' -ForegroundColor Cyan
if (Test-Path $OutputDir) { Remove-Item $OutputDir -Recurse -Force }

dotnet publish $project `
    -c $Configuration `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:DebugType=none `
    -p:PublishTrimmed=false `
    -o $OutputDir `
    --nologo -v minimal

if ($LASTEXITCODE -ne 0) { throw "Publish failed ($LASTEXITCODE)." }

$exe = Join-Path $OutputDir 'WinCare Pro.exe'
if (-not (Test-Path $exe)) { throw "Expected $exe but it was not produced." }

$sizeMb = [math]::Round((Get-Item $exe).Length / 1MB, 1)
Write-Host ''
Write-Host "Done. WinCare Pro.exe ($sizeMb MB)" -ForegroundColor Green
Write-Host "  $exe"
Write-Host ''
Write-Host 'To install for the current user (creates Start Menu + optional auto-start):'
Write-Host '  powershell -ExecutionPolicy Bypass -File .\install.ps1'