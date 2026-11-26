# Next-candidate acceptance — prepared, not executed

Prepared November 25, 2026 (Pacific time). **HOLD / NOT BETA APPROVED.** This is a prospective test protocol, not an installer or UI result. It does not repair, replace, or supersede historical evidence. The next beta build is owned by another lane; its final MSI identity is not yet bound here.

## 1. Boundaries and evidence reviewed

This task performed document/source reads, live primary-documentation retrieval, and an in-process Windows PowerShell capability probe. It did **not** build, extract an MSI, install, repair, uninstall, launch WinCare, send keys, change system settings, elevate, reboot, sign, touch certificates, install tools, or write to a network service. Its sole durable output is this new document.

Reviewed:
- `HOST-INSTALLER-LIFECYCLE.md` (historical installed payload, not next-beta evidence).
- `RUNTIME-MIGRATION-RESULTS.md` (migration/build evidence, not next-beta native acceptance).
- `C:/Users/blunt/AppData/Local/hermes/cache/scratch/wincare-lifecycle-aab4bcc894434332ac66672eba03e9b8/INSTALLER-VERIFICATION-REVIEW.md`.
- Current `installer/WinCare.wxs`, `build-installer.ps1`, `Services/SafetyMode.cs`, and navigation/window markup in `MainWindow.xaml`.

Historical review retains HOLD: `/fa` caller arguments included suppression properties, but engine properties omitted them and the log opened a Restart Manager session. Exit 0, copied payload, successful removal and unchanged boot are useful observations, not proof of effective repair suppression. Original verifier and logs remain untouched.

Snapshot fingerprints (SHA-256; concurrent source changes invalidate source-specific assumptions):

```text
installer/WinCare.wxs       1daed1727bffa17d419f9c3f2f8740542fc43f8d09d288af3b47174f928dce7f
Services/SafetyMode.cs      dd357b0181c07aa4aeb1932b16db0f5c2b49d2705ce97a1642f4f0954ed2beac
HOST-INSTALLER-LIFECYCLE.md  5c74a391157a917a9f257b59cae594ffa9009e017cf511eed5f5848844b9ccc7
RUNTIME-MIGRATION-RESULTS.md bb510b81e0ee245a0c6ae31d738e431893b5d5bd264b06cf9f2a6fdac59ee054
```

## 2. Documented route, distinct from observed acceptance

Microsoft documents `/i Package|ProductCode` as install/configure and explicitly says `/f` ignores command-line property values [M1]. Therefore the proposed **maintenance** route is `/i` against the newly installed exact ProductCode, not `/fa`, and not a fresh package with ambiguous identity. This is a documentation-backed route to test; it has not been executed against the next beta.

`REINSTALL=ALL` selects all previously installed features; it does not install an absent product. Set `REINSTALLMODE` explicitly [M2]. Select **`amus`** for this prospective lifecycle: `a` forces file reinstall, `u` rewrites required user registry, `m` rewrites machine/class registration where authored, and `s` reinstalls shortcuts/icons. It is broader than historical file-only `/fa`; review those tables before authorizing it. `m` is not a scope switch. Do not add `v`: recaching/source-package update is not this repair test [M3]. If approval only permits file-copy parity, use `a` instead and mark registry/shortcut reconstruction out of scope; do not silently change modes mid-run.

Use both `/norestart` and `REBOOT=ReallySuppress`. Microsoft describes them as equivalent [M4]. When effective, ReallySuppress suppresses Installer restarts and prompts, including ForceReboot and end-of-install restarts. Suppress is weaker; `/qn` alone is not restart protection. Files in use can require restart without authored reboot actions [M5]. This is not protection against unrelated OS activity or arbitrary custom-action reboot code.

Select **`MSIRESTARTMANAGERCONTROL=Disable`** for every operation: it disables package interaction with Restart Manager and uses FilesInUse behavior. Default/0 attempts Restart Manager; `DisableShutdown` still uses it for detection and is not equivalent. The property is ignored when Restart Manager is unavailable/disabled. Neither setting substitutes for REBOOT [M6]. Close the owned app before maintenance; do not manufacture locked files or rely on silent mode to resolve them.

