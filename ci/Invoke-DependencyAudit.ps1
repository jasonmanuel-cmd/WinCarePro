# Restore-time audit; no publishing, signing, install, or host changes.
[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$ReportsDirectory)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repo = Split-Path $PSScriptRoot -Parent
New-Item -ItemType Directory -Force -Path $ReportsDirectory | Out-Null
$projects = @('WinCareDesktop.csproj', 'WinCareDesktop.Tests/WinCareDesktop.Tests.csproj',
    'Tools/RenderShots/RenderShots.csproj', 'Tools/MakeIcon/MakeIcon.csproj', 'Tools/LicenseGen/LicenseGen.csproj')
$results = @()
$failed = $false
foreach ($project in $projects) {
    $name = ($project -replace '[/\\]', '_')
    $log = Join-Path $ReportsDirectory ($name + '.log')
    # --force prevents a no-op restore; --no-cache refreshes advisory data.
    # All restore warnings are errors: includes NU1900/NU1905 source failures,
    # NU1903/NU1904 high/critical findings, and future unknown restore warnings.
    & dotnet restore (Join-Path $repo $project) --force --no-cache --nologo `
        --configfile (Join-Path $PSScriptRoot 'NuGet.Config') `
        -p:NuGetAudit=true -p:NuGetAuditMode=all -p:NuGetAuditLevel=high `
        -p:TreatWarningsAsErrors=true -warnaserror *>&1 | Tee-Object -FilePath $log | Out-Null
    $code = $LASTEXITCODE
    Get-Content -LiteralPath $log
    $results += [pscustomobject]@{ Project=$project; ExitCode=$code; Log=$name + '.log' }
    if ($code -ne 0) { $failed = $true }
}
$results | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $ReportsDirectory 'audit-results.json')
if ($failed) { throw 'Dependency audit failed. Inspect project logs: findings and unavailable advisory data are not a clean scan.' }
