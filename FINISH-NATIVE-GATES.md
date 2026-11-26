# WinCare native validation and distribution gates

## Verdict and scope

**HOLD / NO_SHIP. No usable disposable Windows environment was verified.** Read-only discovery on November 24, 2026; observed host clock `2026-11-24T09:07:06.4917756-08:00`. The reviewed candidate remains `artifacts/rescue-reviewed-portable/WinCare Pro.exe`, not `publish/` or the existing MSI/MSIX.

Only this report was created in the repository. No builds, installations, feature changes, elevation, scans/remediation, app closures, registry/network/scheduler changes, signing, certificate import/export, package downloads, commits, or publishing were performed. Existing files were retained. PowerShell was invoked noninteractively with `-NoLogo -NoProfile -NonInteractive`; inspection did not execute the repository's scripts. Signature verification can use Windows trust infrastructure; it was not a signing operation.

Read sources: `RESCUE-BLOCKER-FIXES.md` (authoritative current HOLD), `RESCUE-FINAL-REVIEW.md` and `RESCUE-ACTIONS-RESULTS.md` relevant findings, `COMPLETION-WORK.md`, `FINISH-UP-RESULTS.md:82-87`, `RELEASE.md`, `STORE.md`, `packaging/release-github.md`, installer/signing/release scripts, `WinCareDesktop.csproj`, WiX/MSIX manifests, version and winget installer metadata. Historical test/build claims were not rerun or promoted to fresh evidence.

## 1. Observed host capabilities — not an isolation approval

Commands below are read-only discovery commands, not setup instructions. Working directory: `C:/Users/blunt/Desktop/wincare-desktop`.

### Host and token

```powershell
Get-CimInstance Win32_OperatingSystem | Select-Object Caption,Version,BuildNumber
Get-CimInstance Win32_ComputerSystem | Select-Object Manufacturer,Model,HypervisorPresent
Get-CimInstance Win32_Processor | Select-Object Name,VirtualizationFirmwareEnabled,SecondLevelAddressTranslationExtensions,VMMonitorModeExtensions
([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
```

Exact returned values:

```text
Caption=Microsoft Windows 11 Pro; Version=10.0.26300; BuildNumber=26300
Manufacturer=HP; Model=HP Pro Tower 290 G9 Desktop PC; HypervisorPresent=True
Name=13th Gen Intel(R) Core(TM) i3-13100
VirtualizationFirmwareEnabled=False
SecondLevelAddressTranslationExtensions=False
VMMonitorModeExtensions=False
Administrator token=False
```

A present hypervisor is not evidence of an available Windows guest. The processor flags do not establish firmware incapability, particularly alongside `HypervisorPresent=True`; no firmware changes are requested on this evidence.

### Windows features and VM discovery

`Get-WindowsOptionalFeature -Online` failed with **`The requested operation requires elevation.`** No elevation was attempted. The non-elevated alternate succeeded:

```powershell
Get-CimInstance Win32_OptionalFeature |
  Where-Object Name -Match 'Containers-DisposableClientVM|Microsoft-Hyper-V|VirtualMachinePlatform|HypervisorPlatform' |
  Select-Object Name,InstallState
```

Exact rows:

```text
VirtualMachinePlatform                    1
HypervisorPlatform                        2
Microsoft-Hyper-V-All                     2
Microsoft-Hyper-V                         2
Microsoft-Hyper-V-Tools-All                2
Microsoft-Hyper-V-Management-PowerShell    2
Microsoft-Hyper-V-Hypervisor               2
Microsoft-Hyper-V-Services                 2
Microsoft-Hyper-V-Management-Clients       2
Containers-DisposableClientVM             2
```

Microsoft's Win32_OptionalFeature definition maps 1 to Enabled and 2 to Disabled. Thus this query reports Virtual Machine Platform enabled, but Windows Sandbox and the listed Hyper-V features disabled. This is feature inventory, not a launch test or reboot-pending diagnosis.

Other exact discovery outcomes:

