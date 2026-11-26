# Package existing, explicitly selected payload. No publish, signing or cert creation.
# Outer MSIX remains unsigned and on HOLD, including an explicit unsigned fixture.
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][ValidateNotNullOrEmpty()][string]$PayloadDir,
    [Parameter(Mandatory=$true)][ValidateNotNullOrEmpty()][string]$OutputDir,
    [Parameter(Mandatory=$true)][ValidateNotNullOrEmpty()][string]$MakeAppxPath,
    [string]$SignToolPath,
    [Parameter(Mandatory=$true)][string]$Publisher,
    [Parameter(Mandatory=$true)][string]$PublisherDisplayName,
    [Parameter(Mandatory=$true)][string]$IdentityName,
    [string]$Version,
    [switch]$UnsignedCandidate
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'packaging/Packaging.Common.ps1')
$Version = Resolve-PackageVersion (Join-Path $PSScriptRoot 'WinCareDesktop.csproj') $Version
if ([string]::IsNullOrWhiteSpace($Publisher) -or $Publisher -like '*PLACEHOLDER*' -or [string]::IsNullOrWhiteSpace($PublisherDisplayName) -or $PublisherDisplayName -like '*PLACEHOLDER*') { throw 'Explicit approved publisher and display name required.' }
if ($IdentityName -notmatch '^[A-Za-z0-9.-]{3,50}$' -or $IdentityName -like '*PLACEHOLDER*') { throw 'Explicit valid package identity required.' }
$candidate = Initialize-PackageCandidate $PayloadDir $OutputDir $Version $SignToolPath -UnsignedCandidate:$UnsignedCandidate
# Refuse collisions: metadata must not replace any reviewed payload file.
foreach ($reserved in @('Assets','AppxManifest.xml','AppxSignature.p7x','AppxBlockMap.xml','[Content_Types].xml')) {
    if (Test-Path -LiteralPath (Join-Path $candidate.Stage $reserved)) { throw "Reserved MSIX payload path: $reserved" }
}
$assets = Join-Path $candidate.Stage 'Assets'
New-Item -ItemType Directory -Path $assets | Out-Null
Copy-Item (Join-Path $PSScriptRoot 'installer/AppxAssets/*.png') $assets
[xml]$manifest = Get-Content (Join-Path $PSScriptRoot 'installer/AppXManifest.xml') -Raw
$manifest.Package.Identity.Name = $IdentityName
$manifest.Package.Identity.Publisher = $Publisher
$manifest.Package.Identity.Version = $Version
$manifest.Package.Properties.PublisherDisplayName = $PublisherDisplayName
$manifest.Save((Join-Path $candidate.Stage 'AppxManifest.xml'))
$staged = Get-PayloadSnapshot $candidate.Stage
$msix = Join-Path $candidate.Output 'WinCare.msix'
Invoke-CheckedTool $MakeAppxPath @('pack','/d',$candidate.Stage,'/p',$msix,'/v') 'MSIX pack failed'
if (-not (Test-Path -LiteralPath $msix -PathType Leaf)) { throw 'MSIX pack produced no package.' }
$extract = Join-Path $candidate.Output 'extracted'
Invoke-CheckedTool $MakeAppxPath @('unpack','/p',$msix,'/d',$extract) 'MSIX unpack failed'
# Compare all staged files by relative path/hash. MakeAppx may add package metadata.
$actual = Get-PayloadSnapshot $extract
foreach ($name in $staged.Keys) {
    if (-not $actual.ContainsKey($name) -or $actual[$name] -ne $staged[$name]) { throw "Payload identity mismatch: $name" }
}
foreach ($name in $actual.Keys) {
    if (-not $staged.ContainsKey($name) -and $name -notin @('AppxBlockMap.xml','[Content_Types].xml')) { throw "Unexpected package identity content: $name" }
}
[xml]$verified = Get-Content -LiteralPath (Join-Path $extract 'AppxManifest.xml') -Raw
if ($verified.Package.Identity.Name -ne $IdentityName -or $verified.Package.Identity.Publisher -ne $Publisher -or $verified.Package.Identity.Version -ne $Version -or $verified.Package.Identity.ProcessorArchitecture -ne 'x64') { throw 'MSIX identity verification failed.' }
Assert-PayloadSnapshot $staged $candidate.Stage
Assert-PayloadSnapshot $candidate.Snapshot $candidate.Source
if (-not $UnsignedCandidate) {
    foreach ($name in $candidate.Snapshot.Keys) {
        if ([IO.Path]::GetExtension($name) -in @('.exe','.dll')) { Assert-TrustedSignature (Join-Path $extract $name) $SignToolPath }
    }
}
Write-CandidateReceipt $candidate $msix $Version ([bool]$UnsignedCandidate)
