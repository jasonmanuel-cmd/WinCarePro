# Independent completion-candidate review

## Subsequent bounded finish-up — current status

A distinct reader spot-reviewed the prior privacy Unknown, partial inventory/structured uninstall outcomes and scheduler cancellation/disposal corrections; no additional confirmed substantive defect was found in those specified paths. The remaining diagnostic defect was fixed test-first, removing the obsolete process probe and using the actual HistoryStore path. Final evidence is in `FINISH-UP-RESULTS.md`: **191/191 passing isolated tests**, zero-warning/error Release build, normal official-feed restore/publish, and successful isolated safe-mode launch.

Normal artifact: `C:/Users/blunt/Desktop/wincare-desktop/artifacts/completion-portable-normal/WinCare Pro.exe`, SHA-256 `7193eeeb5dea3d84e13288d924251a60369199591586df456cff4108c83a3c40`. Actual build assets and packaged host trace establish SDK-targeted .NET/WindowsDesktop **8.0.34** without a cached-runtime override. The old offline packaging blocker and residual diagnostic defect below are resolved; latest/security approval is not claimed. Native/manual, full UI and adversarial acceptance gaps below remain. Independent validation of the newest diagnostic correction is unavailable beyond that reader's tests/launch. Earlier runtime/hash/count statements are retained as historical evidence only.

## Prior verdict (superseded only as stated above)

**Local safe-mode candidate verified; not 100% accepted and not production-ready (`no_ship` for an unrestricted release).** Claim source: `COMPLETION-WORK.md`, completion gates 1–6 and refinements 35–43. No root `REVIEW.md` policy was supplied; a blocking gap here means a stated completion gate lacks evidence, not that an optional enhancement was missed.

This reader independently inspected the previous implementation before execution, reproduced its baseline (166 passing tests), then made narrowly scoped corrections. Final full suite: **189 executed, 189 passed, 0 failed, 0 skipped**. Release build: **0 warnings, 0 errors**. Offline cached-runtime portable publish, safe-mode launch and rendering succeeded. The working tree is uncommitted on base `06c8acc738c1c07ea02e15280619fe140c5110d5`; exact source hashes are recorded, not represented as a new commit.

Independent validation of **this reader's subsequent fixes** is unavailable: the parent must review them if a distinct second validator is required. This is not a release, CI, merge, signing or native destructive-integration approval.

## Confirmed defects corrected

Paths below are relative to `C:/Users/blunt/Desktop/wincare-desktop/`. Red TRX artifacts are under the evidence directory defined below. These were defects in the received candidate, not claims of regressions introduced relative to the old Git base.

