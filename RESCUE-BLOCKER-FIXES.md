# Rescue blocker fixes — recovered completion and independent review

## Verdict

**PASS — the three exact blockers F1–F3 in `RESCUE-FINAL-REVIEW.md` are resolved in the reviewed local candidate. High confidence within this bounded scope.** No remaining actionable defect was found for those three criteria. **Overall native production readiness: HOLD / NO_SHIP.** This is not release, signing, installer, native-action, accessibility or CI approval.

Recovery review: November 24, 2026 (PST), independent of interrupted implementation worker `sa-0-163b98e9`. Claim/spec source: the three findings and required resolutions in `RESCUE-FINAL-REVIEW.md:13-38`, plus the recovery request. Correctness/risk axis: PASS for those findings. Spec axis: PASS for those same three requirements, not a new whole-product assessment. The original historical review is preserved unchanged; this report supersedes only its unresolved F1–F3 status for the current candidate.

## Observed fixes and independent assessment

### F1 — cancellation during audit before scan initiation: PASS

- `ViewModels/DefenderViewModel.Actions.cs:49-54` awaits the intent audit off the UI thread, checks cancellation after it returns, then forwards the preparation token to `IDefenderActions.Start`. Failed audit still prevents initiation.
- `Services/DefenderScan.cs:73-78,92-100,113-117` checks cancellation at native entry and again immediately before the process factory. After launch, mutation observation deliberately uses a separate token so leaving the page does not purport to cancel an already initiated Defender scan.
- Read the actual regression assertions in `WinCareDesktop.Tests/RescueBlockerTests.cs:7-14,63-77`: cancellation inside the audit callback yields zero fake starts; cancelled native entry never reaches its runner; cancellation at the final launch boundary never reaches its process factory. All passed independently.
- Adjacent retained fix inspected: `ViewModels/MemoryViewModel.Actions.cs:45-48` likewise rechecks after the close-intent audit. Its fake-only cancellation regression in `OptionalAppActionTests.cs:31-37` passed. No app was closed.
- This establishes the reported audit-to-launch boundary, not atomic cancellation of a scan already launched or bounded completion of an indefinitely blocked audit storage operation.

### F2 — inherited PowerShell command/module shadowing: PASS

- `Services/RescuePowerShellPolicy.cs:14-59` selects system Windows PowerShell, clears inherited child environment, constrains PATH and PSModulePath, disables module autoloading, resets the module path again inside the child, imports absolute inbox manifests, and checks exact discovered command module paths. Unexpected origin or unavailable modules fails closed.
- Checked dependencies include Start-MpScan, Get-MpThreatDetection, Get-WinEvent, Get-AuthenticodeSignature, Select-Object and ConvertTo-Json. Production scan/detection/event scripts use module-qualified commands (`Services/DefenderScan.cs:69,78,82`); signature validation does too (`Services/NativeOptionalAppActions.cs:87`). Status reading is direct WMI, not this PowerShell path (`Services/DefenderStatusReader.cs:18-37`).
- `RescueBlockerTests.cs:79-129` verifies bootstrap/environment restrictions, useful trusted discovery, and three scratch shadow modules: Start-MpScan, Get-MpThreatDetection and Get-AuthenticodeSignature. Each shadow exports a throwing function; tests only discover commands, never invoke scan/detection/signature actions. The current policy resolves inbox paths rather than those inherited scratch modules. All passed independently.
- Inspected retained `probe-policy.py` and `policy-probe.json`: imports/discovery only, exit 0, inbox CDXML paths for Defender commands and inbox manifest paths for Security/Diagnostics. This probe explains the exact path checks; fresh tests above exercise the actual current C# policy, not merely the Python approximation.
- Boundary: trusted/protected Windows system installation is assumed. This is not an integrity attestation of Windows files, a defense against administrator replacement of inbox modules, or proof of compatibility on all Windows editions. No real scan or detection query was run.

### F3 — watermark regression above the initial baseline: PASS

- `Services/DefenderScan.cs:29-55` retains the highest accepted watermark, rejects later regression, rejects events above the advertised final watermark, and latches Unknown for incomplete history or disappearance of the previously observed start.
- `Services/DefenderScan.cs:69` reads a watermark before and after event enumeration, advertises the latter, and marks a regressing/inconsistent snapshot incomplete. This explicitly handles the reader's non-atomic query contract conservatively.
- `RescueBlockerTests.cs:16-60` reproduces baseline 10 → watermark 100/start 90 → watermark 50. The result is Unknown and remains Unknown even after a later plausible completion. Missing-start and above-final-watermark assertions also pass. The real script's before/after watermark logic is tested with every event query replaced by an inert fixture; no host event log is read.
- This is not proof against every possible real-world clear/reset/refill race that leaves no observable regression. Those event-delivery and rollover cases remain native VM gates.

## Retained evidence verified against disk

Evidence directory: `C:/Users/blunt/AppData/Local/hermes/cache/scratch/wincare-blockers/`.