### Required effective-property evidence

For install, repair and uninstall separately retain the full raw verbose log, native argv, operation PID, cwd, environment overrides, timestamps, actual exit and pre/post host snapshots. Require:

1. Engine-side command/property evidence for `REBOOT=ReallySuppress` and `MSIRESTARTMANAGERCONTROL=Disable`, plus their final effective property values. For repair additionally require `REINSTALL=ALL`, `REINSTALLMODE=amus`, installed-product maintenance context, and exact ProductCode.
2. Parse values and chronology, not one fragile spacing/case-specific line. Inspect server-side `MSI (s)` / `Property(S)` evidence where present; client argv/`Property(C)` alone does not establish execute-engine acceptance. Later conflicting assignments fail acceptance. If expected engine evidence is absent, ambiguous or truncated, retain HOLD and investigate read-only; never replace it with argv or delete the assertion.
3. No contradictory Restart Manager session-open/shutdown/restart activity in this operation. A session-open line with Disable is a discrepancy requiring investigation, not an automatic harmless exception. Absence of session activity without positive property evidence is insufficient.
4. No reboot-initiated indication, no prompt observed, unchanged OS last-boot identity across the operation and lifecycle, and no unexpected termination/relaunch of unrelated apps. These establish observed behavior only.

These are acceptance rules, not a Microsoft guarantee of a particular log rendering. No one log substring, exit code or boot timestamp alone proves all of them.

## 3. Candidate lock and collision prerequisites — stop on any failure

Complete and sign off this record **before** execution; placeholders intentionally are not runnable identities:

```text
Candidate receipt path / SHA256:
MSI absolute path / SHA256 / byte count:
ProductCode / PackageCode / UpgradeCode / ProductVersion / architecture:
Source snapshot identity / build and runtime receipts:
Complete approved payload path-to-SHA256 manifest:
Expected installed directories, registry rows, shortcuts and feature states:
Operator / authorization / date / current-user SID / token elevation:
Evidence directory (new durable local folder, outside auto-pruned scratch):
```

The migration report's portable executable hash `3dc6a1c690edbd13a39a625956f357b938fd644c67c2fbeb6233ad86bba4280e` describes that report's candidate only. Do not insert it as the next beta identity unless the build owner demonstrates exact equality. Versions such as 2.0.0 are not sufficient identity; preserve complete payload including native sidecars. Do not assume seven files from the historical MSI.

Open the final MSI database **read-only** using existing Windows Installer COM `OpenDatabase(path, 0)` or `msi.dll` APIs. Record Property, Feature, FeatureComponents, Component, File, Directory, Registry, Shortcut, RemoveFile, Upgrade, LaunchCondition, CustomAction, InstallExecuteSequence, InstallUISequence, Media and SummaryInformation (including package code/platform). Verify cabinet/source availability from the build receipt. Do not invoke `/a`: `build-installer.ps1` itself invokes an administrative installation, so it is not a permitted read-only inspection shortcut. Do not launch that helper here.

Current source observations, which the compiled candidate must independently confirm:
- Name `WinCare Pro`, version `2.0.0`, UpgradeCode `{7B2F4A19-3C5E-4D81-9A6B-2E8C4F1D7A30}`, `Scope="perUser"`, one `Main` feature and recursive payload harvest.
- Install destination `%LOCALAPPDATA%\WinCare Pro`; user Start Menu directory `WinCare Pro`; nonadvertised shortcut targeting `WinCare Pro.exe`, no authored arguments, working directory INSTALLFOLDER.
- HKCU `Software\WinCare\WinCare Pro` values `installed=1` and `InstallFolder`; fixed StartMenu component GUID `{A1D4E7C2-5B8F-4E3A-9C6D-0F2B8E5A7C41}`; shortcut folder removal on uninstall.
- ProductCode is not explicitly fixed in this source: read the compiled MSI, never guess/reuse a historical code. MajorUpgrade is authored with `AllowSameVersionUpgrades="no"`.

