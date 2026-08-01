# WinCarePro Release Checklist

Run in order. Do not skip a step to save time — each one exists because a
prior release found a way to break without it.

1. `git status` — working tree clean, on `main`, up to date with `origin/main`.
2. `./build.ps1` — installs pinned deps, runs `pytest`, builds `dist\WinCarePro.exe`,
   smoke-launches it and confirms the main window appears.
3. Set `$env:WINCAREPRO_SIGN_CERT_THUMBPRINT` if a code-signing certificate is
   configured. If not, confirm shipping unsigned is intentional for this release.
4. Set `$env:WINCAREPRO_DOWNLOAD_URL` to the HTTPS URL the installer will be
   hosted at once uploaded.
5. `./package.ps1` — signs the engine exe (if configured), compiles
   `dist\WinCarePro-Setup-<version>.exe` via Inno Setup, signs the installer,
   writes `dist\update.json`.
6. If signed: `signtool verify /pa /v dist\WinCarePro.exe` and the installer —
   confirm `Valid` and the expected publisher subject.
7. `release\RunCleanVm.ps1` (or `release\CleanVmSmoke.ps1` directly in a clean
   Windows 11 VM) — confirms a machine with none of this dev environment's
   state can launch the signed exe and see the main window.
8. Manually run `dist\WinCarePro-Setup-<version>.exe` once: confirm install,
   Start Menu shortcut, launch, and uninstall all work.
9. Upload the installer to the URL from step 4, then upload `dist\update.json`
   to `$env:WINCAREPRO_UPDATE_MANIFEST_URL`.
10. Bump the version literal in `WinCarePro.Desktop/WinCarePro.Desktop.csproj`,
    `README.md`, and `installer/WinCarePro.iss` for the next release.
