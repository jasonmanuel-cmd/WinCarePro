# WinCare Rescue — bounded slice results

## Outcome and acceptance boundary

**Implemented a usable read-only rescue slice, not the full RESCUE-UPGRADE.md roadmap. Overall roadmap acceptance remains HOLD.** The existing uncommitted completion/usefulness work and portable artifacts were preserved. No existing live WinCare instance was closed or overwritten.

- **249 executed / 249 passed / 0 failed / 0 skipped**, parsed from the final TRX.
- Release build: **0 warnings / 0 errors**, exit 0.
- Self-contained win-x64 single-file publish: exit 0, normal SDK targeting, no dependency changes or runtime override. Included .NET and WindowsDesktop **8.0.34**; not a latest/security-approval claim.
- **40 actual WPF PNG renders** generated. Five individual new-page/minimum-size images and five contact sheets covering the captures were inspected. No blocking overlap observed in those captures; minimum-height content uses scrolling. This is not native keyboard/modal/DPI/accessibility certification.
- New portable launched with isolated safe mode: PID **4336**, HWND **1967144**, responsive window-message probe, WM_CLOSE sent only to that verification child, exit **0**. Isolated data directory had **no files**, consistent with diagnostic logging off/safe mode. No real scan, process close, OS setting change or remediation was performed.

## Implemented versus unavailable

| Requirement | Implemented in this slice | Still unavailable / open |
|---|---|---|
| 1. Memory/background apps | Manual RAM capacity/consumption and top working-set readings; short sampled CPU normalized by logical processors, matched PID/start identity and per-process monotonic interval. Two bounded passes inspect at most 512 processes each, display at most 20 rows and disclose inaccessible/exited/over-limit observations. Explain RAM versus storage, shared working sets, unknown roles, unsaved work and existing Startup Apps review. | **All in-app closing disabled.** No confidently eligible app allowlist, owner/session/path/role verification, exact-target confirmation or close audit implemented. No denylist-only shortcut, forced kill, tree termination or global flush added. This is process inventory, not optional-app classification or verified memory relief. |
| 2. Defender | Explicit read-only WMI status request; active/passive/EDR/disabled/unknown/error distinctions; signature timestamp/age. Confirmed supported Windows Security settings handoff outside safe mode. Instructions for scans and Protection history, with remediation-policy disclosure. | **Direct Quick/Full initiation disabled.** No in-app scan lifecycle, scan-ID correlation, cancellation, detections listing, quarantine/removal or elevation. Opening Windows Security is only a launch request, never scan completion. Native WMI compatibility and real shell handoff remain unverified on production providers. |
| 3. Safety | No new close/scan mutation adapter exists. Root handoff and native shell adapter both enforce safe mode before confirmation/launch. Options writes blocked at root VM in safe mode. Existing removal/rollback guards untouched. | Full roadmap process/remediation safety gates cannot pass while actions are unavailable. Existing native/admin/destructive gates remain open. |
| 4. Options | Schema-1 app store, staged same-directory atomic replacement; validated refresh choices 5/15/30/60 seconds, default 15. Local environment/crash files opt-in, default off. Corrupt/unknown-schema/invalid preferences visibly fall back to private defaults without overwriting saved bytes. Failed saves retain previously applied preferences. Reset writes only app preferences. | No legacy options schema existed; unknown versions are refused, not migrated speculatively. Crash/power-loss durability and malicious filesystem races not proven. No new theme toggle. Existing interface is static with no animated transitions; reduced-motion preference is persisted as true on save/reset, not advertised as a new animation engine. |
| 5. Low overhead/lifecycle | New readers are manual, never start on navigation/launch, and refuse overlapping requests. Cancellation/navigation/window-close discard late results and retain clearly stale observations. No new polling loop or background task registration. | Cancellation cannot forcibly interrupt a blocked native WMI call; requested enumeration timeout is not a hard end-to-end deadline. No older-PC performance improvement is claimed. |

### Privacy details

