# Actual MSI lifecycle — November 24, 2026

## Outcome

**Actual install, isolated safe-mode launch, full-file repair and exact-product uninstall succeeded. Aggregate automated verification remains HOLD; do not claim a fully green release gate.**

- Corrected per-user install: Windows Installer exit **0**.
- Installed launch: responsive WPF window with Dashboard and Memory & Apps UI Automation elements; executable version **2.0.0.0**, product version **2.0.0**; graceful close and application exit **0**.
- Repair `/fa` against the exact new ProductCode: exit **0**, **7 actual FileCopy operations**, and all seven installed hashes matched the reviewed payload afterward.
- Exact-product uninstall: exit **0**. Readback: product state **-1**, no related/registered WinCare product, no uninstall entry, no application registration, installation folder or Start Menu shortcut folder. No WinCare process remained.
- Existing `%LOCALAPPDATA%\WinCarePro` data: **3 files preserved byte-for-byte** through installation, launch, repair and removal.
- Final verification reached and passed comparison of **1,750 pre-existing repository files**, with no changed or missing baseline file, before failing a subsequent log assertion. No product source or existing artifact was changed. This report is this task's sole repository addition.

The aggregate verifier failed initially on an over-specific ARP hive assumption, then on Windows PowerShell JSON containing an unescaped control character, then after the second correction on an exact reboot-property log assertion. The two-correction limit was respected: no further aggregate rerun or false PASS receipt. These are harness/verification issues, not observed application crashes. Repair was invoked with `/norestart` and `REBOOT=ReallySuppress`, but its log does **not** contain the exact `Property(S): REBOOT = ReallySuppress` line. Install and uninstall logs do contain it. Repair reboot-suppression propagation is therefore not established by that log check; no reboot occurred.

## Scope, identity and collision safety

The current user explicitly authorized this lifecycle on the spare desktop, including the installer's own registration/shortcuts and subsequent removal. Backup and recovery capability remain unverified. No self-elevation, UAC approval, signing, certificate/key operation, download, unrelated install/uninstall, system-setting change, Defender remediation, reboot, publication or git mutation was performed.

Read the current `HOST-NATIVE-TEST-RESULTS.md`, `HOST-TEST-PREFLIGHT.md`, `GEMINI.md`, `installer/WinCare.wxs`, `build-installer.ps1`, app startup/window composition, safety/data-root and instance isolation code, scheduler initialization and read-only diagnostic reader. The existing MSI was rehashed and its actual tables freshly opened read-only through `msi.dll`; it was not rebuilt or replaced.

```text
Repository R:
C:/Users/blunt/Desktop/wincare-desktop

Evidence root S:
C:/Users/blunt/AppData/Local/hermes/cache/scratch/wincare-lifecycle-aab4bcc894434332ac66672eba03e9b8

Candidate M:
C:/Users/blunt/AppData/Local/hermes/cache/scratch/wincare-native-363d0f35aa5d40dc98120761b055936b/msi candidate/WinCare.msi

MSI SHA256:
aac88f6cdfde7cbe4784f683cb9ec488d3050db4c62d2245c75e4e64af897d8a

ProductCode P:
{66CFF114-90ED-4A21-B432-87ABED1DE127}
UpgradeCode:
{7B2F4A19-3C5E-4D81-9A6B-2E8C4F1D7A30}
ProductName / ProductVersion:
WinCare Pro / 2.0.0

Reviewed and installed executable SHA256:
3e082629060d9580b74e34fdfe996e105bc5dabcb64fec6ff940bb3026db1859
```

MSI review: zero custom actions; seven payload File rows; per-user source authoring and absent ALLUSERS property; standard install/remove/registry/shortcut actions; MajorUpgrade removal identified. `msi-tables.json` also retains Component, RemoveFile, Upgrade, LaunchCondition and UI/execute sequences. No previous product was permitted to enter the upgrade path.