**Collision risk:** the stable paths, registry key, shortcut and component identity are not isolated just because the beta is a new build. WiX documents that same-version packages with differing product codes can coexist when same-version upgrades are disabled [W1]. Here shared resources make coexistence unacceptable. An older related product can also enter major-upgrade removal. Neither scenario is authorized by this clean lifecycle. A fourth version field alone does not solve MSI major-upgrade version comparison [W1]. No authoring changes are proposed/applied by this document.

Preflight must enumerate current-user and machine-visible related MSI registrations (UpgradeCode, exact product and named products), plus HKCU/HKLM uninstall entries in both registry views; capture MSI context/assignment separately. Use read-only MSI enumeration APIs/COM, never `Win32_Product`. Check both user/machine install and Start Menu paths, user/public desktop shortcuts, authored keys and running WinCare processes. Stop on **any** existing related product, shared path/key/shortcut or unresolved access denial; do not upgrade, uninstall or overwrite it. Cross-user visibility is limited: offline profiles and inaccessible registration contexts are not cleared by a negative current-user query.

Require a standard, non-elevated token and compiled per-user scope. For this authoring with absent ALLUSERS, **omit both ALLUSERS and MSIINSTALLPERUSER overrides**. MSIINSTALLPERUSER only affects scope when ALLUSERS=2 [M7]; it is not a safeguard for this package. Never pass bare `ALLUSERS=`. If compiled scope differs, stop rather than correcting it with command-line properties. Require no unexpected custom actions, reboot actions, service/driver registration or unexplained removal/upgrade behavior before authorization.

Baseline original `%LOCALAPPDATA%\WinCarePro` data, all potential owned destinations, relevant pending-reboot state, boot time and process inventory. Hash source/approved artifacts and original user data. Keep backup/recovery status explicit; never claim a local copy proves recovery. If pending reboot is already present, pause for owner risk disposition; no automatic reboot. `MsiSystemRebootPending=1` means a pending rename and is not a complete reboot detector [M8]. Record denied reads, not guessed values. Confirm no concurrent installer/build is modifying the locked candidate or targets.

## 4. Future-authorized command templates — NOT RUN

These commands mutate installer state. This document is not permission to execute them. Run only after new candidate identity, fresh collision checks and exact lifecycle authorization are recorded. Retain the original source MSI at its approved location for repair source resolution. Never auto-fallback to another package, old ProductCode, `/f`, recache, scope override or elevation.

```text
# M = exact approved MSI; P = exact ProductCode installed by this test; E = existing evidence folder
C:\Windows\System32\msiexec.exe /i "M" /qn /norestart REBOOT=ReallySuppress MSIRESTARTMANAGERCONTROL=Disable /l*vx! "E\install.log"
C:\Windows\System32\msiexec.exe /i "P" REINSTALL=ALL REINSTALLMODE=amus /qn /norestart REBOOT=ReallySuppress MSIRESTARTMANAGERCONTROL=Disable /l*vx! "E\repair.log"
C:\Windows\System32\msiexec.exe /x "P" /qn /norestart REBOOT=ReallySuppress MSIRESTARTMANAGERCONTROL=Disable /l*vx! "E\uninstall.log"
```

Do not paste these into bash unchanged. Prefer a reviewed native argv launcher; for a future Windows PowerShell harness this bounded single-operation template preserves the final command string. It intentionally stops before doing anything until manually adapted after approval. Never put all three operations in an unconditional loop.