The Options switch controls future local `environment.txt` and crash files, not required recovery history or explicitly previewed diagnostic exports. Existing files are retained. The startup environment report is skipped when disabled; enabling it takes effect on a later launch. Crash UI now describes attempted/enabled logging rather than asserting a write succeeded. Safe mode suppresses these automatic diagnostic files regardless of preference. No upload client was added.

### Useful prior-work review (read-only)

Reviewed `DiagnosticSession.cs`, `DiagnosticSession.Export.cs`, `DiagnosticExport.cs` and existing cancellation tests while integrating. The exact-preview export, sanitized allowlisted payload, duplicate-save barrier and late-result discard remain unchanged and their tests passed in the full suite. Two existing boundary observations are retained for parent review rather than silently refactored:

1. Dashboard guidance reads cancel on window close, but `DashboardViewModel.OnNavigatedFrom` does not cancel them. New Memory/Defender readers do cancel on navigation. Existing reads may therefore finish after leaving Dashboard.
2. Existing explicitly requested diagnostic export is not blocked by `SafetyMode`; its writer can write the user's chosen destination. New Options privacy control intentionally does not disable manual export. Do not interpret safe mode as an absolute no-file-write sandbox for existing export/test-fixture paths.

No real diagnostic export was created from host data during this pass.

## Test-first and verification evidence

Evidence root: **`C:/Users/blunt/AppData/Local/hermes/cache/scratch/wincare-rescue/`**.

Observed behavioral RED runs, followed by green coverage in the final full suite:

- `options-red.log`: private/low-resource defaults failed; `options-default-green.log` passed.
- `options-load-red.log`: corrupt, unsupported schema, invalid interval and saved-value reads failed; subsequent options run passed these.
- `options-save-red.log`: save/reset, locked destination preservation and invalid-save rejection failed; subsequent combined run passed all eight options cases.
- `memory-red.log`: sampled CPU, PID reuse and bounded results failed; subsequent combined run passed.
- `defender-red.log`: mode/unknown/signature semantics and safe-mode shell guard failed; subsequent combined run passed.
- `rescue-ui-red.log`: missing navigation failed; subsequent integration run passed the navigation contract.
- `options-integration-red.log`: timer interval and diagnostic-file policy failed; final suite passed.
- `memory-timing-red.log`: using enumeration time instead of the individual process observation interval failed; final suite passed.
- Supplemental injected lifecycle/error/safe-mode tests cover cancellation, late results, duplicate requests, failed reads and pre-cancelled native reads. These are additional coverage, not a claim that every added line had a separate RED run.

First full run: `full-tests.log`, **247 passed / 1 failed**. Failure was the previous exact-eight-navigation assertion, caused by the requested new pages. Replaced it with an explicit complete eleven-title assertion, not a weaker count check. Final result: `final-tests.log` and `rescue-final.trx`, **249 passed** after the added per-process-timing regression. Initial scaffold warnings and the xUnit navigation assertion warning were resolved; final test/build output has no warnings.

Exact executable: **`C:/Program Files/dotnet/dotnet.exe`**. Commands run from `C:/Users/blunt/Desktop/wincare-desktop`:

```text
test WinCareDesktop.Tests/WinCareDesktop.Tests.csproj --no-restore --verbosity quiet --logger trx;LogFileName=rescue-final.trx --results-directory C:/Users/blunt/AppData/Local/hermes/cache/scratch/wincare-rescue
build WinCareDesktop.csproj -c Release --no-restore --verbosity minimal
publish WinCareDesktop.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true --no-restore -o C:/Users/blunt/Desktop/wincare-desktop/artifacts/rescue-portable --verbosity minimal
run --project Tools/RenderShots/RenderShots.csproj --no-restore -- C:/Users/blunt/AppData/Local/hermes/cache/scratch/wincare-rescue/views
```

`run.py` and `*-command.json` retain exact argv, exits and logs. Every invocation set telemetry/first-experience/certificate suppression, scratch TEMP/TMP, safe mode and isolated data root. Full-suite mutations use pre-existing isolated fixtures; new lifecycle reads use injected tasks. No native Defender scan, real app close or Windows Security launch was exercised. `git diff --check` returned 0 with only Git line-ending normalization notices. UI renders precede only the final CPU-interval implementation correction; no UI/layout text changed after rendering.