| Priority / confidence | Reproduced defect and correction | Source / regression evidence |
|---|---|---|
| P1 / high | Privacy read errors collapsed into false/unprotected. Nullable readback now displays **Unknown**, including absent/unsupported values, excludes them from protected counts, and avoids claiming Advertising ID is on in health findings. | `Services/SystemOptimizerService.cs:408–414,457–462`; `Models/Models.cs:69–80`; `ViewModels/MainViewModel.cs:152–165`; `Views/PrivacyView.xaml`; `PrivacySafetyTests`; `independent-privacy-red.trx` |
| P1 / high | Store failures were logged and returned as an empty success. Enumeration now fails explicitly on process/stderr/JSON/identity errors; the view loads sources independently, retains the available source and labels inventory **Partial**. | `Services/AppxService.cs:45–118`; `ViewModels/AppUninstallViewModel.cs:72–101`; `UninstallSafetyTests`; `independent-inventory-red.trx`, `independent-store-red.trx` |
| P1 / high | Re-enable while a cancelled run was still active could replace its session state; repeated Dispose threw. Re-enable is refused until the previous runner finishes, disabled status is retained, cancellation is recorded, CTS disposal is bounded and Dispose is idempotent. | `Services/SchedulerRuntime.cs:15–57,84`; `SchedulerLifecycleTests`; `independent-scheduler-red.trx`, `independent-dispose-red.trx` |
| P2 / high | Uninstall summary flattened pending/reboot/unknown outcomes into failure, and a reboot result could be counted as removed. Structured `UninstallOutcome` now retains all non-verified rows, reports distinct counts and writes the state into display-only history. Cancellation and unreadable inventory remain Unknown; reboot outcomes remain RebootRequired. | `Models/UninstallOutcome.cs`; `ViewModels/AppUninstallViewModel.cs:145–215`; `UninstallSafetyTests`; `independent-outcomes-red.trx`, `independent-batch-red.trx` |
| P2 / high | Actual app-remover screenshot showed only `0` on the destructive button: WPF Content binding did not apply the intended StringFormat. The label is now a bound TextBlock; long status text wraps beside rather than underneath the cancel control. | `Views/AppUninstallView.xaml:43–65`; `BindingContractTests`; `independent-uninstall-label-red.trx`; final `appremover.png` and `fixture-apps-busy.png` |
| P1 / high | Startup destination collision detection considered only supported REG_SZ entries and could miss a value of another kind. Presence is now checked separately using value names, before copying. | `Services/StartupManagerService.cs:25–36,117–126`; `StartupSafetyTests`; `independent-startup-red.trx` |
| P2 / high | Actual startup rendering listed `desktop.ini` as an enabled startup program. Enumeration now omits this shell metadata file. | `Services/StartupManagerService.cs:94–98`; `StartupSafetyTests`; final `startupapps.png` |

Scheduler fake-clock regressions additionally exercise duplicate due ticks, a backward clock jump, sleep/resume without a catch-up queue, overlapping tick suppression, cancellation, and closing/disposal. All runners are injected fixtures; they do not clean host files. The clock is supplied explicitly to `EnableForSession`/`TickAsync`, rather than changing the machine clock. All waits in the newly added lifecycle tests have a three-second deadline.

README and scheduler consent/view text now distinguish one resumed due attempt from a replay queue. The source manifest's erroneous `pp.xaml.cs` entry was corrected to the existing `App.xaml.cs`. `COMPLETION-WORK.md` was not modified.

## Isolation audit before execution

Reviewed `SafetyMode`, module-initialized test composition, `TestService`, App startup, MainWindow close handling, scheduler construction/runtime, RenderShots, mutation adapters and destructive test call sites before the baseline full suite.

- Test module initialization sets safe mode and a unique scratch data root before constructing WPF objects. History and scheduler defaults use `SafetyMode.DataRoot`; app environment/crash outputs use the same root.
- Default safe-mode cleanup roots are empty. Tests that delete files explicitly use their own scratch fixtures and metadata snapshots. Cleanup is not an arbitrary sandbox: explicit injected roots/delegates are trusted test seams.
- Default registry/privacy/startup writes, DNS changes, recycle calls, memory mutation and scheduler native runner are guarded by `RequireMutationAllowed`. Injected dictionary/delegate tests do not resolve those native mutation adapters.
- Scheduler configuration is loaded disabled, the timer is not started by construction/navigation, and native enable is blocked in safe mode. MainWindow closes telemetry polling and scheduler, and cancels disk scanning.
- Navigation uses genuine read-only host telemetry, disk enumeration, startup enumeration and installed-app enumeration. Appx inventory invokes read-only PowerShell. Process-runner tests invoke synthetic output, sleep, exit and missing-executable commands; they are not uninstall or configuration commands.
- Generic `RunProcessPublic` remains a general process helper, not a security sandbox. Safety claims apply to the inspected composition and commands, not arbitrary calls to that helper.
- No native cleanup, uninstall, registry/privacy/startup/DNS mutation, scheduled task registration, external latency probe, dependency download/upgrade, commit/push/release or installer was requested or executed by this review. No kernel-level pre/post registry or filesystem audit was performed; do not inflate source/fixture evidence into such an audit.

## Acceptance coverage