```powershell
throw 'TEMPLATE ONLY: bind approved identity and preflight; do not execute from this document'
$M = '<absolute-approved-msi-path>'
$P = '<exact-new-product-code>'
$E = '<existing-durable-evidence-directory>'
$Operation = 'repair' # one approved operation only
$ExpectedMsiSha256 = '<approved-sha256>'
if ($P -cnotmatch '^\{[0-9A-F]{8}(-[0-9A-F]{4}){3}-[0-9A-F]{12}\}$') { throw 'Invalid ProductCode' }
if (!(Test-Path -LiteralPath $E -PathType Container)) { throw 'Missing evidence directory' }
if ((Get-FileHash -LiteralPath $M -Algorithm SHA256).Hash -ne $ExpectedMsiSha256) { throw 'Candidate changed' }
$log = Join-Path $E ($Operation + '.log')
if (Test-Path -LiteralPath $log) { throw 'Never overwrite previous operation log' }
switch ($Operation) {
  'install' { $nativeArgs = @('/i',$M) }
  'repair' { $nativeArgs = @('/i',$P,'REINSTALL=ALL','REINSTALLMODE=amus') }
  'uninstall' { $nativeArgs = @('/x',$P) }
  default { throw 'Unknown operation' }
}
$nativeArgs += @('/qn','/norestart','REBOOT=ReallySuppress','MSIRESTARTMANAGERCONTROL=Disable','/l*vx!',$log)
# These tokens may have spaces, but embedded quotes/newlines are not accepted.
if (@($nativeArgs | Where-Object { $_ -match '["\r\n]' }).Count) { throw 'Unsafe argument' }
$commandLine = ($nativeArgs | ForEach-Object { '"' + $_ + '"' }) -join ' '
$exe = Join-Path $env:WINDIR 'System32\msiexec.exe'
$start = Get-Date
# Persist exact nativeArgs, commandLine, exe hash, cwd, environment and baseline BEFORE Start-Process.
$p = Start-Process -FilePath $exe -ArgumentList $commandLine -WorkingDirectory $E -PassThru
# Operational observation bound, NOT transaction cancellation or a guarantee of completion.
if (!$p.WaitForExit(180000)) {
  throw 'HOLD: 180-second observation limit. Do not kill Installer, rerun, or start next operation; operator must inspect the live transaction.'
}
$p.Refresh()
[pscustomobject]@{Operation=$Operation; Started=$start.ToString('o'); Ended=(Get-Date).ToString('o'); Pid=$p.Id; Exit=$p.ExitCode; Log=$log}
# Persist this receipt; perform readback and review before any next operation.
```

The timeout bounds waiting, not Installer service activity. Do not force-kill `msiexec` or its service; a live/hung transaction blocks subsequent actions. Precreate log directories and do not overwrite receipts. There is no runtime property-acceptance dry-run: the first real invocation itself is a mutation, so prereview is necessary but cannot prove the result in advance.

### Per-operation acceptance assertions

| Stage | Required evidence and stop conditions |
|---|---|
| Install | Exact new ProductCode registered in expected context, Assignment=0, expected feature state; all installed paths/bytes match the approved manifest; registry and shortcut values match compiled tables; no unrelated product removed; no UAC/elevation or machine-target payload. Record actual ARP hive/view rather than assuming HKCU. |
| Prelaunch | Hash every installed payload file; inspect shortcut via WScript.Shell without launching it; normalize trailing separators for working-directory comparison. Source MSI and manifest unchanged. |
| Safe launch/close | Exact installed executable PID/path/hash, isolated safe-mode environment, responsive expected window, UI checklist evidence, graceful close of only this PID and recorded exit. No normal-mode shortcut launch. |
| Repair | Fresh owned-product identity/context/path check, no WinCare process, positive maintenance properties and actual executed file-copy actions mapped to approved File/Component rows; complete post-repair manifest equality; authored registry/shortcut readback. A start-of-action line alone is not proof of copying. No deliberate deletion/corruption to force repair. |
| Uninstall | Fresh ownership check; exact test ProductCode only; registration, owned payload, shortcut and authored key removed with readback. Preserve unrelated files/data; investigate residue instead of manually deleting it. Do not clean Installer caches. |
| Preservation | Original user data unchanged throughout; candidate/source artifacts unchanged; distinguish concurrent owner edits from this lane. No unrelated process terminated, scope change or restart. Final exact-target requery and boot/process snapshot required. |

