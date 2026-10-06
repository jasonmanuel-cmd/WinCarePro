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

## 3. Update check wiring (handled by the build)

Two files, and it is worth being precise about which is which:

1. **`update-source.txt` ships next to the exe.** One JSON line, no prose:

   ```json
   { "version": "1.0.0", "url": "https://raw.githubusercontent.com/jasonmanuel-cmd/WinCarePro/master/packaging/version.json" }
   ```

   `build-installer.ps1` generates this into `publish\` before the MSI harvest
   and reads the version back out of `WinCare.wxs`, so the two cannot drift. Do
   not hand-write it — the build wipes `publish\` on every run.

2. **The `url` endpoint serves the version.** That is `packaging\version.json`
   in this repo:

   ```json
   { "version": "1.0.0" }
   ```

   Bump that file when you ship a release.

The app never fetches `update-source.txt` — it reads `url` out of it and fetches
*that*. Pointing `url` at `github.com/…/releases/latest` will not work: it
returns an HTML page, and the checker requires JSON with a `version` key.

No server to run — raw.githubusercontent.com is the CDN.

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
