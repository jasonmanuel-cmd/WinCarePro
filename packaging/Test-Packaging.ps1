# Only inert tools are invoked. Writes are isolated beneath ScratchRoot.
[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$ScratchRoot)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repo = Split-Path $PSScriptRoot -Parent
$run = Join-Path $ScratchRoot ('packaging-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $run | Out-Null
foreach ($name in @('sign-release.ps1','build-installer.ps1','build-msix.ps1','release-to-github.ps1','WinCareDesktop.csproj')) {
    Copy-Item (Join-Path $repo $name) $run
}
Copy-Item (Join-Path $repo 'packaging') $run -Recurse
New-Item -ItemType Directory -Path (Join-Path $run 'installer') | Out-Null
Copy-Item (Join-Path $repo 'installer/WinCare.wxs'),(Join-Path $repo 'installer/AppXManifest.xml') (Join-Path $run 'installer')
Copy-Item (Join-Path $repo 'installer/AppxAssets') (Join-Path $run 'installer') -Recurse
$payload = Join-Path $run 'payload'
New-Item -ItemType Directory -Path $payload | Out-Null
# Read a small existing versioned apphost; NEVER launch it.
Copy-Item (Join-Path $repo 'bin/Release/net10.0-windows/WinCare Pro.exe') $payload
Set-Content (Join-Path $payload 'native.dll') 'inert sidecar'
Set-Content (Join-Path $run 'dummy.pfx') 'NOT A CREDENTIAL'
$env:WINCARE_SIGN_PFX = Join-Path $run 'dummy.pfx'
$env:WINCARE_SIGN_PWD = 'inert'
$global:Calls = [System.Collections.Generic.List[string]]::new()
$global:SignExit = 0; $global:VerifyExit = 0; $global:PackExit = 0; $global:ExtractExit = 0
$global:Corrupt = $false; $global:PackedSource = ''; $global:WrongIdentity = $false
function global:dotnet { throw 'FORBIDDEN dotnet call' }
function global:git { throw 'FORBIDDEN git call' }
function global:gh { throw 'FORBIDDEN gh call' }
function global:New-SelfSignedCertificate { throw 'FORBIDDEN certificate creation' }
function global:Export-PfxCertificate { throw 'FORBIDDEN certificate export' }
function global:MockSignTool {
    $global:Calls.Add("signTool $($args -join ' ')")
    if ($args[0] -eq 'sign') {
        $global:LASTEXITCODE = $global:SignExit
        if ($global:SignExit -eq 0) { Add-Content -LiteralPath $args[-1] -Value 'INERT SIGNATURE MARKER' }
    } else { $global:LASTEXITCODE = $global:VerifyExit }
}
function global:MockWix {
    $global:Calls.Add('wix build')
    $sourceArg = @($args | Where-Object { "$_" -like 'SourceDir=*' })[0]
    $global:PackedSource = "$sourceArg".Substring(10)
    $out = $args[[array]::IndexOf($args, '-o') + 1]
    if ($global:PackExit -eq 0 -and -not $global:MissingPackage) { Set-Content -LiteralPath $out 'INERT MSI' }
    $global:LASTEXITCODE = $global:PackExit
}
function global:Start-Process {
    param($FilePath, $ArgumentList, [switch]$Wait, [switch]$PassThru)
    if ($FilePath -ne 'msiexec') { throw 'Unexpected process' }
    $global:Calls.Add('msi extract')
    $targetArg = @($ArgumentList | Where-Object { "$_" -like 'TARGETDIR=*' })[0]
    $dest = "$targetArg".Substring(10).Trim('"')
    # Administrative images contain a rewritten MSI at their root, not payload.
    Set-Content (Join-Path $dest 'WinCare.msi') 'INERT ADMINISTRATIVE DATABASE'
    if ($global:OutsideFile) {
        $extra=Join-Path $dest $global:OutsideFile
        New-Item -ItemType Directory -Force -Path (Split-Path $extra -Parent) | Out-Null
        Set-Content -LiteralPath $extra 'unreviewed inert bytes'
    }
    $dest = Join-Path $dest 'WinCare Pro'
    New-Item -ItemType Directory -Path $dest | Out-Null
    Copy-Item (Join-Path $global:PackedSource '*') $dest -Recurse
    if ($global:Corrupt) { Set-Content (Join-Path $dest 'native.dll') 'tampered' }
    [pscustomobject]@{ExitCode=$global:ExtractExit}
}
function global:MockMakeAppx {
    $global:Calls.Add("makeappx $($args[0])")
    $dest = $args[[array]::IndexOf($args, '/d') + 1]
    if ($args[0] -eq 'pack') {
        $global:PackedSource = $dest
        $out = $args[[array]::IndexOf($args, '/p') + 1]
        if ($global:PackExit -eq 0 -and -not $global:MissingPackage) { Set-Content -LiteralPath $out 'INERT MSIX' }
        $global:LASTEXITCODE = $global:PackExit
    } else {
        New-Item -ItemType Directory -Path $dest | Out-Null
        Copy-Item (Join-Path $global:PackedSource '*') $dest -Recurse
        if ($global:Corrupt) { Set-Content (Join-Path $dest 'native.dll') 'tampered' }
        if ($global:WrongIdentity) {
            $m = Join-Path $dest 'AppxManifest.xml'
            (Get-Content $m -Raw).Replace('2.0.0.0','9.0.0.0') | Set-Content $m
        }
        $global:LASTEXITCODE = $global:ExtractExit
    }
}
$script:Passed = 0; $script:Failed = 0
function Test([string]$Name, [scriptblock]$Body) {
    $global:SignExit=0; $global:VerifyExit=0; $global:PackExit=0; $global:ExtractExit=0
    $global:OutsideFile=''; $global:MissingPackage=$false
    $global:Corrupt=$false; $global:WrongIdentity=$false; $global:Calls.Clear()
    try { & $Body; $script:Passed++; Write-Host "PASS: $Name" }
    catch { $script:Failed++; Write-Host "FAIL: $Name : $($_.Exception.Message)" }
}
function Reject([scriptblock]$Body, [string]$Pattern) {
    $message = ''
    try { & $Body } catch { $message = $_.Exception.Message }
    if (-not $message -or $message -notlike $Pattern) { throw "Expected '$Pattern', got '$message'" }
}
function OutDir { Join-Path $run ([guid]::NewGuid().ToString('N')) }
$sign = @{Targets=@((Join-Path $payload 'WinCare Pro.exe')); Stage='Payload'; SignToolPath='MockSignTool'; TimestampUrl='https://invalid.example/inert'}
$msi = @{PayloadDir=$payload; OutputDir=''; WixPath='MockWix'; SignToolPath='MockSignTool'}
$msix = @{PayloadDir=$payload; OutputDir=''; MakeAppxPath='MockMakeAppx'; SignToolPath='MockSignTool'; Publisher='CN=Fixture'; PublisherDisplayName='Fixture'; IdentityName='Fixture.WinCare'}
Test 'relative output follows PowerShell location, not process location' {
    . (Join-Path $run 'packaging/Packaging.Common.ps1')
    $processLocation=[Environment]::CurrentDirectory
    $location=Join-Path $run 'ps-location'
    New-Item -ItemType Directory -Path $location | Out-Null
    Push-Location $location
    try {
        [Environment]::CurrentDirectory=$run
        $candidate=Initialize-PackageCandidate $payload 'relative-output' '2.0.0.0' 'MockSignTool' -UnsignedCandidate
        if ($candidate.Output -ne (Join-Path $location 'relative-output')) { throw "Output resolved outside PowerShell location: $($candidate.Output)" }
        if (Test-Path (Join-Path $run 'relative-output')) { throw 'Wrote under process location' }
    } finally { Pop-Location; [Environment]::CurrentDirectory=$processLocation }
}
Test 'sign verification failure closes gate' {
    $global:VerifyExit=1
    Reject { & (Join-Path $run 'sign-release.ps1') @sign } '*verification failed*'
}
Test 'sign failure closes gate' {
    $global:SignExit=1
    Reject { & (Join-Path $run 'sign-release.ps1') @sign } '*failed to sign*'
}
Test 'no implicit signing targets' {
    $s=$sign.Clone(); $s.Targets=@()
    Reject { & (Join-Path $run 'sign-release.ps1') @s } '*'
    if ($global:Calls.Count) { throw 'tool invoked without target' }
}
Test 'mixed payload/package signing refused' {
    $s=$sign.Clone(); $s.Targets=@((Join-Path $payload 'WinCare Pro.exe'),(Join-Path $run 'fake.msi'))
    Set-Content $s.Targets[1] 'inert'
    Reject { & (Join-Path $run 'sign-release.ps1') @s } '*stage*'
    if ($global:Calls.Count) { throw 'partially signed before validation' }
}
Test 'missing credentials refused' {
    $saved=$env:WINCARE_SIGN_PFX; $env:WINCARE_SIGN_PFX=''
    try { Reject { & (Join-Path $run 'sign-release.ps1') @sign } '*certificate*' }
    finally { $env:WINCARE_SIGN_PFX=$saved }
}
Test 'release disabled before external commands' {
    Reject { & (Join-Path $run 'release-to-github.ps1') -Version '2.0.0' } '*HOLD*'
}
Test 'MSI consumes exact signed payload without publish' {
    & (Join-Path $run 'sign-release.ps1') @sign
    $msi.OutputDir=OutDir
    $before=(Get-FileHash $sign.Targets[0]).Hash
    & (Join-Path $run 'build-installer.ps1') @msi
    if ((Get-FileHash $sign.Targets[0]).Hash -ne $before) { throw 'source payload changed' }
    if ((Get-FileHash (Join-Path $global:PackedSource 'WinCare Pro.exe')).Hash -ne $before) { throw 'packaged different bytes' }
    if ($global:Calls[0] -notlike 'signTool sign*' -or $global:Calls.IndexOf('wix build') -lt 2) { throw 'wrong ordering' }
}
Test 'MSI unsigned payload rejected before packaging' {
    $global:VerifyExit=1; $msi.OutputDir=OutDir
    Reject { & (Join-Path $run 'build-installer.ps1') @msi } '*verification failed*'
    if ($global:Calls.Contains('wix build')) { throw 'packaged unsigned payload' }
}
Test 'MSI pack failure rejected' {
    $global:PackExit=1; $msi.OutputDir=OutDir
    Reject { & (Join-Path $run 'build-installer.ps1') @msi } '*MSI build failed*'
}
Test 'MSI extraction failure rejected' {
    $global:ExtractExit=1; $msi.OutputDir=OutDir
    Reject { & (Join-Path $run 'build-installer.ps1') @msi } '*extraction failed*'
}
Test 'MSI altered payload rejected' {
    $global:Corrupt=$true; $msi.OutputDir=OutDir
    Reject { & (Join-Path $run 'build-installer.ps1') @msi } '*Payload identity*'
}
foreach ($extra in @('unreviewed.dll','Other\nested.dll','WinCare Pro-other\native.dll','Other\WinCare.msi','unexpected.msi')) {
    Test "MSI rejects outside payload file $extra" {
        $global:OutsideFile=$extra; $msi.OutputDir=OutDir
        Reject { & (Join-Path $run 'build-installer.ps1') @msi } '*Unexpected MSI extraction content*'
        if (Test-Path (Join-Path $msi.OutputDir 'candidate.json')) { throw 'Rejected candidate received receipt' }
    }
}
Test 'MSI existing output preserved' {
    $msi.OutputDir=OutDir; New-Item -ItemType Directory $msi.OutputDir | Out-Null
    Set-Content (Join-Path $msi.OutputDir 'sentinel') 'keep'
    Reject { & (Join-Path $run 'build-installer.ps1') @msi } '*already exists*'
    if ((Get-Content (Join-Path $msi.OutputDir 'sentinel')) -ne 'keep') { throw 'overwritten' }
}
Test 'MSIX project version and payload identity preserved' {
    $msix.OutputDir=OutDir
    & (Join-Path $run 'build-msix.ps1') @msix
    [xml]$manifest=Get-Content (Join-Path $global:PackedSource 'AppxManifest.xml')
    if ($manifest.Package.Identity.Version -ne '2.0.0.0') { throw 'version drift' }
    if ((Get-FileHash (Join-Path $global:PackedSource 'WinCare Pro.exe')).Hash -ne (Get-FileHash $sign.Targets[0]).Hash) { throw 'payload changed' }
    if (@($global:Calls | Where-Object { $_ -like 'signTool sign*' }).Count) { throw 'implicit signing' }
}
Test 'MSIX unsigned payload rejected' {
    $global:VerifyExit=1; $msix.OutputDir=OutDir
    Reject { & (Join-Path $run 'build-msix.ps1') @msix } '*verification failed*'
}
Test 'explicit unsigned candidate never signs or creates credentials' {
    $global:VerifyExit=1; $msix.OutputDir=OutDir
    & (Join-Path $run 'build-msix.ps1') @msix -UnsignedCandidate
    if (@($global:Calls | Where-Object { $_ -like 'signTool*' }).Count) { throw 'signing tool touched' }
}
Test 'MSIX altered payload rejected' {
    $global:Corrupt=$true; $msix.OutputDir=OutDir
    Reject { & (Join-Path $run 'build-msix.ps1') @msix } '*Payload identity*'
}
Test 'MSIX extracted manifest byte mismatch rejected' {
    $global:WrongIdentity=$true; $msix.OutputDir=OutDir
    Reject { & (Join-Path $run 'build-msix.ps1') @msix } 'Payload identity mismatch: AppxManifest.xml'
}
Test 'MSIX semantic architecture mismatch rejected despite identical manifest bytes' {
    $path=Join-Path $run 'installer/AppXManifest.xml'; $saved=Get-Content $path -Raw
    try {
        [xml]$xml=$saved; $xml.Package.Identity.ProcessorArchitecture='x86'; $xml.Save($path)
        $msix.OutputDir=OutDir
        Reject { & (Join-Path $run 'build-msix.ps1') @msix } 'MSIX identity verification failed.'
        if ((Get-FileHash (Join-Path $msix.OutputDir 'payload/AppxManifest.xml')).Hash -ne
            (Get-FileHash (Join-Path $msix.OutputDir 'extracted/AppxManifest.xml')).Hash) { throw 'Hash gate masked semantic gate' }
        if (Test-Path (Join-Path $msix.OutputDir 'candidate.json')) { throw 'Invalid identity received receipt' }
    } finally { Set-Content $path $saved }
}
Test 'MSIX pack failure rejected' {
    $global:PackExit=1; $msix.OutputDir=OutDir
    Reject { & (Join-Path $run 'build-msix.ps1') @msix } '*MSIX pack failed*'
}
Test 'MSIX unpack failure rejected' {
    $global:ExtractExit=1; $msix.OutputDir=OutDir
    Reject { & (Join-Path $run 'build-msix.ps1') @msix } '*MSIX unpack failed*'
}
Test 'MSIX missing publisher refused' {
    $m=$msix.Clone(); $m.Publisher=''; $m.OutputDir=OutDir
    Reject { & (Join-Path $run 'build-msix.ps1') @m } '*publisher*'
}
Test 'MSIX version override mismatch refused' {
    $msix.OutputDir=OutDir
    Reject { & (Join-Path $run 'build-msix.ps1') @msix -Version '1.0.0.0' } '*version*'
}
Test 'MSIX invalid version refused' {
    $msix.OutputDir=OutDir
    Reject { & (Join-Path $run 'build-msix.ps1') @msix -Version '2.0' } '*version*'
}
Test 'missing project version never falls back' {
    $proj=Join-Path $run 'WinCareDesktop.csproj'; $saved=Get-Content $proj -Raw
    try {
        $saved.Replace('<Version>2.0.0</Version>','') | Set-Content $proj
        $msix.OutputDir=OutDir
        Reject { & (Join-Path $run 'build-msix.ps1') @msix } '*version*'
    } finally { Set-Content $proj $saved }
}
Test 'signature warning exit 2 fails closed' {
    $global:VerifyExit=2
    Reject { & (Join-Path $run 'sign-release.ps1') @sign } '*verification failed*exit 2*'
}
Test 'missing password refuses signing before tool call' {
    $saved=$env:WINCARE_SIGN_PWD; Remove-Item Env:WINCARE_SIGN_PWD
    try {
        Reject { & (Join-Path $run 'sign-release.ps1') @sign } '*password*not configured*'
        if ($global:Calls.Count) { throw 'Tool called without password' }
    } finally { $env:WINCARE_SIGN_PWD=$saved }
}
Test 'package stage signs and verifies only explicit inert packages' {
    $s=$sign.Clone(); $s.Stage='Package'; $s.Targets=@((Join-Path $run 'outer.msi'),(Join-Path $run 'outer.msix'))
    foreach ($p in $s.Targets) { Set-Content $p 'inert package' }
    & (Join-Path $run 'sign-release.ps1') @s
    if ($global:Calls.Count -ne 4 -or $global:Calls[0] -notlike 'signTool sign*' -or $global:Calls[1] -notlike 'signTool verify*' -or $global:Calls[2] -notlike 'signTool sign*' -or $global:Calls[3] -notlike 'signTool verify*') { throw 'Wrong package signing sequence' }
    foreach ($p in $s.Targets) { if ((Get-Content $p -Raw) -notlike '*INERT SIGNATURE MARKER*') { throw 'Missing inert signature' } }
}
Test 'MSI unsigned candidate permits administrative metadata and never signs' {
    $global:VerifyExit=1; $msi.OutputDir=OutDir
    & (Join-Path $run 'build-installer.ps1') @msi -UnsignedCandidate
    if (@($global:Calls | Where-Object { $_ -like 'signTool*' }).Count) { throw 'Signing tool touched' }
    $receipt=Get-Content (Join-Path $msi.OutputDir 'candidate.json') -Raw | ConvertFrom-Json
    if (-not $receipt.unsignedPayloadCandidate -or $receipt.status -ne 'HOLD / NO_SHIP') { throw 'Wrong receipt status' }
    if ($receipt.packageSha256 -ne (Get-FileHash (Join-Path $msi.OutputDir 'WinCare.msi')).Hash) { throw 'Wrong outer hash' }
}
foreach ($kind in @('msi','msix')) {
    Test "$kind exit zero without package rejected before extraction" {
        $global:MissingPackage=$true
        $m=if ($kind -eq 'msi') { $msi.Clone() } else { $msix.Clone() }; $m.OutputDir=OutDir
        $scriptName=if ($kind -eq 'msi') { 'build-installer.ps1' } else { 'build-msix.ps1' }
        Reject { & (Join-Path $run $scriptName) @m } '*produced no package*'
        if ($global:Calls.Contains('msi extract') -or $global:Calls.Contains('makeappx unpack')) { throw 'Extraction reached' }
    }
}
Test 'MSIX existing output preserved' {
    $msix.OutputDir=OutDir; New-Item -ItemType Directory $msix.OutputDir | Out-Null
    Set-Content (Join-Path $msix.OutputDir 'sentinel') 'keep'
    Reject { & (Join-Path $run 'build-msix.ps1') @msix } '*already exists*'
    if ((Get-Content (Join-Path $msix.OutputDir 'sentinel')) -ne 'keep' -or $global:Calls.Count) { throw 'Existing output touched' }
}
foreach ($reserved in @('Assets','AppxManifest.xml','AppxSignature.p7x','AppxBlockMap.xml','[Content_Types].xml')) {
    Test "MSIX reserved path refused: $reserved" {
        $path=Join-Path $payload $reserved; Set-Content -LiteralPath $path 'inert collision'
        try {
            $msix.OutputDir=OutDir
            Reject { & (Join-Path $run 'build-msix.ps1') @msix } ([WildcardPattern]::Escape("Reserved MSIX payload path: $reserved"))
            if ($global:Calls.Contains('makeappx pack')) { throw 'Packed collision' }
        } finally { Remove-Item -LiteralPath $path }
    }
}
Test 'XML special publisher text round trips' {
    $m=$msix.Clone(); $m.OutputDir=OutDir; $m.Publisher='CN=Fixture & "Quoted"'; $m.PublisherDisplayName='Fixture <Display> & Co'
    & (Join-Path $run 'build-msix.ps1') @m
    [xml]$xml=Get-Content (Join-Path $m.OutputDir 'extracted/AppxManifest.xml') -Raw
    if ($xml.Package.Identity.Publisher -ne $m.Publisher -or $xml.Package.Properties.PublisherDisplayName -ne $m.PublisherDisplayName) { throw 'XML text changed' }
}
Test 'project version component over 65535 refused' {
    . (Join-Path $run 'packaging/Packaging.Common.ps1')
    $p=Join-Path $run 'out-of-range.csproj'; Set-Content $p '<Project><PropertyGroup><Version>65536.0.0</Version></PropertyGroup></Project>'
    Reject { Resolve-PackageVersion $p } '*component out of range*'
}
Write-Host "RESULT: $script:Passed passed; $script:Failed failed. Fixtures: $run"
if ($script:Failed) { exit 1 }
