param([string]$Exe = (Join-Path $PSScriptRoot "..\dist\WinCarePro.exe"))
$ErrorActionPreference = "Stop"
$windowsRoot = if ($env:SystemRoot) { $env:SystemRoot } else { "C:\Windows" }
$sandbox = Join-Path $windowsRoot "System32\WindowsSandbox.exe"
if (-not (Test-Path -LiteralPath $sandbox)) {
    throw "Windows Sandbox is unavailable. Enable it or run release\CleanVmSmoke.ps1 in a clean Windows 11 VM."
}
$exePath = (Resolve-Path -LiteralPath $Exe).Path
$root = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..")).Path
$config = @"
<Configuration>
  <MappedFolders>
    <MappedFolder><HostFolder>$root</HostFolder><SandboxFolder>C:\WinCarePro</SandboxFolder><ReadOnly>true</ReadOnly></MappedFolder>
  </MappedFolders>
  <LogonCommand>
    <Command>powershell.exe -ExecutionPolicy Bypass -File C:\WinCarePro\release\CleanVmSmoke.ps1 -Exe C:\WinCarePro\dist\$([IO.Path]::GetFileName($exePath))</Command>
  </LogonCommand>
</Configuration>
"@
$wsb = Join-Path $env:TEMP "WinCarePro-clean-test.wsb"
Set-Content -LiteralPath $wsb -Value $config -Encoding UTF8
Start-Process -FilePath $sandbox -ArgumentList $wsb