Exit handling [M9]: **0** is operation success, not aggregate acceptance. **3010** is successful operation with restart required: retain evidence, stop lifecycle progression, do not reboot and do not claim completed no-restart lifecycle; owner disposition required. **1641** means restart initiated and violates the no-restart criterion. Any other code, missing receipt, unexpected UAC, timeout, source prompt, files-in-use condition or ambiguous ownership is HOLD. Do not automatically retry or uninstall after a failed safety gate; safe cleanup also requires explicit disposition.

Do not stress reboot behavior on this host: no locking installed DLLs, forced reboot actions, pending-rename writes, test drivers, deliberate missing files or system-state manipulation. Effective properties plus unchanged boot can qualify only the ordinary observed path. Reboot-required-path suppression remains **not exercised** and must stay an explicit limitation; if policy requires that coverage, use a separately authorized disposable environment, not this desktop.

## 5. Bounded keyboard/accessibility smoke — exact beta only

### Facilities and launch prerequisite

A read-only capability probe at `2026-11-25T05:35:07.7645216-08:00` loaded UIAutomationClient 4.0.0.0, UIAutomationTypes and System.Windows.Forms in existing Windows PowerShell; the PowerShell parser also worked (exit 0). This proves availability only, not a WinCare UI pass. No SDK inspector, Accessibility Insights, FlaUI, WinAppDriver, voice or other tool installation is needed/allowed for this checklist.

Reinspect safe-mode guards in the **final beta source**, not just the snapshot. Current `SafetyMode.cs` accepts `WINCARE_SAFE_MODE=1` or `--safe-mode` and selects `WINCARE_DATA_ROOT` only in safe mode. Launch the exact hash-verified installed executable with both, using process-local environment and new owned directories for data, TEMP/TMP/TMPDIR and DOTNET_BUNDLE_EXTRACT_BASE_DIR. Do not set global/user environment variables or use existing `%LOCALAPPDATA%\WinCarePro`. Set working directory to the installed directory. Record environment, PID, start time, path, file version and hash; bind automation to PID and HWND, never window title alone.

Limits: one launch; 30 seconds for a responsive top-level window; 10 minutes total smoke; at most 40 Tab and 40 Shift+Tab transitions, and one read-only navigation visit per approved visible page. Snapshot at each page change and focus failure. Abort on foreign focus, unexpected native operation/UAC/dialog, exception, hang or identity change. Close only the launched app via CloseMainWindow and wait at most 15 seconds; if it remains, report HOLD rather than force-killing or starting repair.

### Read-only UIA collector template

Run this in a separate existing Windows PowerShell process with a 15-second observer budget. If a provider call hangs, only the owned collector may be ended, not the application or system processes. Resolve `$AppPid` from the launch receipt and validate executable path/hash before use. Query only that window's descendants, never the desktop descendants [U1]. This sample emits per-element JSON lines with potentially problematic UI text base64-encoded; retain raw output and errors. It does not drive focus, invoke patterns or send keys.

