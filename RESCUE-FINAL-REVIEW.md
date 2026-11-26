# Rescue final independent review

## Verdict and blocking definition

**Local candidate: NO_SHIP / BLOCK — high confidence.** The reviewed source has two concrete action-boundary defects and one lifecycle correctness defect below. These are code findings, not deferred native-VM questions. The matching portable artifact should not be promoted as a safety-approved action candidate until the findings are resolved and independently checked.

**Production/native release: HOLD separately.** Passing local checks after fixes would still not satisfy the existing disposable-VM, accessibility, signing and release gates. No production approval is given.

Claim sources: `RESCUE-UPGRADE.md:8-19` and `RESCUE-ACTIONS-RESULTS.md:9-37`. No root `REVIEW.md` was found. For this review, a demonstrated violation of fail-closed action initiation, trusted native command selection, or truthful scan-lifecycle handling blocks the corresponding acceptance criterion. This is not a numerical quality score.

## Findings — correctness/risk axis

### F1 — P1 / High: cancelled or expired preparation can still initiate a Defender scan

- **Path / lines:** `ViewModels/DefenderViewModel.Actions.cs:45-55`; related `Services/DefenderScan.cs:67-71`.
- **Evidence:** The last cancellation check precedes the audit write. After `Audit(...)`, the code creates the tracker and calls `_actions.Start(kind)` without checking the preparation token again. The native `Start` API receives no preparation token and uses `CancellationToken.None` for its wrapper.
- **Reachable trigger:** the 35-second preparation deadline expires while the synchronous audit write is delayed, then that write succeeds. The native launch proceeds despite preparation already being cancelled. This is distinct from intentionally allowing an *already launched* scan to continue when observation stops.
- **Observed isolated reproduction:** a scratch console references the freshly rebuilt application assembly and supplies only an injected fake `IDefenderActions`. Its audit callback calls `StopMonitoring()` immediately before returning. `QuickScanAsync()` nevertheless invokes fake `Start` **once**, versus expected **zero**; status remains “Quick: start requested…”. No Defender provider is invoked. See scratch `review-tracker/Program.cs` and `review-fixture-observations.jsonl`.
- **Impact / violated criterion:** cancellation is not a fail-closed pre-action boundary for an operation explicitly disclosed as potentially quarantining/removing threats. Violates upgrade requirements 2, 3 and 5 and the report's bounded/cancellable preparation claim.
- **Required resolution:** propagate or revalidate preparation cancellation at the actual launch boundary after audit persistence; retain the separate rule that stopping observation does not cancel an already initiated Defender scan. Add the deterministic audit-to-launch cancellation regression. No fix was made here.

### F2 — P1 / High: the system PowerShell executable does not pin the executed provider command

- **Path / lines:** `Services/DefenderScan.cs:63,71,75,89-93`; related trust check `Services/NativeOptionalAppActions.cs:85-87`.
- **Evidence:** `RescuePowerShell` selects the system executable and disables profiles, but inherits the parent's module-search environment and runs bare command names. `Start-MpScan` is not imported from or checked against a trusted system module. The same helper supplies provider reads and the signature-verification script.
- **Observed isolated reproduction:** `review-command-resolution.py` creates an inert, scratch-only `Defender/Defender.psm1` exporting a fixture `Start-MpScan`, gives only a child process that scratch `PSModulePath`, and launches the same system Windows PowerShell with `-NoLogo -NoProfile -NonInteractive`. `Get-Command Start-MpScan` resolves to the **scratch module / Function**, exit 0. It never invokes `Start-MpScan`; the fixture function would throw if invoked. Output is retained in `review-command-resolution.json`. Module-analysis cache is redirected to scratch; no user/system module directory, registry or persistent environment was changed.
- **Reachable trigger / prerequisites:** an inherited module path or a user-writable module location contains a shadowing command/module. This requires local control of that environment/module location; it is not a claimed remote exploit or demonstrated privilege escalation. The probe establishes the command-selection failure, not execution of a malicious payload. The production launch uses the same unqualified name.
- **Impact / violated criterion:** a confirmed “Microsoft Defender scan” need not execute Microsoft's scan implementation. Fixing the executable path and passing constant script text does not establish command provenance. The shared read/trust helper also deserves the same restriction rather than assuming `mutation=false` makes imported code read-only. Violates upgrade requirements 2–3 and report lines 13–15's fixed-provider action claim.
- **Required resolution:** restrict module loading/command provenance to trusted system locations and verify imported command origin, including signature-verification dependencies; fail closed on unexpected modules. Add a harmless shadow-module negative fixture. Do not rely solely on `-NoProfile` or a module name qualifier.
- **Supporting primary reference:** Microsoft Learn, *about_PSModulePath* (Windows PowerShell 5.1), retrieved during review and retained as scratch `review-psmodulepath.html`. It documents user-scope module locations, inherited-path construction and recursive module discovery. The URL is `https://learn.microsoft.com/en-us/powershell/module/microsoft.powershell.core/about/about_psmodulepath?view=powershell-5.1`.