- `Get-Command WindowsSandbox.exe,vmconnect.exe,Get-VM,VBoxManage.exe,vmrun.exe,makeappx.exe,signtool.exe,wix.exe -ErrorAction SilentlyContinue` returned only `wix.exe`, source `C:\Users\blunt\.dotnet\tools\wix.exe`. Missing PATH commands are not proof of no installation elsewhere.
- `Get-Service vmms,vmcompute,HvHost -ErrorAction SilentlyContinue` returned `HvHost` and `vmcompute`, each JSON `Status=4, StartType=3` (Running, Manual); no `vmms` row.
- `Get-VM unavailable`.
- `Get-CimInstance -Namespace root/virtualization/v2 -ClassName Msvm_ComputerSystem -ErrorAction Stop` returned `Invalid namespace`.
- `Test-Path C:\Windows\System32\WindowsSandbox.exe` = False.
- `Test-Path C:\Program Files\Oracle\VirtualBox\VBoxManage.exe` = False.
- `Test-Path C:\Program Files (x86)\VMware\VMware Workstation\vmrun.exe` = False.
- `Test-Path C:\Users\blunt\VirtualBox VMs` = False.
- `Test-Path C:\Users\blunt\Documents\Virtual Machines` = False.
- `Test-Path C:\ProgramData\Microsoft\Windows\Hyper-V` = True, but listing returned `Access to the path 'C:\ProgramData\Microsoft\Windows\Hyper-V' is denied.` Its existence does not verify guest disks/configuration/checkpoints.
- Both `C:\Program Files\VideoLAN\VLC\vlc.exe` and `C:\Program Files (x86)\VideoLAN\VLC\vlc.exe` returned Exists=False.

Discovery was bounded to commands, providers and conventional paths above; it did not search all disks, enumerate remote infrastructure, or certify that no VM exists anywhere. No safe guest path, guest identity, baseline checkpoint, restoration procedure or guest administrator access is established.

## 2. Existing packaging/signing capability and artifacts

### Tools and credential boundary

Files exist in the local cache:

```text
C:\Users\blunt\.nuget\packages\microsoft.windows.sdk.buildtools\10.0.29648.1000-preview\bin\10.0.29648.0\x64\makeappx.exe
C:\Users\blunt\.nuget\packages\microsoft.windows.sdk.buildtools\10.0.29648.1000-preview\bin\10.0.29648.0\x64\signtool.exe
```

Equivalent arm64/x86 paths were also found. No matching tools were returned from `C:\Program Files (x86)\Windows Kits\10\bin`. WiX was discovered but not run. Cached preview tools are availability evidence, not an approved release toolchain; `makeappx` packaging was not exercised.

Presence-only environment checks returned False for all of `WINCARE_SIGN_PFX`, `WINCARE_SIGN_PWD`, `WINCARE_MSX_PUBLISHER`, `WINCARE_MSX_PUBLISHER_DISPLAY`. This concerns the current process environment, not every possible signing service/account.

Read-only `Cert:\CurrentUser\My` code-signing metadata returned two entries: Subject/Issuer both `CN=WinCare MSIX`, and Subject/Issuer both `CN=WinCare Pro`; each HasPrivateKey=True. No key material or passwords were read/exported. Matching self-issued subjects and private-key availability do not establish publicly trusted signing capability. No approved production credential or Partner Center identity was verified.

### Fresh artifact measurements

Measured with `Get-FileHash -Algorithm SHA256`, `Get-Item` and `Get-AuthenticodeSignature`:

| Relative path | Bytes | SHA-256 | Signature status |
|---|---:|---|---|
| `artifacts/rescue-reviewed-portable/WinCare Pro.exe` | 137703146 | `3E082629060D9580B74E34FDFE996E105BC5DABCB64FEC6FF940BB3026DB1859` | NotSigned |
| `installer/WinCare.msi` | 60497920 | `FBA97CAF2C8507C50E883451C0FD9D86D120CC191A92AE7E7880C76C486D5CB7` | NotSigned |
| `installer/WinCare.msix` | 61833906 | `C9C1273FBD3B86DCBFCBE4F2F06D5B4D6CEDDA732CB65D2A65C2FF8C6F7C0730` | UnknownError |
| `publish/WinCare Pro.exe` | 137429155 | `D9A3A67E2AC74149C5BD40AB95235F8A3466903A5E87F7497D7A69535DAFCAC3` | NotSigned |