- Recomputed all **122 listed source SHA-256 hashes**: no mismatches with `evidence-summary.json`.
- Recomputed all **7 package hashes and byte lengths**: no mismatches; no extra package files.
- Independently parsed `blockers-full-suite.trx`, including actual result elements rather than trusting the summary: **303 results, all Passed; counters 303 executed/passed, 0 failed/notExecuted**. Retained run: November 24, 2026, 03:38:49–03:39:00 PST. This is the worker's full-suite evidence, not a fresh independent full-suite run.
- `release-build-command.json` records exit 0; `release-build.log` says build succeeded, **0 warnings, 0 errors**. `portable-publish-command.json` records exit 0; publish output identifies `artifacts/rescue-reviewed-portable`.
- Inspected `launch-and-record.py`: the retained child was explicitly launched with both safe-mode environment and `--safe-mode`, isolated data root and scratch TMP/TEMP. `packaged-launch-result.json` records responsive true, close_requested true, exit 0, and no data files. **No launch or window close was repeated during recovery.** This is retained smoke evidence, not a fresh interactive/native-action test.
- `RESCUE-FINAL-REVIEW.md` still matches its retained SHA-256: `3a2de6c66368c3d8d79dcf8fc905522fd7610ae70b5ce719523cf056c6843f68`.
- Git HEAD is `06c8acc738c1c07ea02e15280619fe140c5110d5`; the candidate includes extensive pre-existing tracked and untracked work. Reviewed git status/diff and current relevant files; did not reset or infer a clean committed patch. The manifest covers listed files, not all build inputs. Hash equality is consistency evidence, not reproducible-build or signed source-to-binary attestation.

### Current portable candidate

`C:/Users/blunt/Desktop/wincare-desktop/artifacts/rescue-reviewed-portable/WinCare Pro.exe`

- Size: **137703146 bytes**
- SHA-256: **3e082629060d9580b74e34fdfe996e105bc5dabcb64fec6ff940bb3026db1859**

This is the reviewed candidate; the historical `rescue-actions-portable` is not silently substituted for it.

## Fresh independent focused verification

Read `SafetyMode.cs`, `CompositionSafetyTests.cs` module initializer, selected test classes, native guards and composition wiring before executing. The initializer forces safe mode and a scratch data root. Selected action tests use fakes; native mutation checks reject before launch/process lookup. PowerShell tests import/discover inbox commands or run inert event fixtures. No dependency restore or upgrade was requested.

Working directory: `C:/Users/blunt/Desktop/wincare-desktop`.

```bash
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1 DOTNET_GENERATE_ASPNET_CERTIFICATE=false DOTNET_NOLOGO=1 WINCARE_SAFE_MODE=1
export TMP='C:/Users/blunt/AppData/Local/hermes/cache/scratch/wincare-blockers' TEMP='C:/Users/blunt/AppData/Local/hermes/cache/scratch/wincare-blockers'
'C:/Program Files/dotnet/dotnet.exe' test WinCareDesktop.Tests/WinCareDesktop.Tests.csproj --no-restore --filter 'FullyQualifiedName~RescueBlockerTests|FullyQualifiedName~DefenderActionTests|FullyQualifiedName~OptionalAppActionTests' --logger 'trx;LogFileName=independent-blocker-recovery.trx' --results-directory C:/Users/blunt/AppData/Local/hermes/cache/scratch/wincare-blockers
```

Observed exit **0**, **50 passed, 0 failed, 0 skipped**; independently parsed all 50 actual TRX results as Passed. Run finished November 24, 2026, 07:47:03 PST. Scope includes all 13 RescueBlockerTests cases plus the adjacent Defender/optional-app action tests. Debug application/test outputs were rebuilt from this checkout. No source implementation changed in recovery.

## Checked-and-clean / remaining gates

Checked-and-clean: the three exact defect paths, their actual regression assertions, relevant adjacent command provenance and cancellation boundaries, retained evidence counts/hashes, preserved historical review, and fresh focused tests. Independent review here is separate from the implementation worker; no additional third reviewer or CI run is claimed.

**verification_gaps / could_not_assess — overall production HOLD:**

- Actual Quick/Full scans/remediation, passive/managed-policy denial, event XML/localization/delays/concurrency/rollover and long-running wrapper behavior in a disposable VM (`Services/DefenderScan.cs:60-117`).
- Genuine signed VLC compatibility, permission/elevation behavior, PID/image/HWND races, refusal/save prompts and graceful close outcomes (`Services/NativeOptionalAppActions.cs:42-104`).
- Consent-dialog keyboard/focus, screen-reader, high-DPI/contrast and complete scroll journeys; no fresh visual acceptance performed.
- Existing cleanup/startup/privacy/DNS/scheduler administrative, rollback and destructive VM gates; installer/signing/runtime vulnerability/release validation. Local green tests do not discharge these.

No production-source changes, real host scans/remediation, live app closures, settings/registry mutations, elevation, dependency upgrades, commits, pushes or release occurred in recovery. Created this report and the focused TRX; allowed test execution refreshed generated Debug outputs and scratch test files. No tool/test failure blocked recovery. Stop condition reached: the three-blocker local review is complete; native production remains HOLD.
