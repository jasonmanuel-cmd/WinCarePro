# Inert MSBuild warning fixtures: no package sources contacted or app launched.
[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$ScratchRoot)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$run = Join-Path $ScratchRoot ('audit-policy-' + [guid]::NewGuid().ToString('N'))
$ci = Join-Path $run 'ci'
New-Item -ItemType Directory -Force -Path $ci | Out-Null
Copy-Item (Join-Path $PSScriptRoot 'Invoke-DependencyAudit.ps1'), (Join-Path $PSScriptRoot 'NuGet.Config') $ci
$projects = @('WinCareDesktop.csproj', 'WinCareDesktop.Tests/WinCareDesktop.Tests.csproj',
    'Tools/RenderShots/RenderShots.csproj', 'Tools/MakeIcon/MakeIcon.csproj')
$passed = 0
foreach ($code in @('CLEAN', 'NU1900', 'NU1903', 'NU1904', 'NU1905')) {
    foreach ($project in $projects) {
        $path = Join-Path $run $project
        New-Item -ItemType Directory -Force -Path (Split-Path $path -Parent) | Out-Null
        $task = if ($code -eq 'CLEAN') { '<Message Text="INERT clean policy fixture" Importance="high" />' }
                else { '<Warning Code="' + $code + '" Text="INERT audit-policy fixture" />' }
        # Deliberately no SDK/import/package reference: this tests actual CLI
        # warning-to-error handling, not a pretend vulnerability scan.
        Set-Content -LiteralPath $path ('<Project><Target Name="Restore">' + $task + '</Target></Project>')
    }
    $reports = Join-Path $run $code
    & pwsh -NoLogo -NoProfile -NonInteractive -File (Join-Path $ci 'Invoke-DependencyAudit.ps1') -ReportsDirectory $reports *> (Join-Path $run ($code + '.log'))
    $exitCode = $LASTEXITCODE
    $results = @(Get-Content (Join-Path $reports 'audit-results.json') -Raw | ConvertFrom-Json)
    if ($results.Count -ne $projects.Count) { throw "$code did not audit every project" }
    if ($code -eq 'CLEAN') {
        if ($exitCode -ne 0 -or @($results | Where-Object ExitCode -ne 0).Count) { throw 'Clean policy fixture rejected' }
    } else {
        if ($exitCode -eq 0 -or @($results | Where-Object ExitCode -eq 0).Count) { throw "$code was not failed closed" }
        foreach ($result in $results) {
            if ((Get-Content (Join-Path $reports $result.Log) -Raw) -notmatch "error $code") { throw "Missing error evidence for $code" }
        }
    }
    $passed++
    Write-Host "PASS: $code (all four project exits and durable reports checked)"
}
Write-Host "RESULT: $passed audit-policy scenarios passed. Fixtures: $run"
