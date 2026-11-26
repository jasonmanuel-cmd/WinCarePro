# Explicit two-stage signing: Payload BEFORE packaging; Package AFTER packaging.
# No discovery, no automatic credentials, no implicit publish/installer targets.
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][ValidateNotNullOrEmpty()][string[]]$Targets,
    [Parameter(Mandatory=$true)][ValidateSet('Payload','Package')][string]$Stage,
    [Parameter(Mandatory=$true)][ValidateNotNullOrEmpty()][string]$SignToolPath,
    [Parameter(Mandatory=$true)][ValidateNotNullOrEmpty()][string]$TimestampUrl
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'packaging/Packaging.Common.ps1')
[uri]$timestamp = $null
if (-not [uri]::TryCreate($TimestampUrl, [UriKind]::Absolute, [ref]$timestamp) -or $timestamp.Scheme -notin @('http','https')) {
    throw 'An explicitly approved HTTP(S) timestamp URL is required.'
}
# Validate ALL inputs before the first signing operation.
$resolved = @()
foreach ($target in $Targets) {
    $item = Get-Item -LiteralPath $target -Force
    $allowed = if ($Stage -eq 'Payload') { @('.exe','.dll') } else { @('.msi','.msix') }
    if ($item.PSIsContainer -or $item.Extension -notin $allowed -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
        throw "Target is not allowed in $Stage signing stage: $target"
    }
    $resolved += $item.FullName
}
$pfx = $env:WINCARE_SIGN_PFX
if (-not $pfx -or -not (Test-Path -LiteralPath $pfx -PathType Leaf)) { throw 'No signing certificate configured; no credentials will be created.' }
$password = $env:WINCARE_SIGN_PWD
if ($null -eq $password) { throw 'Signing certificate password environment variable is not configured.' }
foreach ($target in ($resolved | Select-Object -Unique)) {
    # SignTool exposes /p in process arguments. Use only an approved isolated signer.
    Invoke-CheckedTool $SignToolPath @('sign','/fd','SHA256','/tr',$TimestampUrl,'/td','SHA256','/f',$pfx,'/p',$password,$target) "File failed to sign: $target"
    Assert-TrustedSignature $target $SignToolPath
}
Write-Host 'Explicit targets signed and verified. This is NOT release approval.'