### F3 — P2 / High: later event-log watermark regression is not detected

- **Path / lines:** `Services/DefenderScan.cs:22-47`, especially line 32; UI consumption `ViewModels/DefenderViewModel.Actions.cs:70-83`.
- **Evidence:** the tracker compares every batch watermark only to the *initial* baseline, not to the highest watermark already observed. A later watermark can regress substantially and still pass. If the new batch lacks the previously observed start, the retained `ScanId`/state survives and the UI timestamps a new “Running” observation.
- **Observed isolated reproduction:** initial baseline **10**; complete batch watermark **100** with matching start record **90** establishes `Running`; next complete batch watermark **50** with no events leaves the tracker **Running**, not `Unknown`. This invokes the actual tracker from the rebuilt application assembly, using in-memory snapshots only. See `review-fixture-observations.jsonl`.
- **Impact / violated criterion:** after a clear/reset/regression that remains above the initial baseline, the tracker does not fail closed on lost event continuity. Contradicts `RESCUE-ACTIONS-RESULTS.md:17` (“regressed log records become unknown”) and upgrade requirement 2's lifecycle verification. This finding does not require caller-owned correlation and does not claim a reproduced wrong malware-clearance verdict.
- **Required resolution:** retain and validate the observed high watermark/continuity state, latch unknown when continuity is lost, and add regression tests after a start was already observed. Account for the native reader's separate watermark/event queries rather than assuming an atomic snapshot.

## Spec axis

**Verdict: BLOCK for the affected safety/lifecycle criteria; partial implementation otherwise demonstrated.**

| Requirement | Observed scope / result |
|---|---|
| 1 — memory/background relief | Read-only measured resource UI plus individually confirmed, narrowly allowlisted VLC close path. No blanket RAM trim, standby flush, kill-all or speedup inference found in the reviewed action code. Native trust/race assurance remains bounded below, including F2's shared helper. |
| 2 — supported Defender scans/remediation | Quick/Full native scripts, confirmation, availability checks, manual lifecycle observations, bounded detection summary and Security handoff exist. F1–F3 prevent acceptance of the claimed guarded action/lifecycle behavior. Caller ownership is explicitly disclaimed and was **not** demanded by this review. |
| 3 — fail-closed safety/audit | View-model scan/close callers persist intent before the requested operation; audit errors stop those callers. Native safe-mode checks independently precede mutation. F1 and F2 remain failures, not replaced by those passing checks. |
| 4 — Options | Store and view model inspected: supported schema/interval validation, temporary-file replacement, visible corrupt/load/save failure, app-only reset, no option to disable critical action confirmation. This was a source spot-check, not a fresh full Options test pass. |
| 5 — overhead/lifecycle | Manual scan monitoring, bounded event/detection reads, close-wait limit and navigation/window cancellation wiring present. Eight-hour scan-wrapper deadline is session-local; app close does not supervise it after process exit. F1 affects prelaunch deadline enforcement. |

## Evidence verification

Evidence directory: `C:/Users/blunt/AppData/Local/hermes/cache/scratch/wincare-actions/`.