Preflight at **22:16:20 PST** found a non-elevated token, no WinCare processes, product state -1, no related products from the UpgradeCode, no matching visible MSI products, and no matching uninstall entries in HKCU/HKLM 32/64-bit views. The per-user install and shortcut targets, both Program Files targets, common Start Menu target, and user/public desktop shortcuts were absent. No `Win32_Product` query or broad repair was used. Existing user data was baselined, not overwritten or used by the launch. These checks cover the current-user/machine-visible product scope, not every offline Windows profile.

## Actual commands and results

`S`, `M` and `P` below expand to the exact values above. Full native argv, cwd, timestamps, stdout/stderr and child exit status are in each named JSON record. Native commands used `C:\Windows\System32\msiexec.exe`; child cwd was S. The harness explicitly redirected child TEMP/TMP/TMPDIR to `S/temp`. Windows Installer service-owned caches/bookkeeping and its independently resolved temporary locations were not redirected or manually cleaned.

| Operation | Time, PST, November 24 | Exit | Evidence |
|---|---|---:|---|
| Initial malformed harness install | 22:16:38 | 1603 | `initial-install.json`, `initial-install.log`, `initial-install-after.json` |
| Corrected install | 22:17:11–22:17:14 | 0 | `install.json`, `install.log`, `install-after.json` |
| Initial prelaunch shortcut assertion | 22:17:57 | 1 | `initial-launch.json`; app not started |
| Installed safe-mode launch and close | 22:18:24–22:18:32 | 0 | `launch.json`, `launch-before.json`, `launch-after.json` |
| Repair | 22:18:41–22:18:42 | 0 | `repair.json`, `repair.log`, `repair-verified.json` |
| Uninstall | 22:18:44–22:18:45 | 0 | `uninstall.json`, `uninstall.log`, `uninstall-after.json` |
| Final independent process readback | 22:21:00 | 0 | `observed-results.json`; empty process output |

```text
# Corrected install; package scope was explicitly inspected before execution.
msiexec.exe /i "M" MSIINSTALLPERUSER=1 /qn /norestart REBOOT=ReallySuppress MSIRESTARTMANAGERCONTROL=Disable /l*v "S/install.log"

# Repair all files of ONLY the newly installed exact product.
msiexec.exe /fa P /qn /norestart REBOOT=ReallySuppress MSIRESTARTMANAGERCONTROL=Disable /l*v "S/repair.log"

# Remove ONLY that exact product after checking its ownership and target.
msiexec.exe /x P /qn /norestart REBOOT=ReallySuppress MSIRESTARTMANAGERCONTROL=Disable /l*v "S/uninstall.log"
```

The successful logs record `Assignment=0`; all three return 0. Payload and shortcut destinations are per-user, and the source requests no elevation. The observed ARP entry was nevertheless in **HKLM's 64-bit uninstall view**, while the application's authored registration was HKCU. The report preserves that fact rather than claiming all registration was HKCU. Both registrations were absent after removal. The machine reported `MsiSystemRebootPending=1` already in the initial attempt; this task did not act on it or establish its cause.

### Launch isolation and identity

```text
powershell.exe -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "S/launch.ps1"
```

The launched process was exactly `%LOCALAPPDATA%\WinCare Pro\WinCare Pro.exe --safe-mode`, with process-local:

```text
WINCARE_SAFE_MODE=1
WINCARE_DATA_ROOT=S/isolated-data
DOTNET_BUNDLE_EXTRACT_BASE_DIR=S/bundle-extract
TEMP=TMP=TMPDIR=S/temp
```

Before starting it, the harness compared the whole installed file inventory with the reviewed seven-file input and checked the executable hash. The actual shortcut targeted this exact executable, had empty arguments, and used the installation directory as its working directory. The shortcut was inspected through WScript.Shell but **not launched in normal mode**.

