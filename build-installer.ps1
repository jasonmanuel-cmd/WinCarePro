# Package an existing payload ONLY. Never builds, republishes, or modifies it.
# Outer package is unsigned; production remains HOLD regardless of this result.
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][ValidateNotNullOrEmpty()][string]$PayloadDir,
    [Parameter(Mandatory=$true)][ValidateNotNullOrEmpty()][string]$OutputDir,
    [Parameter(Mandatory=$true)][ValidateNotNullOrEmpty()][string]$WixPath,
    [string]$SignToolPath,
    [switch]$UnsignedCandidate
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'packaging/Packaging.Common.ps1')
$version = Resolve-PackageVersion (Join-Path $PSScriptRoot 'WinCareDesktop.csproj')
$wxs = Join-Path $PSScriptRoot 'installer/WinCare.wxs'
[xml]$source = Get-Content -LiteralPath $wxs -Raw
if ($source.Wix.Package.Version -ne ($version -replace '\.0$','')) { throw 'WiX version does not match project version.' }
$candidate = Initialize-PackageCandidate $PayloadDir $OutputDir $version $SignToolPath -UnsignedCandidate:$UnsignedCandidate
$msi = Join-Path $candidate.Output 'WinCare.msi'
Invoke-CheckedTool $WixPath @('build','-arch','x64','-d',"SourceDir=$($candidate.Stage)",$wxs,'-o',$msi,'-nologo') 'MSI build failed'
if (-not (Test-Path -LiteralPath $msi -PathType Leaf)) { throw 'MSI build produced no package.' }
# This administrative extraction is a native Windows Installer operation.
# Run only in an explicitly authorized disposable environment; fixtures replace it.
$extract = Join-Path $candidate.Output 'extracted'
New-Item -ItemType Directory -Path $extract | Out-Null
$log = Join-Path $candidate.Output 'extract.log'
$result = Start-Process -FilePath msiexec -ArgumentList @('/a',"`"$msi`"",'/qn',"TARGETDIR=`"$extract`"",'/l*v',"`"$log`"") -Wait -PassThru
if ($result.ExitCode -ne 0) { throw "MSI extraction failed (exit $($result.ExitCode))." }
# Inventory the entire image before selecting the app root; this also rejects
# reparse entries outside the app subtree without traversing them.
$image = Get-PayloadSnapshot $extract
$roots = @($image.Keys | Where-Object { [IO.Path]::GetFileName($_) -eq 'WinCare Pro.exe' })
if ($roots.Count -ne 1) { throw 'Payload identity mismatch: expected one extracted app root.' }
$appRoot = Split-Path (Join-Path $extract $roots[0]) -Parent
$appPrefix = [IO.Path]::GetDirectoryName($roots[0])
if ($appPrefix) { $appPrefix += '\' }
foreach ($name in $image.Keys) {
    # Only this root-level administrative database is metadata. Its bytes may
    # differ from the original MSI; no blanket .msi or directory allowance.
    if ($name -eq 'WinCare.msi') { continue }
    if (-not $name.StartsWith($appPrefix, [StringComparison]::OrdinalIgnoreCase) -or
        -not $candidate.Snapshot.ContainsKey($name.Substring($appPrefix.Length))) {
        throw "Unexpected MSI extraction content: $name"
    }
}
Assert-PayloadSnapshot $candidate.Snapshot $appRoot
Assert-PayloadSnapshot $candidate.Snapshot $candidate.Stage
Assert-PayloadSnapshot $candidate.Snapshot $candidate.Source
if (-not $UnsignedCandidate) {
    foreach ($name in $candidate.Snapshot.Keys) {
        if ([IO.Path]::GetExtension($name) -in @('.exe','.dll')) { Assert-TrustedSignature (Join-Path $appRoot $name) $SignToolPath }
    }
}
Write-CandidateReceipt $candidate $msi $version ([bool]$UnsignedCandidate)
