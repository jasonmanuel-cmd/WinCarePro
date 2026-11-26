# Shared fail-closed candidate packaging checks. No credential discovery or writes.
Set-StrictMode -Version Latest
function Invoke-CheckedTool {
    param([string]$Tool, [string[]]$Arguments, [string]$Failure)
    if ([string]::IsNullOrWhiteSpace($Tool)) { throw "Missing tool for $Failure" }
    $global:LASTEXITCODE = $null
    & $Tool @Arguments | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "$Failure (exit $LASTEXITCODE)." }
}
function Assert-TrustedSignature {
    param([string]$Path, [string]$SignToolPath)
    # /tw warns on absent timestamps; every nonzero result (including warnings) fails.
    Invoke-CheckedTool $SignToolPath @('verify','/pa','/all','/tw','/v',$Path) "Signature verification failed: $Path"
}
function Resolve-PackageVersion {
    param([string]$ProjectPath, [string]$RequestedVersion)
    [xml]$project = Get-Content -LiteralPath $ProjectPath -Raw
    $nodes = @($project.SelectNodes('/Project/PropertyGroup/Version'))
    if ($nodes.Count -ne 1 -or $nodes[0].InnerText -notmatch '^\d+\.\d+\.\d+$') {
        throw 'Project version must be one explicit three-part numeric Version; no fallback.'
    }
    $version = $nodes[0].InnerText + '.0'
    foreach ($part in $version.Split('.')) {
        if ([long]$part -gt 65535) { throw 'Package version component out of range.' }
    }
    if ($RequestedVersion -and ($RequestedVersion -notmatch '^\d+\.\d+\.\d+\.\d+$' -or $RequestedVersion -ne $version)) {
        throw "Requested version must exactly match project-derived version $version."
    }
    return $version
}
function Get-PayloadSnapshot {
    param([string]$Directory)
    $root = Get-Item -LiteralPath $Directory -Force
    if (-not $root.PSIsContainer -or ($root.Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Payload must be a normal directory.' }
    $prefix = $root.FullName.TrimEnd('\') + '\'
    $queue = [System.Collections.Generic.Queue[string]]::new()
    $queue.Enqueue($root.FullName)
    $snapshot = @{}
    while ($queue.Count) {
        foreach ($entry in Get-ChildItem -LiteralPath $queue.Dequeue() -Force) {
            if ($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Payload reparse point refused: $($entry.Name)" }
            if ($entry.PSIsContainer) { $queue.Enqueue($entry.FullName) }
            else { $snapshot[$entry.FullName.Substring($prefix.Length)] = (Get-FileHash -LiteralPath $entry.FullName -Algorithm SHA256).Hash }
        }
    }
    if (-not $snapshot.Count) { throw 'Empty payload refused.' }
    return $snapshot
}
function Assert-PayloadSnapshot {
    param([hashtable]$Expected, [string]$Directory)
    $actual = Get-PayloadSnapshot $Directory
    if ($actual.Count -ne $Expected.Count) { throw 'Payload identity mismatch: file count.' }
    foreach ($name in $Expected.Keys) {
        if (-not $actual.ContainsKey($name) -or $actual[$name] -ne $Expected[$name]) { throw "Payload identity mismatch: $name" }
    }
}
function Initialize-PackageCandidate {
    param([string]$PayloadDir, [string]$OutputDir, [string]$Version, [string]$SignToolPath, [switch]$UnsignedCandidate)
    $payload = (Get-Item -LiteralPath $PayloadDir).FullName.TrimEnd('\')
    $provider = $null; $drive = $null
    $output = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputDir, [ref]$provider, [ref]$drive).TrimEnd('\')
    if ($provider.Name -ne 'FileSystem') { throw 'Output must be a filesystem path.' }
    if (Test-Path -LiteralPath $output) { throw 'Output directory already exists; never overwrite a candidate.' }
    if (($output + '\').StartsWith($payload + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Output must not be inside payload.' }
    $snapshot = Get-PayloadSnapshot $payload
    if (-not $snapshot.ContainsKey('WinCare Pro.exe')) { throw 'Missing WinCare Pro.exe payload.' }
    $exeVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $payload 'WinCare Pro.exe')).FileVersion
    if ($exeVersion -ne $Version) { throw "Payload executable version '$exeVersion' does not match project version '$Version'." }
    if (-not $UnsignedCandidate) {
        foreach ($name in $snapshot.Keys) {
            if ([IO.Path]::GetExtension($name) -in @('.exe','.dll')) { Assert-TrustedSignature (Join-Path $payload $name) $SignToolPath }
        }
    }
    New-Item -ItemType Directory -Path $output | Out-Null
    $stage = Join-Path $output 'payload'
    New-Item -ItemType Directory -Path $stage | Out-Null
    Get-ChildItem -LiteralPath $payload -Force | Copy-Item -Destination $stage -Recurse -Force
    Assert-PayloadSnapshot $snapshot $stage
    Assert-PayloadSnapshot $snapshot $payload
    return [pscustomobject]@{Source=$payload; Output=$output; Stage=$stage; Snapshot=$snapshot}
}
function Write-CandidateReceipt {
    param($Candidate, [string]$PackagePath, [string]$Version, [bool]$UnsignedPayload)
    # A local identity receipt, NEVER a release approval or signature attestation.
    [ordered]@{
        status='HOLD / NO_SHIP'; version=$Version; package=$PackagePath
        packageSha256=(Get-FileHash -LiteralPath $PackagePath -Algorithm SHA256).Hash
        outerSignature='Not signed by packaging script'; unsignedPayloadCandidate=$UnsignedPayload
        payload=$Candidate.Snapshot
    } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $Candidate.Output 'candidate.json') -Encoding UTF8
    Write-Host "HOLD / NO_SHIP: unsigned package candidate: $PackagePath"
}