Other evidence:

- `release-build.log`, `portable-publish.log`, `render.log`.
- `views/*.png`, `contact-0.png` through `contact-4.png`. New captures: blank/new pages, sampled/error/busy/cancelled memory, Defender modes/error, Options failure and minimum 980x650 window variants. Standard window is 1180x760. Client captures exclude native frame.
- `launch-and-record.py`, `packaged-launch-result.json`, `packaged-host-trace.log`, `published.runtimeconfig.json`.
- `evidence-summary.json`: parsed TRX, runtime, package hashes, source hashes and screenshot hashes.

## Artifact

**Directory:** `C:/Users/blunt/Desktop/wincare-desktop/artifacts/rescue-portable/`

**Executable:** `C:/Users/blunt/Desktop/wincare-desktop/artifacts/rescue-portable/WinCare Pro.exe`

- Size: **137641706 bytes**.
- SHA-256: **66a212123db2cdd7609234170a4607f8619b389210ef68911510bab6fbec8226**.
- Retain native sidecars. Inspect with `--safe-mode` and a separate `WINCARE_DATA_ROOT`.
- No dependency upgrades, Git reset/commit/push, signing, install or release. Previous artifacts retained.

## Official sources fetched before native integration

The configured extraction tool failed because its backend was search-only. A read-only Python HTTPS fallback successfully fetched the Microsoft pages below; full extracted pages and source URLs are retained in `C:/Users/blunt/AppData/Local/hermes/cache/scratch/wincare-rescue/docs/`.

- Microsoft Learn `MSFT_MpComputerStatus`: `https://learn.microsoft.com/en-us/previous-versions/windows/desktop/defender/msft-mpcomputerstatus`
- `Start-MpScan`: `https://learn.microsoft.com/en-us/powershell/module/defender/start-mpscan?view=windowsserver2025-ps`
- Defender event IDs: `https://learn.microsoft.com/en-us/defender-endpoint/troubleshoot-microsoft-defender-antivirus` — 1000 started, 1001 completed, 1002 cancelled carry Scan ID. No command/event implementation was guessed from these docs.
- Defender compatibility/modes: `https://learn.microsoft.com/en-us/defender-endpoint/microsoft-defender-antivirus-compatibility`
- Process.CloseMainWindow: `https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.process.closemainwindow?view=net-8.0`
- Win32_Process identity/owner/creation metadata: `https://learn.microsoft.com/en-us/windows/win32/cimwin32prov/win32-process`
- Windows Settings URI reference: `https://learn.microsoft.com/en-us/windows/apps/develop/launch/launch-settings-app` — fixed `ms-settings:windowsdefender` handoff only.

## Changed files

New production: `Services/RescueOptions.cs`, `MemorySampler.cs`, `NativeMemoryReader.cs`, `DefenderStatusReader.cs`; `ViewModels/MemoryViewModel.cs`, `DefenderViewModel.cs`, `OptionsViewModel.cs`; matching three XAML views and code-behind files.

Modified integration: `App.xaml.cs`, `MainWindow.xaml`, `MainWindow.xaml.cs`, `ViewModels/MainViewModel.cs`, `Tools/RenderShots/Program.cs`.

Tests: five new `Rescue*Tests.cs` files; extended `PanelDismissTests.cs`. Docs: `RESCUE-DESIGN.md`, `RESCUE-RESULTS.md`, status in `RESCUE-UPGRADE.md`, `README.md`. Reusable CPU-sampling/bounded-capability lessons recorded in the already-loaded TDD skill's Windows isolation reference.

## Remaining gates

Parent independent review, native WMI/provider and Windows Security handoff, standard-mode Options modal/interaction, native keyboard/focus/screen-reader/hover and DPI matrix remain open. Disposable-VM administrative/destructive and prior cleanup/startup/privacy/DNS/scheduler recovery gates remain open. Full process eligibility/closing, scan-ID lifecycle/detections/remediation work remains a future implementation task, not a passed test or deferred verification of implemented functionality. No full-roadmap completion, security clearance, freed-RAM guarantee or restored hardware performance is claimed.