| Completion gate | Observed coverage | Status / boundary |
|---|---|---|
| 1. Isolated tests/mutations | Pre-execution source audit; module initializer and isolated stores; full suite ran with scratch TEMP/TMP. Native writes guarded in inspected composition. | Pass for the executed safe composition, not a general-purpose sandbox certification. |
| 2. Disk/uninstall/startup | Confirm/changed/protected-target tests; recycle-only adapter source; structured uninstall classification and mixed batch; Store partial failure; startup preserved commands/collision/metadata fixtures. | Implemented and fixture-verified; native Recycle Bin/vendor/UAC/registry behavior remains manual. |
| 3. Privacy/DNS | Unknown display and count tests; exact privacy journal/readback/undo/conflict fixtures; stable DNS adapter binding and automatic-mode/conflict fixture. IPv4-only source inspected. | Partial integration coverage. Native DNS/static multi-server/restart/policy matrix not proved. |
| 4. Auto-clean lifecycle | Fresh consent, inactive legacy config, explicit due times, reversal/dedup/sleep, overlapping ticks, cancellation, re-enable guard, idempotent disposal; source shows temp-only item/byte/inspection/time limits. | Lifecycle fixture checks pass; production runner limits and complete DST/crash/failure matrix not executed. |
| 5. Truthful maintenance/history/labels | Snapshot/changed/foreign/locked-file accounting fixtures; confirmation-decline fixture; awaited rollback and property bindings; disk/network/memory labels read and rendered. | Covered at source/fixture level; real undo/native filesystem semantics remain unverified. |
| 6. Build/UI/local launch | Full tests, zero-warning Release build, self-contained cached-runtime publish, 14 actual WPF PNGs inspected, responsive portable window and clean close. | Local verification passes; full state/DPI/modal matrix and current-runtime production artifact remain open. |

## Executed evidence

Evidence directory: `C:/Users/blunt/AppData/Local/hermes/cache/scratch/wincare-verification/`.

- `independent-baseline.trx`: 166 passed before this reader's changes.
- `independent-final.trx`, `independent-final-tests.log`: **189 passed / 0 failed / 0 skipped**, exit 0; TRX counters parsed, not inferred from filenames. Test run was Debug; the separately verified application build/publish was Release.
- `independent-release-build.log`: **0 warnings / 0 errors**, exit 0.
- `independent-portable-publish.log`: cached-runtime self-contained publish, exit 0.
- `independent-render.log`: all requested captures produced, exit 0.
- `independent-diff-check.log`: exit 0; only Git LF-to-CRLF normalization notices.
- `independent-commands.json` and `independent-verify.py`: exact executable/arguments and logs. Calls use `C:/Program Files/dotnet/dotnet.exe`, `--no-restore`, telemetry/first-time/certificate suppression, and scratch TEMP/TMP. No restore or download was needed in this pass.
- `independent-source-capture-manifest.json`: hashes of 89 source/project/XAML files and 14 PNGs, repository/base identity and capture time.
- `independent-portable-launch.py`, `independent-portable-launch-result.json`: responsive window `WinCare Pro - System Optimizer & Health Suite`, nonzero HWND 2229258, process exit 0 after CloseMainWindow; isolated launch data contains only `environment.txt`, no crash log.

Portable executable: `C:/Users/blunt/Desktop/wincare-desktop/artifacts/completion-portable/WinCare Pro.exe`.

- Size: **137481962 bytes**.
- SHA-256: `001c331a2161d13c77dbe1595cd9555140a3c727093a25b98dc47f1a21a0bcad`.
- Keep native DLL sidecars together with the executable.
- `bin/Release/net8.0-windows/win-x64/WinCare Pro.runtimeconfig.json` independently confirms included `Microsoft.NETCore.App` and `Microsoft.WindowsDesktop.App` **8.0.30**. This reused the existing cached-runtime target, not a normal current-runtime production restore. Prior worker reported an 8.0.34 apphost; this review did not independently establish apphost binary provenance or the currently approved runtime version. **Do not distribute this as a current-runtime production build.**

