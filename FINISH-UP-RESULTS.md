# Bounded finish-up results

## Outcome

The requested diagnostic correction, normal SDK-targeted portable packaging, and independent spot-review are complete. **This is a verified local candidate, not full product acceptance or an unrestricted release approval.** This addendum supersedes the diagnostic/runtime/count/artifact limitations in the earlier reports; their unexecuted native/manual and UI gates remain open.

- Full isolated suite: **191 executed, 191 passed, 0 failed, 0 skipped** (TRX parsed).
- Release build: **0 warnings, 0 errors**, exit 0.
- Normal official-feed restore and self-contained publish: exit 0, no cached-runtime target override.
- Installed SDK **8.0.428** targeted **Microsoft.NETCore.App 8.0.34** and **Microsoft.WindowsDesktop.App 8.0.34**. These are SDK-targeted versions, not a claim of latest or fully secure runtime.
- Packaged executable launched with safe mode and isolated data, responsive window handle **2753084**, then closed cleanly, exit 0. Only `environment.txt` appeared in the isolated data root; its history path and memory label are correct.

## Diagnostic correction and regression evidence

`Services/EnvironmentReport.cs` now uses `new HistoryStore().FilePath`, sharing the actual default path resolution through `SafetyMode.DataRoot` without loading or saving history. Memory copy reads `Memory maintenance: WinCare working-set trim only (not probed)`. The obsolete compression-process probe and helper were removed entirely; report generation performs no memory operation or spawned probe.

New `WinCareDesktop.Tests/EnvironmentReportTests.cs` tests were implemented vertically:

1. History-path assertion failed on the original production-only display (`finish-path-red.trx`: 1 failed), then passed after correction (`finish-path-green.log`).
2. Memory-label assertion failed on the standby wording (`finish-label-red.trx`: 1 failed, 1 passed), then both passed (`finish-diagnostic-green.log`).

The final full suite includes both regressions and all previous tests. An initial diff check detected an extra EOF blank line introduced while removing the helper. It was removed, then the full tests/build/publish/launch pass was repeated for the exact final source. No failing test was skipped or weakened.

## Independent spot-review of prior worker fixes

Claim source: the user's bounded finish-up request and the corrected contracts in `INDEPENDENT-REVIEW.md`. This reader is distinct from the prior worker. It did not initiate another audit or delegate to more agents. No additional confirmed substantive defect was found in the specified correction paths, and no unrelated production code was changed.

| Checked surface | Evidence and result | Boundary |
|---|---|---|
| Privacy Unknown | `Services/SystemOptimizerService.cs:408–414,457–462`, `Models/Models.cs:69–80`, `ViewModels/MainViewModel.cs:143–166`, `ViewModels/PrivacyViewModel.cs:18–35`; nullable readback flows to Unknown, only true counts as protected, health text does not assert Unknown is on. `PrivacySafetyTests` passed in the final full run. | Native denied/policy registry behavior was not exercised; fixtures supply values/errors. |
| Store partial inventory | `Services/AppxService.cs:45–118`, `ViewModels/AppUninstallViewModel.cs:72–100`; process/JSON/identity failures reach the partial-inventory message while the successful source is retained. `UninstallSafetyTests` passed. | Real Store/version/vendor matrix not exercised; readable inventory is not proof of native uninstall behavior. |
| Structured uninstall outcomes | `Models/UninstallOutcome.cs:3–20`, `ViewModels/AppUninstallViewModel.cs:153–215`; only VerifiedRemoved removes rows; Pending/RebootRequired/Unknown/Failed retain them with distinct summary/history; cancellation truthfully stops waiting rather than claiming the vendor stopped. Mixed outcomes and exit classification tests passed. | Registry-list absence remains the implemented verification definition, not proof all vendor files/processes are removed. Native bootstrap/reboot behavior is a manual gate. |
| Scheduler cancellation/disposal | `Services/SchedulerRuntime.cs:16–55,84`, `ViewModels/SchedulerViewModel.cs:19–39`; active run blocks re-enable, disable preserves disabled status, cancellation recorded, active CTS survives until runner completion, repeated Dispose is guarded. `SchedulerLifecycleTests` passed. | UI-dispatcher use is inspected; arbitrary multithreaded callers, DST/crash/failure/bounds matrix remain unproved. |

Correctness/risk axis: specified corrections checked clean at source/fixture level. Spec axis: bounded task satisfied, including normal-runtime packaging. Full-application verdict remains **no_ship for unrestricted release**, because previously stated manual integration/UI acceptance gates are still missing. Independent validation of this reader's new diagnostic implementation is unavailable; its tests and packaged output were checked by this reader, not a third reviewer.

## Safety composition inspected before execution

Read `SafetyMode`, `HistoryStore`, App startup, module-initialized test composition, test service seams, scheduler runtime/view-model, cleanup and native mutation adapters, and destructive/process test call sites. The test module initializes safe mode and a unique scratch data root. Native privacy/startup/DNS/recycle/memory/Store/classic-uninstall mutations are guarded. Default cleanup roots are empty in safe mode; actual delete tests use explicit scratch fixtures. Scheduler construction does not arm automation and native enable is blocked in safe mode. Synthetic process tests only produce output, sleep, exit or request a missing executable.

