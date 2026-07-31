# WinCarePro Release Setup

## Checkout and licensing

1. Create the WinCarePro product in Gumroad and copy its public HTTPS checkout URL.
2. Set `WINCAREPRO_CHECKOUT_URL` for the packaged app.
3. Keep Gumroad product verification enabled; activation uses Gumroad's HTTPS API and fails closed.

## Secure automatic updates

1. Host the signed installer or executable at a stable HTTPS URL.
2. Set `WINCAREPRO_SIGNER_SUBJECT` to the exact subject on the code-signing certificate.
3. Sign and create the update manifest:

```powershell
$env:WINCAREPRO_SIGN_CERT_THUMBPRINT = "CERTIFICATE_THUMBPRINT"
python .\release_tools.py .\dist\WinCarePro.exe --version 1.3.0 --url "https://downloads.example.com/WinCarePro.exe"
```

4. Publish `dist\update.json` over HTTPS and set `WINCAREPRO_UPDATE_MANIFEST_URL` to that URL.
5. Do not publish an unsigned build. The updater requires both the manifest SHA-256 and a valid Authenticode signature from the configured publisher.

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

It targets the .NET 8 Windows Desktop LTS runtime and uses native Windows controls exposed to UI Automation. The current Python interface remains the production feature engine until every screen has moved to WPF.