Both executable file versions are `2.0.0.0`; hashes prove they are not the same candidate. Reviewed portable files observed: `D3DCompiler_47_cor3.dll`, `PenImc_cor3.dll`, `PresentationNative_cor3.dll`, `WinCare Pro.exe`, `WinCare Pro.pdb`, `vcruntime140_cor3.dll`, `wpfgfx_cor3.dll`. Keep native sidecars; no `update-source.txt` was present there.

Read-only ZIP inspection of the existing MSIX found:

- `AppxSignature.p7x` **present**, contrary to the historical unsigned-MSIX description in `STORE.md`.
- Identity `WinCarePro.WinCareDesktop`, Publisher `CN=WinCare Pro`, Version `1.0.0.0`, architecture x64.
- Executable entry `WinCare%20Pro.exe`, SHA-256 `d9a3a67e2ac74149c5bd40ab95235f8a3466903a5e87f7497d7a69535dafcac3`: matches old `publish/`, not the reviewed rescue candidate. The initial exact-name ZIP read failed with KeyError for `WinCare Pro.exe`; enumerating the encoded entry resolved the inspection, without modifying/extracting the archive.

Explicit read-only verification:

```bash
'C:/Users/blunt/.nuget/packages/microsoft.windows.sdk.buildtools/10.0.29648.1000-preview/bin/10.0.29648.0/x64/signtool.exe' verify /pa /v 'installer/WinCare.msix'
```

Exit **1**. Exact decisive output:

```text
Issued to: WinCare Pro
Issued by: WinCare Pro
Expires: Sat Oct 14 20:54:27 2028
The signature is timestamped: Wed Oct 14 20:54:33 2026
Number of files successfully Verified: 0
Number of warnings: 0
Number of errors: 1
SignTool Error: A certificate chain processed, but terminated in a root
    certificate which is not trusted by the trust provider.
```

It is signed but not trusted by this verification policy; signature-entry presence is not proof of installability. No host trust-root import was attempted. MSI payload/source parity was not established; the MSI was neither installed nor administratively extracted.

### Script/configuration blockers before distribution

1. **Wrong payload risk:** `build-installer.ps1:80-89` deletes/recreates `publish/`; `build-msix.ps1:75-87,103-106` also republish/stage afresh. They do not consume the reviewed portable artifact. A future rebuild creates a new candidate requiring explicit source/input identity, hashes and appropriate revalidation. Never relabel current installers as the reviewed candidate.
2. **Signing order defect:** `sign-release.ps1:88-98` selects the MSI first, then `publish/*.exe`, after packaging. Signing the external EXE does not update the embedded MSI payload. Required pipeline: approved publish -> sign/timestamp executable payload -> package that exact signed payload -> sign/timestamp installer -> verify outer signature AND extracted payload signatures/hashes. Merely rerunning `build-installer.ps1` after signing would republish and defeat that sequence.
3. **Verification does not fail the signing script:** `sign-release.ps1:164-179` prints NOT VALID after verification failure but does not throw/set a failure exit there. Release automation must fail closed.
4. **MSIX safety/failure gates:** `build-msix.ps1:161-181` can create/export a self-signed credential automatically; signing failure at 192-207 does not abort packaging, and 271-275 equates signature-file presence with installability. Do not run this script unchanged as a production signing gate or on the preserved host.
5. **Version/identity drift:** project, WiX source and `packaging/version.json` say 2.0.0; the existing MSIX says 1.0.0.0. MSIX default version discovery checks a DLL that may not exist in single-file output and falls back to 1.0.0.0 (`build-msix.ps1:90-99`). Source MSIX publisher is PLACEHOLDER. Explicitly set approved package identity/version and verify final package metadata.
6. **winget mismatch:** `packaging/winget/WinCare.WinCarePro.installer.yaml` declares 1.0.0, `Scope: machine`, old release URL and SHA-256 `4DE72C1BFC55C980814DFF30054D1E3CBD85B9ADC09626854D237A753DA835BC`. WiX source declares `Scope="perUser"`, version 2.0.0; the existing MSI hash differs. Regenerate/check metadata against the final signed installer; do not publish existing manifests unchanged.
7. **Release script bypass:** `release-to-github.ps1` builds then tags/pushes/uploads without a signing gate. Its `git diff --quiet` alone does not establish a fully clean reviewed source snapshot. Do not run it here.
8. **Docs are historical/conflicting:** RELEASE describes an old MSI name/version and test count; release-github permits unsigned freeware while RELEASE calls signing non-negotiable; STORE's unsigned-MSIX claim is contradicted above. MSIX MinVersion is `10.0.19041.0`, while other distribution text/manifests reference 17763. Align channel-specific support claims and signing policy. Pricing/reputation/Store assertions in old docs were not independently certified by this audit.