TEMP/TMP were redirected to the Hermes scratch evidence folder for all dotnet execution and launch; telemetry, first-time setup and ASP.NET certificate generation were suppressed. Generic process execution is not a sandbox; these claims concern the inspected composition. No real cleanup, uninstall, DNS/privacy/startup change, OS task, signing, SDK installation, global configuration edit, project package-version edit, commit or release was performed. The expressly authorized official NuGet restore wrote required runtime packages to `C:/Users/blunt/.nuget/packages/`. The old cached-runtime artifact was left intact.

## Normal portable artifact

Directory: **`C:/Users/blunt/Desktop/wincare-desktop/artifacts/completion-portable-normal/`**

Executable: **`C:/Users/blunt/Desktop/wincare-desktop/artifacts/completion-portable-normal/WinCare Pro.exe`**

- Size: **137559786 bytes**.
- SHA-256: **`7193eeeb5dea3d84e13288d924251a60369199591586df456cff4108c83a3c40`**.
- Retain the published native DLL sidecars with the executable; self-contained single-file bundling does not mean one filesystem file.
- Actual RID build runtimeconfig contains both included frameworks at 8.0.34; deps lists both 8.0.34 runtime packs; project assets target 8.0.34 runtime downloads into the standard cache. Packaged launch host trace confirms bundle startup with apphost/hostfxr/hostpolicy 8.0.34 and self-contained execution. This is stronger than inferring runtime from SDK version alone.
- Runtime release/security approval was not researched or asserted. Normal packaging is now unblocked; it does not automatically establish production-runtime security approval.

## Commands and durable evidence

Evidence root: **`C:/Users/blunt/AppData/Local/hermes/cache/scratch/wincare-verification/`**. `finish-verify.py` supplies the isolation environment and records exact command arrays and exit codes in `finish-*-command.json`; corresponding logs contain actual output.

Commands, run from `C:/Users/blunt/Desktop/wincare-desktop` with `C:/Program Files/dotnet/dotnet.exe`:

```text
restore WinCareDesktop.csproj -r win-x64 --source https://api.nuget.org/v3/index.json --verbosity minimal
test WinCareDesktop.Tests/WinCareDesktop.Tests.csproj --no-restore --verbosity quiet --logger trx;LogFileName=finish-full-tests.trx --results-directory C:/Users/blunt/AppData/Local/hermes/cache/scratch/wincare-verification
build WinCareDesktop.csproj -c Release --no-restore --verbosity minimal
publish WinCareDesktop.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true --no-restore -o C:/Users/blunt/Desktop/wincare-desktop/artifacts/completion-portable-normal --verbosity minimal
```

No CustomAfterMicrosoftCommonTargets, RuntimeFrameworkVersion or cached-runtime override was passed. Restore used SDK defaults; no network failure occurred.

Key evidence files under that root:

- `finish-full-tests.trx`, `finish-full-tests.log`, `finish-release-build.log`.
- `finish-normal-restore.log`, `finish-normal-publish.log`.
- `finish-normal-project.assets.json`, `finish-normal.runtimeconfig.json`, `finish-normal.deps.json` (preserved real build assets).
- `finish-normal-launch.py`, `finish-normal-launch-result.json`, `finish-normal-host-trace.log`.
- `finish-normal-final-launch-data/environment.txt`.
- `finish-evidence-summary.json`: parsed TRX counters/failure messages, inspected-source hashes, runtime evidence and package hashes.
- `finish-diff-check.log`: final source check exit 0; Git LF/CRLF notices are not whitespace failures.

The final suite was Debug, with a separate Release application build and portable publish. No new UI captures were made; the prior worker's captures remain prior evidence, not revalidated here.

## Remaining acceptance gates — not passed

1. **Disposable-VM native/admin integration:** COM recycle with no permanent fallback across volume/ACL/UAC conditions; vendor bootstrap/uninstall/reboot behavior; real startup/privacy registry changes and recovery/crash windows; ordered static/DHCP DNS restore across adapters/policies, IPv6 noninterference; actual rollback. None should be tested on this user's real settings as part of this bounded task.
2. **Complete UI matrix:** every panel's empty/loading/error/partial/busy/confirmation state; native modal focus/dismissal, keyboard accessibility, hover, scrolling and additional DPI/resolutions. Responsive safe-mode launch is not that matrix.
3. **Remaining adversarial coverage:** DST, corrupt/unwritable schedule, crash/failure/retry and production-runner file/byte/time bounds; filesystem TOCTOU and persistence boundaries. Limits remain cooperative, not a hard I/O timeout.
4. **Release approval:** runtime/dependency security and provenance policy approval, signing/distribution decisions if required. No latest-runtime, fully secure, installer, CI, merge or release claim is made.

`COMPLETION-WORK.md` was left unchanged. Changes specific to this pass are listed in `IMPLEMENTATION-FILES.json` under `finish_up_paths`; earlier implementation changes were retained.
