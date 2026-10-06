# Build a clean release, tag it, and create the GitHub Release with assets.
# Usage: .\release-to-github.ps1 -Version 1.0.0
param(
    [Parameter(Mandatory=$true)][string]$Version
)

$ErrorActionPreference = 'Stop'
$tag = "v$Version"

Write-Host "==> Spot-checking repo is clean"
git diff --quiet --exit-code
if ($LASTEXITCODE -ne 0) { throw "Working tree has uncommitted changes. Commit or stash first." }

Write-Host "==> Building release + installer"
.\build-installer.ps1

$zip = "installer\WinCarePro-$Version-win-x64.zip"
Compress-Archive -Path "publish\*" -DestinationPath $zip -Force

Write-Host "==> Tagging $tag"
git tag -a $tag -m "WinCare Pro $Version"

Write-Host "==> Pushing tag"
git push origin $tag

Write-Host "==> Creating GitHub Release"
gh release create $tag `
    --title "WinCare Pro $Version" `
    --notes-file CHANGELOG.md `
    "installer\WinCare.msi" `
    $zip

Write-Host "Done. Release assets:"
Write-Host "  installer\WinCare.msi"
Write-Host "  $zip"
Write-Host "SHA256 for winget manifest:"
Get-FileHash "installer\WinCare.msi" | Format-List Hash
