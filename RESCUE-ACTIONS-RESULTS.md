# Rescue actions — implementation and evidence

## Outcome

**Implemented guarded action paths, not just read-only inventory. Native release acceptance remains HOLD pending disposable-VM validation and independent review.** Existing uncommitted work and earlier artifacts were retained. No host Defender scan/remediation, live optional-app close, task registration, policy change, administrator elevation, install, dependency upgrade, commit, push or release was performed.

Final isolated suite: **289 executed, 289 passed, 0 failed, 0 skipped**. Release build: **0 warnings, 0 errors**. Normal self-contained win-x64 publish succeeded. Safe-mode packaged launch produced a responsive window and exited cleanly after closing only that verification child. RenderShots generated **51 WPF captures**, including **11 explicit new action fixtures**.

## Functional scope

### Defender

- Quick and Full buttons are wired to real native `Start-MpScan -ScanType QuickScan` / `FullScan` adapters, not success-returning placeholders. The actual provider process was NOT launched for these mutation paths during development.
- A prior active-protection read enables the controls outside safe mode. Each request then requires a named Quick/Full confirmation disclosing existing-policy quarantine/removal, irreversibility, CPU/disk cost, no hidden elevation, and the difference between monitoring cancellation and scan cancellation. Active mode and event availability are rechecked after consent.
- Native adapter and process-launch boundary reject safe mode before starting a mutation process. PowerShell uses the Windows system executable, `-NoProfile`, `-NonInteractive`, no shell elevation, fixed enum-derived command text and terminating error handling. Permission/policy failure is reported; no silent elevation or protection changes occur.
- Scan request and lifecycle are separate. States are NotStarted, StartRequested, Running, Completed, Cancelled, Failed and Unknown. A successful process exit is not scan completion. Manual **Refresh scan lifecycle** queries events; no always-on scan polling or fabricated percentage/ETA was added.
- Correlation requires the Microsoft Defender provider, event record IDs beyond the captured baseline, a start in the 30-second initiation window, matching user and scan kind, and a valid Scan ID. Completion/cancellation requires the same ID after its start. Multiple candidate starts, incomplete snapshots or regressed log records become unknown; ambiguity is latched rather than forgotten on a later snapshot.
- **Important evidence boundary:** Start-MpScan does not provide a caller-bound correlation token in this implementation. The UI explicitly describes a uniquely correlated provider observation, not proof that WinCare owns that scan. Concurrent external scans can make attribution unknown. No claim of malware clearance is made.
- Preparation is bounded at 35 seconds; individual process-backed reads at 15 seconds; lifecycle/detection read cancellation at 20 seconds; the scan command wrapper at eight hours while WinCare remains running. Timeout stops only WinCare's PowerShell wrapper, never a Defender process/tree, and does not establish actual scan cancellation. Closing WinCare ends its in-process monitoring/deadline management; review Windows Security afterwards.
- Stop monitoring, navigation and window close cancel observation/preflight only. They do not issue a scan-cancel command. Unknown/failed outcomes block another request in this session; completed/cancelled observations permit another only after the command task ends.
- `Get-MpThreatDetection` supplies a bounded history summary (first 100 of up to 101 retrieved entries), with threat IDs, provider status and action-success fields. Active and past detections are explicitly not attributed to the requested scan. Partial/unavailable/error states remain visible; empty history is not clean. Names, affected paths and supported remediation remain in **Windows Security → Virus & threat protection → Protection history**.
- Remediation is a confirmed, safe-mode-guarded Windows Security handoff. There is no in-app arbitrary file removal, quarantine restore, policy/exclusion edit or offline reboot.

### Narrow graceful app relief

- **VLC media player only** is initially allowlisted. Review inspects at most eight VLC-named processes; everything unknown, inaccessible, nonstandard or unverified is omitted, not classified unnecessary.
- Native eligibility requires a canonical standard Program Files/Program Files (x86) `VideoLAN/VLC/vlc.exe`, no reparse-point ancestry, matching VLC product/original filename, machine uninstall registration with VideoLAN publisher and matching install location, and a valid VideoLAN Authenticode signer. A signed file alone or a process name alone is insufficient.
- Owner SID and session must match the current user/session. Elevated or critical processes are excluded. A nonzero main window must belong to that PID. Other products and protected/unknown roles are not targets.
- Each row invokes its own confirmation with app name, PID, executable path and start time, plus unsaved-work/recording and non-reversibility warnings. The model revalidates the entire identity after confirmation. The native adapter repeats trust and identity checks, retains a process handle and read-only image lock, and immediately rechecks PID/start/image/session/window before `CloseMainWindow`.
- A false CloseMainWindow result is not success. A true result means a request was sent, not that the app exited. A five-second bounded wait distinguishes verified exit from unverified/prompting/refusing behavior. No force-kill, tree close, automatic closing, service disable, working-set trim or standby flush exists in this feature. No saved-RAM or speedup claim is inferred.
- No live optional app was closed to validate this adapter. Actual signed-VLC metadata compatibility and native races remain VM gates below.

