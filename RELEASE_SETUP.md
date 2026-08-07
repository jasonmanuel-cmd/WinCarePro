# WinCarePro Release Setup

## Build, package, and release

The full release flow is scripted — see [RELEASE_CHECKLIST.md](RELEASE_CHECKLIST.md)
for the ordered steps. Summary of what each script does:

- `build.ps1` — sets up `.venv`, installs pinned `requirements.txt`, runs `pytest`,
  builds `dist\WinCarePro.exe` via `WinCarePro.spec`, and smoke-launches it.
- `package.ps1` — requires `WINCAREPRO_SIGN_CERT_THUMBPRINT`, builds the bundled
  Guided Care bridge, publishes the self-contained accessible WPF shell, signs
  every executable, compiles and signs the installer, and writes `dist\update.json`.
- Both scripts reuse `release_tools.py` for signing (`signtool sign` with
  RFC3161 timestamping) and manifest generation — no separate signing logic
  lives in the PowerShell scripts.

## Checkout and licensing

1. Create the WinCarePro product in Gumroad and copy its public HTTPS checkout URL.
2. Set `WINCAREPRO_CHECKOUT_URL` for the packaged app.
3. Keep Gumroad product verification enabled; activation uses Gumroad's HTTPS API and fails closed.

## Secure automatic updates

1. Host the signed installer at a stable HTTPS URL (`WINCAREPRO_DOWNLOAD_URL`
   passed to `package.ps1`).
2. Set `WINCAREPRO_SIGNER_SUBJECT` to the exact subject on the code-signing certificate.
3. Run `package.ps1` (see above) — it signs and writes `dist\update.json` for you.
   To run the underlying step manually instead:

```powershell
$env:WINCAREPRO_SIGN_CERT_THUMBPRINT = "CERTIFICATE_THUMBPRINT"
python .\release_tools.py .\dist\WinCarePro-Setup-1.3.0.exe --version 1.3.0 --url "https://downloads.example.com/WinCarePro-Setup-1.3.0.exe"
```

4. Publish `dist\update.json` over HTTPS and set `WINCAREPRO_UPDATE_MANIFEST_URL` to that URL.
5. `updater.py`'s `install_and_relaunch()` runs the verified installer with
   `/VERYSILENT /SUPPRESSMSGBOXES /NORESTART` and exits the running app; the
   installer's own post-install step (`installer\WinCarePro.iss`) relaunches it.
6. Unsigned commercial packaging is refused. The updater also rejects unsigned,
   wrongly signed, or signer-mismatched installers.

## Clean Windows certification

Run `release\RunCleanVm.ps1`. It starts Windows Sandbox only when the feature is available. The sandbox test verifies the signature, launches the app, waits for the main window, records a pass line, and stops the process.

For commercial certification, repeat the same smoke test in a clean Windows 11 VM and record:

- Windows edition and build
- installer and uninstall result
- UAC prompt behavior
- first-run storage paths
- signature subject and status
- app launch result
- update check result

## Accessible interface

`WinCarePro.Desktop` is the native WPF accessibility shell. Build it with:

```powershell
dotnet build .\WinCarePro.Desktop\WinCarePro.Desktop.csproj -c Release
```

It targets the .NET 8 Windows Desktop LTS runtime and uses native Windows controls
exposed to UI Automation. The installer starts this shell and bundles the Python
feature engine and Guided Care bridge beside it. Safety Center and Undo Center
currently hand off to the legacy engine.
