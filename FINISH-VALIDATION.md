# WinCare candidate — fresh finish validation

## Verdict

**PASS for the requested safe local full-suite test and Release-build gate. Overall production: HOLD / NO_SHIP.** Fresh execution on November 24, 2026, 09:07:10–09:07:25 PST passed **303 tests, 0 failed, 0 skipped**, followed by a non-incremental Release build with **0 warnings, 0 errors**. No production source or test assertion was edited. This is not signing, installer, native-action, accessibility, CI, publication, or fresh portable smoke approval.

Source: the existing dirty checkout at `C:/Users/blunt/Desktop/wincare-desktop`, HEAD `06c8acc738c1c07ea02e15280619fe140c5110d5`. The revision alone does not identify this candidate: the before/after manifests record its tracked and untracked input files. SDK observed by the exact executable below: **8.0.428**.

## Preconditions and safety assessment

Read `GEMINI.md`, `README.md`, `RELEASE.md`, `build-release.ps1`, both project files, and `RESCUE-BLOCKER-FIXES.md` before executing. No AGENTS.md/CLAUDE.md was found in the checkout or its ancestors; no ancestor Directory.Build.props/targets or global.json was found by the prerequisite discovery.

Read `Services/SafetyMode.cs`, `WinCareDesktop.Tests/CompositionSafetyTests.cs` and `TestService.cs`. The module initializer forces `WINCARE_SAFE_MODE=1` and replaces `WINCARE_DATA_ROOT` with a unique `wincare-test-run-*` directory beneath `Path.GetTempPath()`. Therefore TMP/TEMP were redirected before starting dotnet, not merely before constructing individual fixtures. The explicit initial data root and the initializer-selected data roots all remain beneath this run's scratch directory.

Inspected native mutation guard locations, cleanup snapshot execution, process-runner tests, power-plan tests, single-instance tests, Defender scan entry/launch guards, optional-app action fakes, and the blocker regressions. Read-only host telemetry/inventory queries remain real. Cleanup mutations are explicit fixture-root operations; privacy/action success tests use injected doubles. Defender command-provenance tests import/discover trusted commands; event snapshot tests replace event queries with inert fixtures. They do not initiate a Defender scan or remediation. Native close tests refuse before process lookup.

The suite may create/close its own WPF fixture windows and terminate its own timeout-test child process; no user's running application was targeted. No app launch/close smoke was separately performed. Safe mode is application/test discipline, not an OS sandbox or a proof of absence of every possible host side effect.

`build-release.ps1` was inspected but **not executed**: it cleans outputs, can restore, deletes its publish destination and publishes. Direct `--no-restore` commands avoid those release-script effects and preserve the reviewed portable. No installs, upgrades, elevation, registry/settings changes, host cleanup, scan/remediation, commits, releases or publication were requested by the validation commands.

## Exact commands and environment

Working directory: `C:/Users/blunt/Desktop/wincare-desktop`.

Actual launcher (a scratch-only Python subprocess runner; 240-second timeout per command):

```bash
python C:/Users/blunt/AppData/Local/hermes/cache/scratch/wincare-finish-validation/run_validation.py
```

The runner explicitly passed the following process-local environment. Equivalent bash exports and the verbatim child command arguments are shown for reproduction:

```bash
export WINCARE_SAFE_MODE=1
export WINCARE_DATA_ROOT='C:/Users/blunt/AppData/Local/hermes/cache/scratch/wincare-finish-validation/20261124-090709/data'
export TMP='C:/Users/blunt/AppData/Local/hermes/cache/scratch/wincare-finish-validation/20261124-090709/temp'
export TEMP='C:/Users/blunt/AppData/Local/hermes/cache/scratch/wincare-finish-validation/20261124-090709/temp'
export TMPDIR='C:/Users/blunt/AppData/Local/hermes/cache/scratch/wincare-finish-validation/20261124-090709/temp'
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1 DOTNET_GENERATE_ASPNET_CERTIFICATE=false DOTNET_NOLOGO=1

'C:/Program Files/dotnet/dotnet.exe' --version
'C:/Program Files/dotnet/dotnet.exe' test WinCareDesktop.Tests/WinCareDesktop.Tests.csproj --no-restore --logger 'trx;LogFileName=finish-full-suite.trx' --results-directory C:/Users/blunt/AppData/Local/hermes/cache/scratch/wincare-finish-validation/20261124-090709
'C:/Program Files/dotnet/dotnet.exe' build WinCareDesktop.csproj -c Release --no-restore --no-incremental --nologo -v minimal
```