## 3. Safe next execution path — requires explicit approval

**User prerequisite:** identify/provide an existing disposable Windows VM (local or remote) with an exact guest identifier/path and owner-approved reset capability, OR separately authorize an administrator to provision one. Current task does not authorize enabling features, rebooting, creating guests, installing hypervisors, acquiring images, or buying signing services. A separate Windows test machine with a restorable disk image is another possible operator-provided target.

Preferred future test arrangement: licensed clean Windows guest(s) covering the declared supported builds, snapshot/checkpoint baseline, local standard/admin test accounts, guest-only disks/data, and a private test network with disposable DNS fixtures. Before any mutation, record guest identity, OS, checkpoint ID/time, disk locations and restoration verification; ensure no writable host folders, production network adapters, shared credentials or user data are exposed. Import the reviewed complete artifact through a controlled read-only handoff and verify its hash inside the guest. Copy into guest-local storage before testing. Guest normal mode is required for native action tests; safe mode cannot discharge those gates.

If the user elects Windows Sandbox later, configure networking and clipboard redirection disabled and any minimal input mapping read-only. Microsoft documents those controls; default Sandbox settings are not the proposed safety boundary. Sandbox was not available in this discovery. Do not count a future Sandbox launch alone as proof of Defender/provider parity, managed-policy coverage, persistent reboot/upgrade behavior, or checkpoint rollback. A persistent disposable VM is the preferred target for these gates.

Official read-only reference checks (not setup execution): Microsoft Learn, **Win32_OptionalFeature class** (InstallState mapping), and **Use and configure Windows Sandbox** (networking, clipboard, read-only mappings), retrieved during this audit. References:

```text
https://learn.microsoft.com/en-us/windows/win32/cimwin32prov/win32-optionalfeature
https://learn.microsoft.com/en-us/windows/security/application-security/application-isolation/windows-sandbox/windows-sandbox-configure-using-wsb-file
```

## 4. Exact remaining native acceptance matrix

Every row below is **NOT RUN / OPEN** in this task. For each future case record artifact hash, guest/build/account/policy, fixture/baseline, exact steps, expected vs actual UI/native outcome, logs, before/after state and reset evidence. Restore the baseline between destructive scenarios. A failed case blocks its affected capability; missing evidence is not a pass.

