# WinCare Pro — session handoff (Tue Oct 06 2026, ~00:05)

## Where we stopped

**Completed:**
- Cipher mode loaded into long-term memory: `C:\Users\blunt\cow\MEMORY.md` (new "🔐 CIPHER MODE" section, v1 Oct 2026).
- WinCare Pro release prep executed and verified on tonight's build:
  - `build-release.ps1` — clean build, 0 warnings, **116/116 tests passing**, self-contained `WinCare Pro.exe` (131 MB publish output).
  - `build-installer.ps1` — WiX 5.0.2, `installer\WinCare.msi` + `cab1.cab` rebuilt; contents verified; MSI unsigned (documented in RELEASE.md §1).
  - RenderShots pass — `dotnet run --project Tools\RenderShots -c Release -- "$env:TEMP\shots"`; all 12 panel PNGs wrote to `C:\Users\blunt\AppData\Local\Temp\shots`; 11/11 cards define hover styles.
- Release plumbing committed (`5288cc3`): `release-to-github.ps1`, `packaging\release-github.md`, winget manifest skeleton `packaging\winget\WinCare.WinCarePro.yaml`, `packaging\update-source.sample.txt`.
- GitHub repo created: https://github.com/jasonmanuel-cmd/WinCarePro (public).

**Blocked:**
- `git push -u origin master` fails: token lacks `workflow` scope (repo has `.github/workflows/ci.yml`).
- Attempted non-interactive `gh auth refresh` — could not complete device flow; last attempt is may be sitting dead in a background shell (`sh_1101136d5001t31ifLZ3Q3MugR`, code E472-1735 — treat as expired after interruption).
- Decision point: user chose option **1** (PAT with `workflow` scope). PAT was not yet pasted — when ready, run: `gh auth login --with-token < <file>` then push. Alternative remains deleting `.github/workflows/ci.yml` and pushing without it.

**Outstanding release steps (once push works):**
```powershell
cd C:\Users\blunt\Desktop\wincare-desktop
git push -u origin master
.\release-to-github.ps1 -Version 1.0.0   # tags v1.0.0, creates GH release, MSI + exe zip, prints winget SHA256
```
Then drop `update-source.txt` next to the exe, and submit the three winget manifest files to `microsoft/winget-pkgs` under `manifests/w/WinCare/WinCarePro/1.0.0/` with the SHA256 from the release script output.

**Known shipping caveat:** unsigned MSI/exe → SmartScreen warning on first install. Acceptable for v1.0 freeware; EV cert (~$250–500/yr) before any paid push.

**Other context:** station sync job today had Property Radar export erroring on "out of managed credits"; Oct 2 export still on disk. Harbison-Standard2 perf commit from Oct 5 10:08 left one untracked Lighthouse report artifact in the repo root.
