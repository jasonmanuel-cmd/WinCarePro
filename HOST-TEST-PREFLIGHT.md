# Spare-host test preflight — November 24, 2026

## Outcome

**Bounded host checks completed; overall HOLD / NO_SHIP remains.** The user authorized this spare desktop as a test machine. That supersedes the earlier VM-only restriction for non-destructive inspection and isolated packaging checks; it does not authorize arbitrary host changes. No VM was required for the checks below.

Fresh observations: real MakeAppx pack/unpack through the current `build-msix.ps1` passed, with byte-identical payload readback. WiX 5 build mode accepted the existing trailing **`-nologo`**, producing a WiX library. This proves build-subcommand option acceptance, not complete MSI production or installation. No source edits were made.

## Sources, boundaries and environment

Read `GEMINI.md`, `FINISH-NATIVE-GATES.md`, `FINISH-PACKAGING-FOLLOWUP.md`, both current packaging entry scripts, all of `packaging/Packaging.Common.ps1`, both installer manifests and the project file before execution. Root `AGENTS.md` and `CLAUDE.md` were absent; `C:/Users/blunt/AGENTS.md` and `C:/Users/blunt/Desktop/AGENTS.md` were also absent. GEMINI's historical `build-installer.ps1 -SkipTests` command no longer matches the current mandatory parameters and was not used. Prior reports' native restrictions are historical context, not a reason to refuse this newly authorized bounded host testing.

Repository: `C:/Users/blunt/Desktop/wincare-desktop`.

All test inputs, temporary files, outputs, harnesses and logs are under the newly created directory:

```text
C:/Users/blunt/AppData/Local/hermes/cache/scratch/wincare-host-preflight-33f10bad12274b3da2244451246a08a7
```

Below, `$S` means that exact directory. Child-process `TEMP`, `TMP` and `TMPDIR` were explicitly set to `$S/temp`; packaging working directory was `$S`. The sole repository addition is this requested report. No existing fixtures were cleaned up.

Safety review:

- MSIX unsigned-candidate path checks version and payload hashes, copies input, generates manifest/assets, calls MakeAppx pack/unpack, verifies extracted bytes/identity and writes a HOLD receipt. It does not install, sign, discover credentials or create certificates. Scratch input is a copy of the complete reviewed portable; its executable was never launched.
- `build-installer.ps1:27` unconditionally invokes `msiexec /a`. **The MSI entry script was not executed.** WiX was instead called via the already reviewed `Invoke-CheckedTool` helper, using scratch source and library output. No MSI, administrative image, validation/custom-action execution or installer lifecycle was requested.
- Test-only publisher/identity strings do not claim owner approval for a release identity. No production signer or Store identity was selected.
- No elevation, installation/uninstallation, download, signing/key export, certificate import, DNS/registry/security/startup change, reboot, process termination, remediation scan, release, commit or push was performed. PowerShell execution policy was scoped to the launched process with `-ExecutionPolicy Bypass`, not changed persistently.

## Read-only preflight

Observed host clock: **2026-11-24T18:01:02.1445480-08:00**. Commands ran noninteractively with `powershell.exe -NoLogo -NoProfile -NonInteractive -Command` from the repository.

| Check / command | Actual result | Meaning |
|---|---|---|
| `([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)` | `False` | Current token is not elevated. This is not a claim about underlying account group membership. |
| `Get-CimInstance Win32_OperatingSystem` selecting Caption, Version, BuildNumber | Microsoft Windows 11 Pro; 10.0.26300; 26300 | Current host OS, not supported-OS matrix coverage. |
| `Get-CimInstance Win32_LogicalDisk -Filter "DriveType=3"` selecting DeviceID, Size, FreeSpace | C: size `998287339520`, free `533281476608` bytes | Available capacity was sufficient for the bounded copied-payload smoke checks. |
| `Get-ComputerRestorePoint -ErrorAction Stop` | `Access denied` | Restore-point availability unknown; not evidence that none exist. |
| `Get-CimInstance Win32_ShadowCopy -ErrorAction Stop` | `Initialization failure` | Shadow-copy availability unknown. |
| `reagentc.exe /info` | Exit **5**; requires elevated command prompt | Windows RE configuration unverified. No elevation attempted. |
| `Get-Command wix.exe` | `C:/Users/blunt/.dotnet/tools/wix.exe` | Existing tool only; no acquisition. |
| MakeAppx file version read from known cached path | `10.0.29648.1000 (WinBuild.160101.0800)` | Existing preview SDK tool, not an approved release-toolchain declaration. |