PID **2824** produced window handle **198568**, title `WinCare Pro - System Optimizer & Health Suite`, `Responding=true`, expected navigation labels, and a completed read-only guidance message. No maintenance button was invoked. `CloseMainWindow=true`, exit within 15 seconds, app exit 0. UI Automation smoke evidence is not a screenshot review, accessibility audit or comprehensive interactive test.

### Repair scope

This was a real `/fa` full-file repair, not just an MSI state query. `repair.log` contains seven executed FileCopy operations; `repair-verified.json` proves all resulting payload hashes equal the original reviewed input and user data remains unchanged. No existing file was deliberately damaged or deleted to manufacture a repair condition. Missing-file recovery, shortcut reconstruction after deliberate deletion, upgrade and downgrade are not covered.

## Preservation and readback

`final-host.json`, collected **22:20:18 PST**, equals the preflight target/data snapshot apart from timestamp. Uninstall removed the exact test product, its application key, ARP entry, installed files/directory and shortcut/directory. Shared Windows Installer caching/event bookkeeping was not exhaustively inventoried; no claim of zero host-wide writes is made.

Existing data hashes stayed:

```text
environment.txt  2b2277efd3f9cf62598059ed1a2dbbe11df15eec2cbd09dbc6707b1e3a6520e0
history.json     9c8d1a25140ddc0cd3bffe396c7c4fef70271b6bb6a82b34d358c43301dc7a16
schedule.json    1d22a899a83e1fc38ef2ff0e9b6426a8f33684ed9e8e5ff0cd8135b809e1c3c6
```

The last aggregate run completed its target restoration, installed/repair payload, existing-data, launch, original MSI/input hashes and 1,750-file repository-preservation assertions before reaching the failing repair-log assertion. It exited **1**, not 0. Final observations were recorded separately, without another correction/rerun, in `observed-results.json` using `S/record-observations.py` (exit 0). That record is not a successful aggregate verifier receipt.

## Harness issues and retained evidence

1. Initial root selection trusted inherited TMPDIR, which actually resolved to `C:/Users/blunt/AppData/Local/Temp`. Only read-only preflight evidence and empty scratch folders were created there. They were moved intact to `C:/Users/blunt/AppData/Local/hermes/cache/scratch/wincare-lifecycle-preflight-initial-ff165efe0f9442f7b28f8fecc63ee13d`; root selection was made explicit and preflight rerun before installation. No user's temporary files were removed.
2. Initial command included bare `ALLUSERS=` followed by `MSIINSTALLPERUSER=1`. The MSI log proves the next property was consumed as the value, producing ALLUSERS=1 and error 1925/exit 1603. Readback proved no installed product, payload, shortcut or user-data change. The harness removed only the erroneous empty override; the unchanged per-user package then installed without elevation. Initial failed evidence is retained, not overwritten.
3. The first launch wrapper compared a directory with and without a trailing backslash and stopped before starting the app. Read-only shortcut inspection proved the same directory; normalization fixed only the harness comparison.
4. Aggregate verification first wrongly required HKCU ARP. It was corrected to check actual product/version and MSI Assignment=0 instead. The next run encountered a raw control character in Windows PowerShell's UI Automation JSON; parsing was corrected with `strict=False`. The final run then failed the exact repair reboot-property line assertion. No third correction/rerun was attempted. All failures are introduced harness assumptions or unresolved verification interpretation, not attributed to product-source changes.

The reusable execution harness is `C:/Users/blunt/AppData/Local/hermes/cache/scratch/wincare-lifecycle.py`; all generated receipts and native logs are retained. No product source fix was warranted by these observations. Historical 303 application and 44 inert packaging tests were not rerun or represented as fresh results.

## Remaining limits

The named native lifecycle operations were actually exercised and the installation was removed. **Overall HOLD / NO_SHIP remains**, including the unresolved aggregate verification assertion. No upgrade/downgrade fixtures, MSIX install/trust, signing/timestamp chain, normal-mode system action, recovery proof, supported-host matrix, or publication is implied. GitHub investigation belongs to the separate delegated lane and is not claimed here.
