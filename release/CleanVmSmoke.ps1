param([Parameter(Mandatory = $true)][string]$Exe)
$ErrorActionPreference = "Stop"
$signature = Get-AuthenticodeSignature -LiteralPath $Exe
if ($signature.Status -ne "Valid") { throw "Signature is $($signature.Status), expected Valid." }
$process = Start-Process -FilePath $Exe -PassThru
try {
    $deadline = (Get-Date).AddSeconds(30)
    do {
        Start-Sleep -Milliseconds 500
        $process.Refresh()
    } until ($process.MainWindowTitle -like "*WinCare Pro*" -or (Get-Date) -ge $deadline -or $process.HasExited)
    if ($process.HasExited) { throw "Application exited with code $($process.ExitCode)." }
    if ($process.MainWindowTitle -notlike "*WinCare Pro*") { throw "Main window did not appear." }
    "CLEAN_VM_SMOKE_PASS title=$($process.MainWindowTitle) signer=$($signature.SignerCertificate.Subject)"
} finally {
    if (-not $process.HasExited) { Stop-Process -Id $process.Id -Force }
}