Preflight's containing PowerShell process exited 0 because query errors were caught to report independent results; **the recovery probes did not pass**. Their exact errors and reagentc exit above are the operative results. No backup image, recovery media, boot test or successful restore has been verified. A restore point, even if later found, is not a full disk/data backup.

## Fresh real-tool results

### 1. Current MSIX script: PASS within unsigned packaging scope

Executed November 24, 2026, 18:02:21–18:02:32 PST, exit **0**, stderr empty. `msix.json` contains the complete argv, timestamps, stdout and stderr; separate `.stdout.txt` / `.stderr.txt` files retain raw streams. Exact command with `$S` expanded to the directory above:

```powershell
powershell.exe -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File C:/Users/blunt/Desktop/wincare-desktop/build-msix.ps1 -PayloadDir "$S/input payload" -OutputDir "$S/msix candidate" -MakeAppxPath C:/Users/blunt/.nuget/packages/microsoft.windows.sdk.buildtools/10.0.29648.1000-preview/bin/10.0.29648.0/x64/makeappx.exe -Publisher "CN=WinCare Host Smoke Test" -PublisherDisplayName "WinCare Host Smoke Test" -IdentityName WinCare.HostSmokeTest -Version 2.0.0.0 -UnsignedCandidate
```

MakeAppx reported **Package creation succeeded** and **Package extraction succeeded**. The script reached its HOLD receipt only after its payload and manifest checks. This also exercises actual Windows PowerShell native-argument marshalling with paths and publisher strings containing spaces.

Independent readback in `verification.json`:

- All **7** reviewed portable files matched the scratch input and extracted payload.
- All **18** staged files matched the **19** extracted files; the sole extra extracted file was `AppxBlockMap.xml`.
- ZIP manifest identity: `WinCare.HostSmokeTest`, publisher `CN=WinCare Host Smoke Test`, version `2.0.0.0`, architecture `x64`.
- `AppxSignature.p7x` absent; receipt says `HOLD / NO_SHIP`, `unsignedPayloadCandidate=true`. No signature/trust/installability claim.
- Package: `$S/msix candidate/WinCare.msix`, **62093562 bytes**.
- SHA-256: `7ecf3c1fb2bd23886b774d1d5624897fb88e028012cb98a197360a440e9c956a`; independently matched the receipt.

### 2. WiX build-mode `-nologo`: PASS, compile/library scope only

`wix.exe build -?` first returned tool help identifying **5.0.2+aa65968c419420d32e3e1b647aea0082f5ca5b78**, exit 127. This help exit was not counted as a successful build.

Actual build via `wix-smoke.ps1` completed at **18:02:48 PST**, exit **0**, stdout/stderr empty. Underlying native argument sequence:

```powershell
& 'C:/Users/blunt/.dotnet/tools/wix.exe' build -arch x64 -d "SourceDir=$S/input payload" "$S/WinCare.wxs" -o "$S/wix-single/WinCare.wixlib" -intermediatefolder "$S/wix-single/intermediate" -nologo
```

This uses an unchanged scratch copy of `installer/WinCare.wxs` and the same checked native invocation helper as production. Library output avoids MSI binding/validation/lifecycle operations. Readback found a valid ZIP containing `wix-ir.json`, **2311 bytes**, SHA-256 `d63e52f7bb6b79e60e0ea43ffd9cc827878341456b85fb0101fdde59b9a1477c`. Thus no speculative source change from `-nologo` to `--nologo` is justified by this test. Complete MSI build/extraction remains unverified.

