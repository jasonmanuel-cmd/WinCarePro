# WinCare Pro — GitHub release + winget prep

## 1. Push the repo to GitHub (one time)

```powershell
cd C:\Users\blunt\Desktop\wincare-desktop
git remote add origin https://github.com/jasonmanuel-cmd/WinCarePro.git
git push -u origin master
```

Create the remote first with:

```powershell
gh repo create jasonmanuel-cmd/WinCarePro --public --source . --remote origin
```

## 2. Cut a GitHub Release

```powershell
.\release-to-github.ps1 -Version 1.0.0
```

This publishes the self-contained build, rebuilds the MSI, creates a zip of
`publish\`, tags `v1.0.0`, and attaches `WinCare.msi` + the zip to the release
as downloadable assets. The release body comes from `CHANGELOG.md`.

## 3. Point the app's update check at the release

Copy the contents of `packaging\update-source.sample.txt` to a file named
`update-source.txt` next to `WinCare Pro.exe` and adjust the URL to the real
release manifest. The in-app updater fetches:

```json
{ "version": "1.0.0", "url": "https://github.com/jasonmanuel-cmd/WinCarePro/releases/latest" }
```

No server to run — GitHub Releases is the CDN.

## 4. winget manifest (community repo)

`packaging\winget\` mirrors the three-file layout the winget-pkgs repo expects:

- `WinCare.WinCarePro.yaml` — version + installer + locale combined.

To publish, fork `microsoft/winget-pkgs`, copy the manifest into
`manifests/w/WinCare/WinCarePro/1.0.0/`, fill in the SHA256 placeholders after
the release asset is finalized, and open a PR. SHA256 of each asset:

```powershell
Get-FileHash .\installer\WinCare.msi
```

## Legal note on unsigned builds

Both the MSI and the exe are unsigned. Windows SmartScreen will warn on first
run. That is acceptable for v1.0 freeware distribution but not a paid product —
see RELEASE.md §1 before monetizing.