| Gate | Required cases and acceptance evidence | Prerequisite |
|---|---|---|
| Defender Quick/Full | Confirm named consent/decline; actual Quick and Full initiation; truthful running/completed/failed/cancelled/unknown states; leaving view must not imply cancellation of an initiated scan. Capture provider event XML, Scan ID/user/time and request/refresh logs, including wrapper duration/timeout behavior. | Disposable guest with active Defender, guest admin/standard accounts; explicit scan authorization acknowledging policy remediation. |
| Defender availability/correlation | Standard-user denial; passive/disabled/managed policy; localized event fields; existing/concurrent scans; delayed/missing events; truncated history (including 257-event bound); clear/reset/rollover and watermark regression; long-running/eight-hour wrapper behavior. Missing/ambiguous history must block or remain unknown, never clean/completed by inference. | Separate controlled guest policy/language fixtures; approved event manipulation in guest only; time budget. |
| Defender detection/remediation handoff | Provider schema/permission failures, empty vs unavailable history; Windows Security Protection history accessibility and supported remediation/restore outcomes. Empty history is not malware-free. No live malware required by this plan; any benign security-test fixture needs separate approval. | Suitable disposable provider fixtures and operator-approved remediation scenario. |
| Verified VLC | Genuine approved VideoLAN installer in standard Program Files location; metadata, machine registration and signature acceptance across supported architectures/versions. Test unsigned/portable/nonstandard/reparse/renamed image rejection, certificate failures, wrong user/session, elevated/critical rejection, PID/start/image/HWND churn, graceful refusal/save prompts, delayed exit and restart. Never force-close. | User-provided or explicitly approved official installer with provenance/hash/signature verification; guest-only installation. Neither standard host path currently contains VLC. |
| Cleanup/recycle/maintenance | Preview decline leaves state unchanged; confirmed file snapshot only; changed/locked/denied targets, traversal/reparse/TOCTOU, protected roots, multiple volumes and non-recyclable targets; COM recycle never silently falls back to permanent deletion. Measure actual successful bytes, partial/error UI, cancellation and bounds. | Disposable guest disks/files/ACL fixtures; no host mappings writable. |
| Uninstall | Disposable desktop MSI/vendor bootstrap and Store fixtures; standard/admin UAC allow/deny, nonzero/unknown results, reboot/pending states and verified removal. History must not promise undo. | Approved benign disposable apps and restartable guest. |
| Startup | Run-entry and startup-file disable/restore, permissions, existing disabled targets, crash between journal/write/readback, external changes after action. Persist recovery before mutation; do not overwrite a later user/admin change. | Guest-only HKCU/HKLM/startup fixtures and controlled crash points. |
| Privacy | Missing/explicit/managed/denied registry states, apply/readback mismatch, durable history failure, crash windows, actual undo and conflict refusal. Confirm only supported settings are represented. | Disposable guest registry/policy fixtures and baseline values. |
| DNS | Multiple adapters with stable identities, disconnected/removed adapters, IPv4 automatic vs ordered static servers, permission/policy failures, partial application, actual undo after restart and concurrent changes. IPv6 unchanged; original mode/order restored or failure truthfully surfaced. | Isolated guest network and multiple guest virtual adapters; no production DNS. |
| Scheduler | New explicit opt-in only; old schedules inert; app-open-only/temp-only behavior; no OS task registration; DST/backward/forward time, corrupt/unwritable config, crash/retry/concurrent runs, file/byte/time bounds and no catch-up storm. Check disable/cancel behavior and accurate failure reporting; cooperative limits are not a hard I/O deadline. | Guest temp fixtures, controllable clock/config and isolated persistence; OS task inventory before/after. |
| Accessibility/UI | All panels and loading/empty/error/partial/busy/confirmation states; Tab/Shift-Tab traversal, visible focus, keyboard accept/decline/Escape, modal focus trap/return, screen-reader names/state/live announcements; hover, high contrast, 100/125/150/200% DPI and mixed-DPI movement; full scrolling at 1180x760 and 980x650. Check second-instance focus, close/process exit, crash log. | Interactive test operator, Narrator/screen reader, display scaling/contrast fixtures. Safe mode can cover nonmutating UI but not normal-mode consent/action semantics. |
| Installer lifecycle | Fresh per-user install without unintended elevation; shortcuts/ARP/version; complete native payload hashes; launch without preinstalled .NET; repair, previous-version upgrade, same-version behavior, downgrade refusal, uninstall, reboot/pending failures and preserved undo history. MSIX separately: identity/trust/capabilities, actual data paths, update-source inclusion, upgrade/uninstall data lifetime. | Fresh resettable guest(s), approved final packages and previous-version fixtures; trusted test signing only inside guest if needed. |
| Release/security | Approved runtime/dependency vulnerability and provenance assessment, immutable source/input manifest, isolated full tests/build, signed payload and package verification, timestamps/chain/revocation-policy evidence, final hashes/SBOM as applicable, matching versions/winget metadata, accurate docs/support/privacy/license and explicit release approval. | Owner-selected channel and supported OS matrix; approved signing identity/service or Partner Center identity; authorized network checks. No credentials should be pasted into chat. |

## 5. Completion decision

This task completed **discovery and gate documentation**, not native validation. Current portable hash matches the report's reviewed candidate; it remains unsigned. Existing distribution artifacts are not releasable evidence for that candidate. The current MSIX is an older payload with a self-issued signature whose trust verification fails, not simply an unsigned file.

The immediate blocker for native action execution is a verified disposable guest plus explicit authorization for the enumerated guest-only mutations. Distribution additionally needs pipeline fixes, channel/signing decisions and final-package validation. Do not weaken host Defender, import test roots on the host, clear host event logs, elevate this audit, or substitute host tests to get green results.