- Recomputed SHA-256 for **all 120 source entries**, **all 7 package entries** (also checked lengths), and **all 51 screenshots** listed in `evidence-summary.json`: **no mismatches**.
- Portable executable: `artifacts/rescue-actions-portable/WinCare Pro.exe`; **137699050 bytes**; SHA-256 **618c35b137b2ffad28ce29581c1252dab83e0c9eb5b860285345cc682a4b250c**. Sidecars match the evidence as well.
- Independently parsed `actions-final-second.trx`: **289 total/executed/passed; 0 failed/notExecuted**. This is the worker's retained full-suite execution, not a fresh independent 289-test run.
- Read `release-build.log`: build success, **0 warnings / 0 errors**. Read publish log and `packaged-launch-result.json`: retained child launch reports responsive true, close requested true, exit 0, empty isolated data directory. No new packaged launch or Release publish was performed in this review.
- After inspecting `SafetyMode.cs`, the test module initializer and the two selected test classes, ran an independent **36-test** focused suite: **36 passed, 0 failed, 0 skipped**, exit 0. It rebuilt Debug application/test outputs without restore. TRX: `independent-actions-review.trx`.
- Isolation: test initializer forces `WINCARE_SAFE_MODE=1`; external environment also set it. Native mutation tests assert rejection before invocation. Other scan/close tests inject fake action implementations; native event/detection tests inject fake runners. TMP/TEMP point to the Hermes scratch evidence directory. Telemetry, first-run and certificate generation were suppressed. No new dependency install was requested.
- Scratch tracker probe builds against the freshly rebuilt application DLL, uses in-memory tracker batches and an injected fake action implementation only. Its process exits 0 because it is an observation harness; its printed `pass:false` values are the reproduced F1/F3 failures, **not passing assertions**.
- Hash equality establishes consistency with the retained manifest. It is not reproducible-build proof or a cryptographic attestation that every binary was produced from that source; the manifest is not a complete all-input build attestation.

### Independent focused command

Working directory: `C:/Users/blunt`.

```bash
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1 DOTNET_GENERATE_ASPNET_CERTIFICATE=false DOTNET_NOLOGO=1 WINCARE_SAFE_MODE=1
export TMP='C:/Users/blunt/AppData/Local/hermes/cache/scratch/wincare-actions' TEMP='C:/Users/blunt/AppData/Local/hermes/cache/scratch/wincare-actions'
'C:/Program Files/dotnet/dotnet.exe' test C:/Users/blunt/Desktop/wincare-desktop/WinCareDesktop.Tests/WinCareDesktop.Tests.csproj --no-restore --filter 'FullyQualifiedName~DefenderActionTests|FullyQualifiedName~OptionalAppActionTests' --logger 'trx;LogFileName=independent-actions-review.trx' --results-directory C:/Users/blunt/AppData/Local/hermes/cache/scratch/wincare-actions
```

Scratch probe execution used the same environment. Its initial framework-only restore used `--source C:/Users/blunt/AppData/Local/hermes/cache/scratch/wincare-actions/review-tracker`, not a network package feed; `dotnet run --project .../review-tracker/TrackerProbe.csproj --no-restore` produced the retained observations. The command-resolution probe ran with `python .../review-command-resolution.py`.

## Visual review of real captures

Inspected with vision: full-size `actions-app-enabled.png`, `actions-defender-safe-disabled.png`, `actions-minimum-defender.png`, `actions-defender-detections.png`, plus the eleven-state `actions-contact.png` overview. Source bindings were also read in both action XAML files.

- Native-safe fixture visibly disables Quick/Full and Security handoff. The synthetic app fixture shows one VLC target with individual close button, save-work warning and the narrow first-eight-VLC scope.
- Completed fixture explicitly says correlated event observation, not malware clearance or proof of initiating caller. Detection fixture explicitly labels synthetic/incomplete history rather than a clean verdict.
- No overlapping action controls or right-edge overflow was seen in the inspected full-size captures. Bottom content extends below the viewport with a visible scrollbar; full scroll accessibility is not proven by a still image. Contact-sheet small text was not used as evidence of full-size readability.
- **No numerical visual score.** Modal confirmation behavior, keyboard/focus, screen-reader, contrast modes, DPI and complete scroll journeys remain unverified.

## Checked-and-clean within the reviewed scope