| Check | Fresh observed result | Scope and freshness |
|---|---|---|
| SDK identity | Exit 0; `8.0.428` | Exact installed dotnet executable, 09:07:09–09:07:10 PST |
| Full suite | Exit 0; 303 passed, 0 failed, 0 skipped | Entire unfiltered test project, default Debug/net8.0-windows, 09:07:10–09:07:21 PST; build enabled, existing dependency assets, safe-mode fixtures and read-only integration queries |
| Release build | Exit 0; build succeeded, 0 warnings, 0 errors | Application project, Release/net8.0-windows, non-incremental compile, existing dependency assets, 09:07:21–09:07:25 PST; not a self-contained publish |
| Final preservation/results pass | PASS | Before/after input and reviewed-package hash comparisons plus parsed TRX results after both commands; no source changes during validation |

All required command checks passed; failure origin classification is not applicable. No failed checks were weakened or bypassed. No test rerun was necessary.

## Fresh evidence and preservation

Evidence directory:

`C:/Users/blunt/AppData/Local/hermes/cache/scratch/wincare-finish-validation/20261124-090709/`

- `commands.json`: exact argv, cwd, environment, timestamps, exit codes and log paths.
- `sdk.log`, `full-tests.log`, `release-build.log`: fresh captured stdout/stderr.
- `finish-full-suite.trx`: parsed actual result elements: **303**, all **Passed**, consistent with total/executed/passed counters; zero nonpassing results. Test execution timestamps: `2026-11-24T09:07:11.2089670-08:00` through `2026-11-24T09:07:20.9872895-08:00`.
- `before.json`, `after.json`: SHA-256/byte-length manifests for **189 existing input files** selected from tracked/untracked nonignored paths excluding artifacts and this report, and **7 reviewed-portable files**. Input contents, reviewed-portable contents, HEAD and git status were unchanged across the command run. This bounded manifest is not a complete dependency/runtime inventory or reproducible-build attestation.
- `verification.json`: consolidated actual TRX counts/outcomes and preservation comparisons.

Reviewed portable retained unchanged:

`artifacts/rescue-reviewed-portable/WinCare Pro.exe`

- Bytes: `137703146`
- SHA-256: `3e082629060d9580b74e34fdfe996e105bc5dabcb64fec6ff940bb3026db1859`

The existing portable was neither republished nor overwritten. Its hash preservation is not proof that it was built by this new run. Fresh build outputs are under normal generated `bin/`/`obj/` locations; tests wrote scratch fixtures and generated test outputs. The only intended new repository file from this task is this report. The runner, logs, manifests and TRX are scratch artifacts.

## Remaining gates and concrete recommendations

1. **Native production behavior remains unverified here.** Retain the production HOLD from `RESCUE-BLOCKER-FIXES.md`. Run separately approved disposable-VM acceptance for actual Quick/Full scans and policy/remediation outcomes, event localization/delays/concurrency/rollover, signed VLC compatibility and PID/image/window races, permissions/elevation, refusal/save prompts, and graceful close outcomes. Those operations were prohibited on this host and were not attempted.
2. **Existing destructive/administrative features need their own VM gates.** Cleanup boundaries under races, startup/privacy/DNS mutation and rollback, scheduler lifecycle and uninstall outcomes are not cleared for native production solely by safe-mode fixtures.
3. **Interactive acceptance is outstanding.** Consent-dialog keyboard/focus, screen-reader, high-DPI/contrast, scroll journeys and native hover/timing require observed acceptance, not this test/build result. No new screenshot or UI acceptance claim is made.
4. **Release chain remains separate.** Signing, installer install/uninstall, runtime/dependency vulnerability review, approved runtime packaging, CI and distribution checks were not run. No current-runtime security approval is implied by using locally available restored assets. Do not run the release scripts or replace the reviewed portable under this task's authorization.
5. **Documentation drift (non-blocking for this local test/build gate):** `README.md:24` still labels `rescue-actions-portable` the newest candidate, whereas the reviewed report identifies `rescue-reviewed-portable`; `RELEASE.md:163` still records 110 tests, while this fresh suite executed 303. Recommend a separately scoped documentation correction that preserves historical evidence and distinguishes local checks from production approval. `RELEASE.md` is treated as an existing checklist, not independently verified current signing/security guidance.

No concrete production-source fix is justified by a failing local test or build in this run: neither failed. The earlier F1–F3 report was used to identify scope and remaining gates, not reused as fresh execution evidence. The requested safe local validation is complete; production approval remains explicitly withheld.
