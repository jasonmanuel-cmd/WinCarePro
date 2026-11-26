# WinCare usefulness upgrade — executed results

## Outcome
Implemented the bounded scope in `USEFULNESS-UPGRADE.md` in the existing WPF application, preserving prior uncommitted work. **Local candidate verified, not unrestricted product/release approval.** No score inflation, promised speedup, causal diagnosis or security clearance is claimed.

- **220 executed / 220 passed / 0 failed / 0 skipped**, parsed from `usefulness-verified.trx` (Debug tests).
- **Release build: 0 warnings, 0 errors**, exit 0.
- **Normal SDK win-x64 self-contained single-file publish: exit 0**, no runtime override, no project dependency changes, no restore/download needed in this pass.
- Build runtimeconfig includes **Microsoft.NETCore.App 8.0.34** and **Microsoft.WindowsDesktop.App 8.0.34**. These are the SDK-targeted versions, not a latest/security approval claim.
- **24 actual WPF PNG captures** generated and inspected: existing eight views, existing six fixture states, ten new guidance/export fixture states. Final source captures are in `views-final/`.
- Packaged executable launched in isolated safe mode: PID **8028**, HWND **1640724**, responded to a bounded window-message probe, closed through WM_CLOSE, exit **0**. Isolated data contained only the pre-existing startup `environment.txt`; no crash log. No diagnostic export of host private data was made.

## Exact behavior added

### 1. Prioritized evidence and explanations
`DashboardGuidance` consumes a detached typed snapshot. It orders disk headroom before RAM demand, optional privacy preferences, then missing evidence. Disk threshold: at least 90% used or below 10 GB free; RAM threshold: at least 85% used. Rows include their own sampled measurements, explain uncertainty and link to existing Disk Analyzer, App Remover or Privacy Shield panels. Links navigate only; they do not clean, uninstall, kill a process or change a preference.

Missing/invalid/non-finite metrics are unknown, not healthy zeroes. Unknown privacy is not asserted to be on/off. No-findings copy explicitly says this is neither a security check nor proof of overall health. Samples older than one minute or ahead of the clock suppress actionable recommendations and request a fresh scan. Freshness is re-evaluated on the existing UI timer and navigation without recreating unchanged guidance rows, avoiding needless keyboard-focus loss.

The existing numeric score algorithm was not changed. Its badge is labeled **HEURISTIC**, header **Score**, and explanatory copy states its limits. Missing RAM/storage capacity displays Unknown in dashboard cards and hides the displayed score. Existing live cards remain separate observations from the manually sampled guidance; the new report never treats them as the same timestamped sample.

### 2. Read-only rescan with lifecycle feedback
The old dashboard refresh command now uses a background `DiagnosticReader`, reusing existing RAM, system-drive and two privacy-setting readers. It performs no temp-file enumeration, external probe or system mutation. Initial window load requests the same scan. Busy status, disabled repeat action, Cancel, success/unknown and sanitized error feedback are visible. A cancelled or failed scan retains the previous sample; cancellation discards late results. Closing the window requests cancellation.

Cancellation is cooperative **between** Windows reads. It cannot interrupt a blocked native query or guarantee a hard scan deadline. The session is a UI-thread controller, not a general multi-threaded service API. Individual failed probes become unknown while other readings remain usable; raw exception text is not exported.

### 3. Previewed, user-controlled diagnostic export
**Preview local report…** opens a read-only JSON preview and disclosure. **Save report as…** opens the native Save As dialog with overwrite confirmation. Cancel writes nothing. Saving writes the exact preview even if the user rescans; reopening the preview updates it. Save-in-progress blocks duplicate writes/preview replacement; failure retains the preview and reports a sanitized actionable error.

The payload is an allowlist: schema, sample/review UTC times, numeric RAM/system-drive values, two privacy states, constant guidance templates and disclosure. It does **not** serialize the internal environment report, machine/user identity, hardware IDs, paths, application/process lists, command lines, credentials, logs or raw exceptions. No upload client or network request was added. Measurements/times can still be sensitive; users must review before sharing. Destination syncing/sharing remains under the user's filesystem/provider configuration, not an app-controlled privacy guarantee.