### Audit and lifecycle

`rescue-actions.json` is an atomic, bounded local action journal (last 100 records). Fixed action names/outcomes and UTC timestamps are stored, not process command lines, owner identifiers, executable paths, scan IDs, resource paths or raw provider errors. Audit persistence is required before requesting an action; write failure blocks initiation. A requested-but-unfinished audit is not a completion record. Independent store instances share an in-process write gate. Explicit injected fixture paths may write scratch audit files in safe mode; native actions remain independently blocked.

Dashboard navigation now cancels its diagnostic read, with a regression test. Memory/Defender action reads cancel on navigation and window close. Existing Options, manual diagnostic export, read-only memory sampling and protection reads remain intact. Safe mode was not redefined as an absolute prohibition on explicitly authorized diagnostic exports or fixture file writes.

## Verification evidence

Evidence directory: `C:/Users/blunt/AppData/Local/hermes/cache/scratch/wincare-actions/`.

- `final-tests.log`, `actions-final-second.trx`: **289/289 passed**, no skipped tests. The test runner warned that it was replacing the earlier same-named TRX; this is an evidence-filename warning, not a build warning.
- `release-build.log`: Release, **0 warnings / 0 errors**.
- `portable-publish.log`: normal SDK-targeted self-contained single-file win-x64 publish; no custom runtime override, restoration or dependency changes.
- `render.log`, `views/*.png`, `actions-contact.png`: actual WPF renders, not mocked screenshot responses. Action states include enabled/requested/running/completed/detections, app-request outcome, native-safe-disabled controls, and 980×650 minimum / 1180×760 standard windows. Captures exclude the native frame.
- The current eleven-action contact sheet and full-size enabled-app, safe-disabled-Defender and minimum-Defender captures were visually inspected. Earlier full-size app-safe-disabled and running/minimum-app captures were also inspected before the renderer-only sidebar-selection correction. No overlapping action controls were observed. Content beyond the vertical viewport remains scrollable; minimum-size screenshots intentionally include partially scrolled content. Narrow rendered-layout verdict: **90/100, PASS**. Native keyboard/modal/focus, accessibility, DPI, complete scroll journeys and independent visual review remain **unverified**, not passed.
- `packaged-launch-result.json`: final verification child PID **14148**, HWND **2820262**, responsive message probe, WM_CLOSE sent only to that child, exit **0**, isolated data directory empty.
- `evidence-summary.json`: parsed TRX counters, source hashes, package hashes, screenshot hashes and runtime configuration. Runtime included by the normal publish: .NET / WindowsDesktop **8.0.34**; this is not a latest-runtime/security-approval claim.
- `git diff --check` returned 0; it printed existing line-ending normalization notices. No clean-worktree assertion is made.

### Test-first and issue history

Observed RED→GREEN steps included the missing scan command surface, ambiguity persistence, missing eligible-app/close behavior, Dashboard navigation cancellation, window-close cancellation of new action reads, explicit-path audit persistence, and native process-runner injection. Additional injected tests cover wrong provider/time/user/kind/baseline, wrong Scan ID, cancellation versus monitoring, pre-existing/incomplete scan history, permission failure, stale eligibility/identity changes, refusal, audit failure, unknown apps and safe native boundaries. Supplemental tests added after those tracer slices are not claimed as a separate RED run for every method.

The first full suite returned **285 passed / 1 failed**: pre-existing `LayoutFitTests.Nothing_renders_past_the_right_edge(1600)` reported the animated ProgressBar template rectangle `Animation` at right 1697. An unchanged-source rerun passed **286/286**; the test was not weakened or removed. After adding the required low-level injectable runner and its tests, the final suite passed **289/289**. Treat that timing-sensitive layout assertion as a retained test-stability observation, not proven fixed.