Harness issue, not a product defect: the first scratch wrapper passed `-nologo` as a value to its own PowerShell `-NoLogoArg` parameter. PowerShell rejected it as a missing argument before WiX launched (exit 1; origin: introduced scratch harness). The wrapper was corrected to use the literal internally; the successful rerun above is preserved separately in `wix-build-result.json`. `wix-single-dash.json` retains the initial failure. `run.py` captures child exit codes but itself returned 0 after capturing that first failure; rely on individual child results, not its container exit.

## Preservation and final verification

`before.json` recorded SHA-256 for **1,748 existing repository files excluding .git**, including dirty source and existing artifacts. `verify.py` ran once after the smoke checks, exit **0**, at **2026-11-24T18:03:19.146957-08:00**:

- **1,748 unchanged; zero changed; zero missing; zero new** at verification time.
- Reviewed executable still hashes to `3e082629060d9580b74e34fdfe996e105bc5dabcb64fec6ff940bb3026db1859`.
- Fresh output hashes, receipt, ZIP/manifest and payload comparisons passed as recorded above.
- This report was then created using the file tool's confirmed write. No source edits or rebuilds followed verification.

Existing installers remain historical/untrusted and were neither replaced nor launched. The reported prior 303 application tests and 44 inert packaging tests were **not rerun** and are not fresh evidence from this task. No host-wide mutation-monitoring claim is made: byte preservation covers the repository baseline, and action restrictions describe the commands actually issued.

## Exact next tests and consent gates

No blanket VM prerequisite is imposed for future non-destructive checks on this authorized spare desktop. Each higher-risk action needs its own scope and approval:

1. **Recovery-status escalation:** if the owner wants it, explicitly authorize elevated *read-only* `Get-ComputerRestorePoint`, `Get-CimInstance Win32_ShadowCopy` and `reagentc /info`, or supply their outputs. Separately identify backup/recovery media and a verified restoration procedure before destructive host testing. Creating restore points, enabling protection, making images or reboot-testing recovery is a separate authorization, not included here.
2. **Full MSI package/extraction gate:** first approve and inspect a bounded full-build procedure and validation behavior. Running the current `build-installer.ps1` additionally requires explicit consent for its exact `msiexec /a <new scratch MSI> /qn TARGETDIR=<new scratch extraction> /l*v <scratch log>` operation. It was expressly prohibited in this task. Do not infer complete MSI packaging success from the library test.
3. **Installer lifecycle:** explicitly approve the named newly built candidate, per-user install target, repair/upgrade/downgrade/uninstall cases, previous-version fixtures, and preservation/reset plan. MSIX signing/trust provisioning is a separate gate. Do not use existing stale installers or silently import test roots on this host.
4. **Native actions:** approve each named target and side effect independently: fixture-only recycle/cleanup; a named disposable uninstall; specified startup/registry setting; a specified network adapter/DNS change; a named app close; or Defender scan acknowledging possible policy remediation. No blanket optimize/cleanup, process kill, host settings change, scan, or reboot follows from “spare desktop.” Fixtures alone do not make machine-wide effects safe. Recovery remains unverified.
5. **UI/accessibility:** a non-mutating interactive pass can use this host after checking current launch/persistence behavior and choosing a scratch-safe mode. It was not launched here. Host display/contrast changes or normal-mode actions need their own boundaries; no claim is made for keyboard, Narrator, DPI or native action semantics.
6. **Distribution:** owner must choose channel, supported OS/runtime/toolchain, publisher/signing identity and credential custody. Approved payload signing, outer signing, chain/timestamp checks, complete package identity, lifecycle tests and reviewed release automation remain open. No credential exports, sign operations, downloads, publication or git writes are authorized by this preflight.

Reusable bounded procedure recorded here: inspect the actual entry/helper/manifest chain; baseline existing bytes; create a unique scratch root; copy the reviewed payload; redirect process temp; run only no-install/no-sign paths; capture individual exits; independently read back identities and hashes; verify baseline preservation; distinguish tool compatibility from native lifecycle and release readiness.