A staging file beside the chosen destination is moved into place only after writing. Fixture tests confirm a locked destination remains unchanged and staging is removed. This is not a crash/power-loss durability guarantee. Export success/failure/cancellation tests use only synthetic fixtures or injected writers, never actual host diagnostic exports.

## UI evidence
Retained existing theme, fonts, sidebar, Card/Surface/Primary brushes and layout direction. New controls are native focusable buttons with explicit automation names and inherited visible focus visuals. Status text wraps and has polite live-region metadata; preview is a labeled read-only scrollable TextBox. These are structural accessibility improvements, **not** a screen-reader/WCAG/native keyboard certification.

Final captures are **1165 × 723 at 96 DPI**. Inspected final dashboard/export individually and all captures through six contact sheets derived from the actual PNGs. Initial hero text crowded the cleanup action; a right margin was added and the final capture confirms separation. No blocking overlap was observed in the captured states. Long guidance and lower maintenance controls use the existing scroll surface. Export captures intentionally scroll to the preview; partially visible hero content above the viewport is scrolling, not hidden controls.

New fixture captures: pressure, unknown, stale, clear/no threshold recommendation, not scanned, busy, cancelled, failed read, export preview, export failure. Synthetic fixture subtitles explicitly distinguish them from host observations. Existing panels remained readable. Captured-surface assessment: **92/100**, bounded rendered-state review only; full visual acceptance remains **HOLD** for the native/modal/keyboard/DPI matrix. RenderShots' inherited `hover: 0/0` line is not hover evidence. No distinct second visual reviewer participated; parent independent review remains pending.

## Issues encountered and resolved
1. The first full test run had **215 passes / 2 failures** in existing single-instance tests. Read-only process inspection found the older `completion-portable-normal/WinCare Pro.exe` already running as PID 15040. It was **not closed or killed**. A failing regression was added, then only safe-mode mutex identity was scoped to a SHA-256-derived token of the isolated data root. Normal production identity is unchanged. Safe-mode sessions sharing a root still share a mutex. This permits verification without colliding with the user's running normal app. Origin of the initial failure: environmental interaction in this checkout; no clean merge-base reproduction was run.
2. A later new locked-destination regression expected only IOException; this Windows run returned UnauthorizedAccessException. The assertion now accepts either explicit refusal while still requiring the original file to remain intact and staging to be removed. No production guard or assertion of preservation was weakened. Final full run passed all 220 tests.
3. One patch failed to match CRLF context and was retried with a smaller exact anchor; no reset/overwrite of prior work was performed.

## Artifact and evidence

**Portable directory:** `C:/Users/blunt/Desktop/wincare-desktop/artifacts/usefulness-portable/`

**Executable:** `C:/Users/blunt/Desktop/wincare-desktop/artifacts/usefulness-portable/WinCare Pro.exe`

- Size: **137592554 bytes**.
- SHA-256: **65565aa40dde8ad73318d0d6b3a06fb44ffda673476f41975dbd211321eec363**.
- Keep native DLL sidecars together with the executable. Launch with `--safe-mode` for read-only inspection; use a separate `WINCARE_DATA_ROOT`.

**Evidence root:** `C:/Users/blunt/AppData/Local/hermes/cache/scratch/wincare-usefulness/`

- `USEFULNESS-UPGRADE.md` preceded production edits.
- Test-first behavioral failures and green runs: `guidance-behavior-red.log`, `guidance-green.log`, `unknown-red.log`, `unknown-green.log`, `scan-red.log`, `scan-green.log`, `reader-red.log`, `reader-green.log`, `export-red.log`, `export-green.log`, `ui-red.log`, `ui-green.log`, `focus-stability-red.log`, `live-unknown-red.log`, `evidence-label-red.log`, `isolation-red.log`; all are covered by the final full green suite.
- Final: `usefulness-verified.trx`, `verified-tests.log`, `release-build.log`, `portable-publish.log`, `final-render.log`, `views-final/*.png`, `contact-0.png` through `contact-5.png`.
- `run.py` and `*-command.json`: exact executable/arguments and exit codes for each dotnet call.
- `launch-and-record.py`, `packaged-launch-result.json`, `packaged-host-trace.log`, `published.runtimeconfig.json`, `evidence-summary.json`: packaged launch, actual runtimeconfig, source/package/capture SHA-256 manifests and parsed counts.
- `git diff --check` returned exit 0; only existing Git LF/CRLF normalization notices were printed. New source files were included in the successful compiler/test runs.