## Artifact

`C:/Users/blunt/Desktop/wincare-desktop/artifacts/rescue-actions-portable/WinCare Pro.exe`

- **137699050 bytes**
- SHA-256: **618c35b137b2ffad28ce29581c1252dab83e0c9eb5b860285345cc682a4b250c**
- Keep native sidecars with it. Use `--safe-mode` and an isolated `WINCARE_DATA_ROOT` for inspection.

## True limitations and remaining manual VM gates

1. **Conservative event-history gate:** the current reader retrieves at most 257 scan events and refuses initiation if that history is truncated, inaccessible, or contains an unmatched prior start. A machine with a long retained scan history may therefore require the Windows Security workflow even when idle. No log clearing or policy change is performed to work around this. Removing this limitation safely needs a provider-aware incremental/baseline design, not simply ignoring incomplete evidence.
2. Native Quick/Full commands, standard-user denial, managed/passive modes, XML field values, localized scan-parameter text, real user attribution, existing/concurrent scans, event-log rollover/clear/delay and eight-hour wrapper timeout require disposable-VM validation. Unexpected/localized representations fail to unknown rather than being guessed. Exact caller ownership remains inherently unproven by the current event correlation.
3. Real detection-provider schemas/permissions, accessible Protection history and supported remediation/restore behavior require a VM. No actual malware test/removal or malware-free claim was made.
4. Verify installed signed VLC metadata across supported versions/architectures, registration variants, certificate failures, same-user/session enforcement, elevated/critical rejection, PID/HWND recycling, reparse/renamed-image races, exit refusal/save prompts and post-close restart behavior in a VM. Portable/unsigned/nonstandard VLC and all other apps deliberately remain manual. Handle-based checks reduce races; adversarial native timing is not certified.
5. Verify real confirmations, keyboard/tab/focus/screen-reader behavior, high contrast, 125–200% DPI, complete vertical-scroll access and native shell handoff. Renders and direct fake command calls do not prove those interactions.
6. Scan monitoring is manual and session-local, not a resumable background scan manager. The minimal audit intentionally contains no persisted Scan ID. After application restart or unknown outcome, use Windows Security; no completion is inferred from the audit.
7. Parent independent security/code review remains open. Earlier cleanup/startup/privacy/DNS/scheduler native, administrative, rollback and destructive VM gates remain open and unchanged. This artifact is not signed, installed or released.

## Official references consulted

The configured extraction tool failed with a search-only-backend error; HTTPS retrieval of these official Microsoft pages succeeded and raw pages were retained under `docs/`. No successful `web.run` invocation is claimed in this subagent environment.

- `https://learn.microsoft.com/en-us/powershell/module/defender/start-mpscan?view=windowsserver2025-ps` — documented QuickScan/FullScan values and job semantics.
- `https://learn.microsoft.com/en-us/defender-endpoint/troubleshoot-microsoft-defender-antivirus` — events 1000 started, 1001 completed, 1002 stopped, with Scan ID, parameters and user.
- `https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.process.closemainwindow?view=net-8.0` — normal window-close request; application may prompt, refuse or remain running.
- `https://learn.microsoft.com/en-us/powershell/module/defender/get-mpthreatdetection?view=windowsserver2025-ps` — active and past detection history, not a new scan verdict.

## Files changed in this pass

New: `Services/DefenderScan.cs`, `Services/OptionalAppActions.cs`, `Services/NativeOptionalAppActions.cs`, `Services/RescueAudit.cs`; `ViewModels/DefenderViewModel.Actions.cs`, `ViewModels/MemoryViewModel.Actions.cs`; `WinCareDesktop.Tests/DefenderActionTests.cs`, `OptionalAppActionTests.cs`, `RescueActionTests.cs`; `Tools/RenderShots/Program.Actions.cs`; this report.

Modified: existing Defender/Memory view models and XAML, `Services/DefenderStatusReader.cs`, `ViewModels/DashboardViewModel.cs`, `MainWindow.xaml.cs`, `Tools/RenderShots/Program.cs`, `README.md`, `RESCUE-UPGRADE.md`, `RESCUE-DESIGN.md`. Existing other uncommitted files were not reset or committed. Scratch scripts/docs/logs/captures and the new portable artifact are additional generated evidence.