## Visual review

Actual images were loaded and inspected, not accepted based on screenshot filenames. All final captures are **1165 × 723**, rasterized at 96 DPI by the WPF harness, under `views-independent/`:

1. `dashboard.png`
2. `diskanalyzer.png`
3. `startupapps.png`
4. `appremover.png`
5. `privacyshield.png`
6. `networkdns.png`
7. `autoclean.png`
8. `historylogs.png`
9. `fixture-privacy-unknown.png`
10. `fixture-apps-partial.png`
11. `fixture-apps-loading.png`
12. `fixture-apps-busy.png`
13. `fixture-apps-outcomes.png`
14. `fixture-apps-empty.png`

Captured-surface visual score: **92/100**. Existing UI direction is intact. No blocking overlap or clipped critical control was observed in these captures; the uninstall label is now readable, cancellation/status text coexist, Unknown is explicit, and desktop metadata is absent from startup rows. Network is scrollable; lower scroll positions were not separately captured. Fixture images are explicitly synthetic states and **not** evidence that a real vendor uninstall ran. Loading/busy/empty/outcome fixtures directly set UI state; partial inventory uses the injected failing Store loader.

Overall visual acceptance: **BLOCK for incomplete matrix**, despite readable captured surfaces. All-page loading/error/partial/busy/confirmation states, native confirmation focus/dismissal, keyboard accessibility, hover, every scrolling state and additional DPI/resolutions remain unverified. The renderer's `hover: 0/0` output is obsolete helper coverage, not a hover pass. No independent second visual reviewer participated.

## Enumerated unmet acceptance gates

1. **Production-runtime artifact (gate 6 / refinement 42):** blocked by the allowed offline runtime cache. Approved current-runtime restore/publish and provenance/security checks require a separately authorized dependency operation. The local older-runtime artifact is honest verification evidence only.
2. **Native/manual integration (gates 2, 3, 5, 6):** disposable-VM checks still needed for COM recycle/no-permanent fallback across volumes, UAC/vendor bootstrap/reboot behavior, real denied/locked registry, startup recovery/crash boundaries, privacy writes/readback, multi-adapter DNS/static ordered restoration/DHCP/policy/IPv6 noninterference, and real rollback. These were intentionally not performed on this PC and are not automatically code defects.
3. **Complete UI matrix (refinement 43):** missing modal/confirmation, keyboard/focus, native hover, additional DPI/resolutions and every state for every navigation panel. Additional safe fixture capture work is feasible; it is not blocked by native-destructive authorization.
4. **Adversarial/temporal and bounds coverage (gate 4, refinements 36–41):** no exhaustive DST, invalid/corrupt/unwritable schedule, crash/failure/retry or multithreaded-caller matrix; item/byte/time limits are source-inspected, not exercised through an injected bounded native-cleaning runner. Current runtime is used by the UI dispatcher; general thread-safe service use is not claimed. Filesystem TOCTOU and persistence/crash windows remain risks; metadata snapshots are not stable handles. The time bound is cooperative, not a hard timeout for blocked OS I/O.
5. **Residual diagnostic-copy defect (non-blocking, P3/high):** `Services/EnvironmentReport.cs:43,48–50` still prints legacy standby terminology and the normal production history path in safe mode. The observed `independent-portable-launch-data/environment.txt` confirms the wrong displayed path. `Services/HistoryStore.cs:25–28` actually resolves the isolated path, so this is misleading support text, not evidence of a production history write. Fixable separately; no further code changes were made after the final verification round. Helper-local warnings also are not all mirrored into the main event-log panel; history retention is still 50 entries.

Checked-and-clean: executed safe composition, the corrected privacy/Store/scheduler/uninstall/startup contracts above, final tests/build, actual selected renders and portable launch. Could not assess: native mutation semantics, full adversarial/state matrix, current-runtime production readiness and a distinct second-reader validation of these fixes. These boundaries must survive the parent summary; passing test count is not full acceptance.