Commands from this checkout (executable `C:/Program Files/dotnet/dotnet.exe`):

```text
test WinCareDesktop.Tests/WinCareDesktop.Tests.csproj --no-restore --verbosity quiet --logger trx;LogFileName=usefulness-verified.trx --results-directory C:/Users/blunt/AppData/Local/hermes/cache/scratch/wincare-usefulness
build WinCareDesktop.csproj -c Release --no-restore --verbosity minimal
publish WinCareDesktop.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true --no-restore -o C:/Users/blunt/Desktop/wincare-desktop/artifacts/usefulness-portable --verbosity minimal
run --project Tools/RenderShots/RenderShots.csproj --no-restore -- C:/Users/blunt/AppData/Local/hermes/cache/scratch/wincare-usefulness/views-final
```

For every invocation, `run.py` set `DOTNET_CLI_TELEMETRY_OPTOUT=1`, `DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1`, `DOTNET_GENERATE_ASPNET_CERTIFICATE=false`, scratch TEMP/TMP and safe mode/isolated data root. Module-initialized tests use unique scratch stores; RenderShots forces safe mode. Inspected safety composition, native mutation guards, scheduler non-activation, cleanup fixture roots and synthetic process tests before full execution.

## Files changed by this pass

New production files: `Services/DashboardGuidance.cs`, `DiagnosticReader.cs`, `DiagnosticSession.cs`, `DiagnosticSession.Export.cs`, `DiagnosticExport.cs`.

Adapted existing production/UI: `ViewModels/DashboardViewModel.cs`, `ViewModels/MainViewModel.cs`, `Views/DashboardView.xaml`, `MainWindow.xaml`, `MainWindow.xaml.cs`, `Services/SingleInstance.cs`.

New tests: `WinCareDesktop.Tests/GuidanceTests.cs`, `GuidanceUiTests.cs`, `DiagnosticScanTests.cs`, `DiagnosticReaderTests.cs`, `DiagnosticExportTests.cs`. Extended `WinCareDesktop.Tests/SingleInstanceTests.cs` and `Tools/RenderShots/Program.cs`.

Docs: `USEFULNESS-UPGRADE.md`, this file, `README.md`. New publish directory and scratch evidence above. Prior completion files and artifacts were retained. No commit, push, signing, install, public release, dependency upgrade, real cleanup/uninstall/registry/privacy/startup/DNS writes, shell repair, OS task, new model install or automatic process killing occurred.

## Remaining gates and separate next phase

- Disposable-VM native/admin/destructive integration, vendor/UAC/reboot behavior, actual rollback and multi-adapter DNS/privacy/startup recovery remain open as documented in `FINISH-UP-RESULTS.md` and `INDEPENDENT-REVIEW.md`.
- Full keyboard focus order, native Save As/modal cancellation/overwrite interaction, screen readers/live announcements, hover, every panel state, scrolling at additional window sizes/DPI and all export filesystem/provider/crash cases remain open. Save dialog wiring is source-inspected; injected dialog cancellation and fixture writes are automated evidence, not a native modal end-to-end test.
- Full scheduler/DST/crash/bounds and adversarial filesystem/TOCTOU matrices remain open. Security/runtime/dependency provenance and distribution approval remain separate.
- Parent independent review of this implementation is pending; local tests and screenshots do not replace it.
- The user's later **older-PC Rescue / Defender scan-remediation / stronger removal protection / Options** roadmap is explicitly deferred to a separate phase. Existing typed snapshot, pure guidance builder, read-only reader and injected diagnostic session are integration seams. No Defender operation, process-management feature, Options panel or rescue automation was added here. Never imply like-new performance or add kill-all, global working-set flushing, arbitrary malware deletion, or security/service disabling.