```powershell
throw 'TEMPLATE ONLY: bind the approved launched process and collect under a bounded observer'
$AppPid = 0 # set from exact launch receipt, not a name search
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
$p = Get-Process -Id $AppPid -ErrorAction Stop
$p.Refresh()
if ($p.MainWindowHandle -eq [IntPtr]::Zero) { throw 'No top-level window' }
$root = [System.Windows.Automation.AutomationElement]::FromHandle($p.MainWindowHandle)
if ($root.Current.ProcessId -ne $AppPid) { throw 'Wrong UIA process' }
function Encode-UiText([string]$s) { [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($s)) }
$elements = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants,[System.Windows.Automation.Condition]::TrueCondition)
if ($elements.Count -gt 1000) { throw 'UIA result exceeds bounded review limit' }
foreach ($el in $elements) {
  $c = $el.Current
  [pscustomobject]@{
    Pid=$c.ProcessId; RuntimeId=($el.GetRuntimeId() -join '.')
    NameB64=(Encode-UiText $c.Name); HelpB64=(Encode-UiText $c.HelpText)
    AutomationIdB64=(Encode-UiText $c.AutomationId)
    Type=$c.ControlType.ProgrammaticName; Enabled=$c.IsEnabled
    Focusable=$c.IsKeyboardFocusable; Focused=$c.HasKeyboardFocus; Offscreen=$c.IsOffscreen
    Bounds=$c.BoundingRectangle.ToString()
    Patterns=@($el.GetSupportedPatterns() | ForEach-Object { $_.ProgrammaticName })
  } | ConvertTo-Json -Depth 4 -Compress
}
```

UIA calls can block before a count check: the separate observer timeout matters. UIA can omit custom-rendered content and names can be blank without proving the control is decorative. Exceptions/stale elements are recorded as incomplete collection, not silently skipped into a PASS. Current navigation source supplies Title as accessible Name and Description as HelpText; runtime assertions must compare the **beta's actual approved navigation**, not historical full-product tabs or assumed AutomationIds.

### Checklist and evidence rows

Use a human keyboard operator or guarded automation with foreground HWND/PID and UIA focused-element verification **before each single key**. Never unattended SendKeys loops. System.Windows.Forms.SendKeys availability is not permission to send keys; manual Tab is sufficient. UIA InvokePattern testing is not a substitute for keyboard testing.

| ID | Test | Acceptance/evidence |
|---|---|---|
| UI-1 | Launch/window | Correct candidate identity; responsive top-level window and readable current page; no crash/dialog; actual screenshot plus scoped UIA snapshot. |
| UI-2 | Forward/reverse navigation | Tab/Shift+Tab order reaches each intended interactive control logically; visible focus at each step; no traps or unexplained focus loss; record ordered name/type/runtime-ID/focus observations. Disabled controls need not receive focus. |
| UI-3 | Keyboard activation | Enter/Space on **allowlisted navigation only** changes the expected page; accessible page heading updates; focus remains understandable. Do not invoke cleanup, optimize, scan, uninstall, startup changes, privacy changes, network actions, scheduled work or other native commands. |
| UI-4 | Safe/disabled states | Read-only beta's excluded or unsafe actions are absent/disabled with understandable reason; UIA enabled state agrees with rendered affordance. Do not click a destructive control to prove its guard; an unexpectedly enabled unsafe control is a blocker for that safety assertion. |
| UI-5 | Accessible names/state | Nondecorative controls have meaningful names, role/type and relevant value/selection/disabled state; labels disambiguate repeated actions; icon glyphs are not the sole name. Record actual pattern support; not every control needs every pattern. |
| UI-6 | Dialog and focus return | Only if a source-reviewed, side-effect-free dialog exists: open by keyboard, inspect title/name, logical focus order, dismiss with Escape/Cancel and verify focus returns. If none exists, mark N/A with reason; do not open a native-action consent dialog speculatively. |
| UI-7 | Narrator (operator opt-in) | Existing Windows Narrator via Win+Ctrl+Enter [U2], with available local voice only; hear page/control names, states, navigation and any read-only status. Record what was heard and mismatches; UIA text alone cannot claim screen-reader success. Restore Narrator to its initial on/off state; do not download voices/change startup settings. Without operator/audio observation, NOT TESTED. |
| UI-8 | Size/visual limits | At current DPI/contrast and permitted ordinary window resize down to its declared minimum, inspect clipping, scroll reachability, labels and focus indicator. Record display resolution, scaling and theme. No global DPI/high-contrast changes in this scope. Additional DPI/high-contrast conditions require separately approved setup/existing suitable display; untested conditions remain NOT TESTED. |
| UI-9 | Close/preservation | Graceful close, recorded exit, no owned residual process; original data unchanged; candidate hashes unchanged. Only isolated test data may have been written. |