- Native scan and app-close mutation entry points reject safe mode before process launch/target lookup; helper repeats the mutation guard. Safe mode is not claimed to sandbox arbitrary imported PowerShell modules (F2).
- Scan/app-close intent audit precedes the requested action in the production view-model callers. Audits use temporary-file replacement, retain the last 100 entries and avoid paths/owner SID/scan IDs in those callers. Audit failure is not silently treated as action success.
- App close revalidates record equality after consent; native adapter additionally retains a process handle and read-only image lock, checks owner/session, non-elevated/noncritical status, standard image location, registration, product metadata, signature result and window owner, then rechecks immediately before normal close.
- No force-kill or tree fallback exists in the optional-app close path. CloseMainWindow refusal and request-without-exit are distinct; post-request waiting is bounded.
- Successful PowerShell exit is not itself scan completion. Matching scan ID and provider event evidence is required; incomplete/multiple-candidate snapshots can latch unknown. Detection history is explicitly not attributed to the requested scan.
- Confirmation discloses policy remediation, irreversibility and monitoring-versus-scan cancellation. No new arbitrary file removal, security exclusions/policy editing or automatic offline reboot exists in the reviewed Rescue paths.

## Non-blocking observations and unresolved protected risks

- `DefenderViewModel.cs:29-34` confirms the Security handoff but does not journal it. That opens a settings UI rather than itself scanning/remediating; this is not counted as an action-safety blocker here. Actual scan/close audit behavior was reviewed separately.
- `NativeOptionalAppActions.cs:48-60,77-90`: source checks reduce substitution risks but do not prove mapped-image identity equals the subsequently verified on-disk file across rename/update scenarios, or atomically bind HWND ownership validation to message delivery. Installed-directory permissions and real signed-VLC metadata were not exercised. These remain explicit protected native risks, not a claimed reproduced exploit or a blanket native-safety pass.
- The 257-event truncation/unmatched-history availability gate is conservative and documented. It can disable scan initiation on long-lived logs; no weakening is requested here.
- The scratch probe also showed events above the advertised watermark can be accepted. Because the native reader obtains its watermark *before* enumerating events, this alone is not promoted to a separate defect: normal events can arrive between those reads. It reinforces the need for a deliberate continuity/snapshot contract in F3.
- The existing layout-animation flake in the worker report was not reproduced or declared fixed; the focused rerun intentionally excluded unrelated layout tests.

## verification_gaps / could_not_assess

- `Services/DefenderScan.cs:61-105`: actual Quick/Full provider execution, managed/passive denial, real event XML/localization/user fields, concurrent scans, delivery delays/rollover and wrapper timeout in a disposable VM. No actual scan/remediation was run.
- `Services/NativeOptionalAppActions.cs:42-109`: genuine VLC signature/registration compatibility, native PID/HWND/image races, app refusal/save prompts, elevation and access-control behavior. No live app was closed.
- `Views/DefenderView.xaml:12-20`, `Views/MemoryView.xaml:12-19`: real consent dialogs, keyboard/focus/accessibility/high-DPI/full scroll behavior. Captures do not establish these.
- `RESCUE-ACTIONS-RESULTS.md:66-74`: older cleanup/startup/privacy/DNS/scheduler administrative/rollback/destructive acceptance remains outside this bounded new-action review.
- No new whole-suite independent run, new Release publish, installer/signing validation, runtime vulnerability clearance, or CI/merge approval. Prior evidence is identified as prior, not relabeled as fresh.
- This review is independent of the implementation worker. A second different reader independently validating these new findings was not available in this review session; **independent validation of the findings unavailable**. Findings were self-rechecked against concrete source and isolated probes; parent review can adjudicate them.

## Review boundaries and files

No source edits, fixes, host Defender scan/remediation, live optional-app closure, OS settings/registry mutation, elevation, commit or release was performed. Created this report plus scratch-only probe sources/results, a retained Microsoft documentation page and the focused TRX; the allowed test build also refreshed generated Debug build intermediates/outputs. Existing uncommitted source work and portable artifacts were retained. Documentation extraction initially failed because the configured extraction backend is search-only; direct HTTPS retrieval of the official Microsoft page succeeded. That tool limitation did not prevent the source review or reproductions.

Stop condition reached: bounded review complete; hand findings to the parent for resolution, not implementation in this review lane.