Receipt columns: `candidate SHA256 | case ID | time | PID/HWND | page | keys/interaction | expected | observed | PASS/FAIL/NOT TESTED/N/A | screenshot/UIA/audio-observation reference | limitation`. A cap reached before coverage means incomplete, not PASS. Captures must exclude unrelated user windows/data. No screenshot, keyboard, Narrator, contrast or DPI observation was made in this preparation task.

## 6. Verdict rules and verification of this deliverable

Accept each criterion only from new exact-candidate receipts. Missing observations are NOT TESTED; safety contradictions are HOLD. Keep ordinary-path lifecycle success separate from reboot-required-path coverage, interactive accessibility coverage, normal-mode native acceptance, support-matrix testing, signing/trust, hosted CI and release approval. This document clears none of those gates.

This preparation verified current source observations, primary documentation and local automation-library availability. The command/code blocks are templates, not an exercised installer harness. A final read-only check parses both PowerShell blocks without executing them, checks required sections/identities/source references, and checks the authoring fingerprint for drift. Successful static verification does not establish native or UI acceptance. Preserve prior failed verifier evidence and the two-correction limit; do not rerun lifecycle mutations merely to obtain a green report.

## 7. Primary references

Retrieved read-only by Python HTTPS GET on November 25, 2026; no `web.run` retrieval was performed. Microsoft pages returned HTTP 200 and readable article content despite generic Learn authorization banners. The FireGiant page returned HTTP 403; web_extract was unavailable on the configured search-only backend. A guessed WiX source path returned 404, then the official GitHub tree identified `src/xsd/wix/MajorUpgrade.xsd`, whose current primary source documentation was retrieved successfully as [W1]. The UIA scope guidance comes from the primary native API reference. References support documented semantics, not a claim that the next candidate followed them.

```text
[M1] Microsoft — Command-Line Options
https://learn.microsoft.com/en-us/windows/win32/msi/command-line-options
[M2] Microsoft — REINSTALL property
https://learn.microsoft.com/en-us/windows/win32/msi/reinstall
[M3] Microsoft — REINSTALLMODE property
https://learn.microsoft.com/en-us/windows/win32/msi/reinstallmode
[M4] Microsoft — Standard Installer command-line options
https://learn.microsoft.com/en-us/windows/win32/msi/standard-installer-command-line-options
[M5] Microsoft — REBOOT property
https://learn.microsoft.com/en-us/windows/win32/msi/reboot
[M6] Microsoft — MSIRESTARTMANAGERCONTROL property
https://learn.microsoft.com/en-us/windows/win32/msi/msirestartmanagercontrol
[M7] Microsoft — MSIINSTALLPERUSER property
https://learn.microsoft.com/en-us/windows/win32/msi/msiinstallperuser
[M8] Microsoft — MsiSystemRebootPending property
https://learn.microsoft.com/en-us/windows/win32/msi/msisystemrebootpending
[M9] Microsoft — MsiExec.exe and InstMsi.exe error messages
https://learn.microsoft.com/en-us/windows/win32/msi/error-codes
[W1] WiX official source — MajorUpgrade schema documentation (main branch at retrieval)
https://raw.githubusercontent.com/wixtoolset/wix/main/src/xsd/wix/MajorUpgrade.xsd
Blocked rendered reference (not relied on as retrieved content):
https://docs.firegiant.com/wix/schema/wxs/majorupgrade/
[U1] Microsoft — IUIAutomationElement::FindAll
https://learn.microsoft.com/en-us/windows/win32/api/uiautomationclient/nf-uiautomationclient-iuiautomationelement-findall
[U2] Microsoft — Chapter 1: Introducing Narrator
https://support.microsoft.com/en-us/accessibility/windows/narrator/chapter-1-introducing-narrator
```
