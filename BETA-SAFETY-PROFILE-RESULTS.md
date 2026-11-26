# Beta safety profile — isolated implementation verified; checkout integration BLOCKED

November 25, 2026. **HOLD / NOT BETA APPROVED.** A diagnostics-only private-preview implementation and self-contained .NET 10 candidate passed the checks below. The implementation is in the isolated source directory, **not applied to this checkout**: concurrent source and old-artifact changes were detected immediately before application. They were not overwritten or silently merged. No installer lifecycle was performed.

## Locations and identity

Evidence root: `C:/Users/blunt/Desktop/wincare-beta-safety-20261125-053557/`

- `baseline/`: preserved 234 original source/configuration/documentation inputs.
- `source/`: 239 tested inputs, including 40 changed/new files (35 existing files changed; five new files). Exact list: `evidence/changed-files.json`.
- `evidence/beta-safety.patch`: isolated baseline-to-tested-source review patch, not applied to checkout. It normalizes original CRLF whitespace; `git apply --check --ignore-space-change` passed against the preserved baseline only. Use exact tested-source files/hashes for reconciliation, not a blind application to the concurrently changed checkout.
- `evidence/final-integrity-verified.json`: 234 preserved baseline inputs, 239 tested source inputs/ZIP entries and all 69 candidate payload/metadata entries verified against their recorded hashes.
- `evidence/tested-source.zip`: full tested source snapshot, excluding build outputs and this generated report.
- `candidate/portable/WinCare Pro.exe`: unsigned, self-contained win-x64 candidate. Keep all seven portable files together.
- `candidate/metadata/`: source hashes, source ZIP/patch, test/audit/build/publish receipts, dependency assets and runtime/deps metadata.
- `candidate/manifest.json`: hashes and lengths for 69 payload/metadata files; manifest itself is outside its own file map.

Candidate executable: **123,533,170 bytes**, SHA-256 `eaf200b2eaecde52f38e0e0f405d0da9e6df8fd704167cbd17aac875b1f379f5`.

Manifest SHA-256: `3dcc120df276ad162baa1e811349244ac8d37ff81ac180ae77f2fd2f7b5a5642`.

Canonical tested-source identity (SHA-256 of sorted compact JSON path-to-hash map): `d4f2ee551c985898e3a7574204c763959cf201dacc0dfba29f10bfb84bd61b00`.

SDK: existing isolated `C:/Users/blunt/Desktop/wincare-runtime-migration-20261124-230712/sdk/dotnet.exe`, pinned **10.0.404**. Included .NETCore/WindowsDesktop runtime: **10.0.15**, confirmed by RID runtimeconfig. No new tool downloads or machine SDK installation.

## Scope decision and action matrix

Grounding: WINCARE-LAUNCH-PLAYBOOK.md sections 3 and 6 require command/service enforcement and explicitly permit a diagnostics-only preview if cleanup is not qualified. RUNTIME-MIGRATION-RESULTS.md is historical evidence, not qualification of this binary.

**Cleanup is excluded.** Existing cleanup has snapshot/path checks, but the initial direct-service regression deleted an explicitly created scratch fixture even in verification mode. Wider recycle-bin, Windows Update cache and DISM behavior is not qualified for this candidate. Existing disposable cleanup receipts do not qualify arbitrary user paths or this new binary. No real cleanup was attempted here.

| Capability | Beta availability | Command/model boundary | Effective service boundary / behavior |
|---|---|---|---|
| System overview, RAM/disk measurements, freshness and guidance | Enabled, read-only | Dashboard refresh/guidance; DiagnosticSession Rescan/Cancel | DiagnosticReader and existing native reads; failures remain unknown/partial, not fake successful optimization |
| Memory/CPU samples, sorting/filtering and app inventory | Enabled, read-only | Memory Refresh/Cancel; AppUninstall LoadApps; optional-app ReviewApps | NativeMemoryReader, InstalledProgramsService, fixed Appx inventory and fixed signature reads; no close/uninstall |
| Disk estimates and cleanup preview | Enabled, read-only | Disk StartScan/CancelScan, quick targets, cache measurement; Main ScanCleanupPreview | DiskAnalyzerService and measurement/preview functions only; existing depth/partial-result limits remain |
| Privacy/startup/DNS/power state | Enabled, read-only | Existing inventory/refresh/navigation | Registry/network/power reads retained; no settings changes |
| Defender status, event/detection observations | Enabled, read-only | Refresh/Cancel, detection reads; lifecycle monitor requires an existing tracker | WMI status and fixed internal provider-read scripts; no scan/remediation initiation and no malware-clearance claim |
| Diagnostic export | Enabled only through reviewed preview and explicit Save | PrepareExport, CloseExport, SaveExport | Existing allowlisted redacted payload and pinned preview; cancelled destination writes nothing; no upload |
| Ordinary app preferences and in-memory log clearing | Retained | Existing Options controls and log clearing | Beta app data separated from normal app data; automatic raw environment/crash logs are disabled in beta even if prior options request them |
| Cleanup, recycling, cache deletion, DISM | Unavailable | Main RunOptimization; Dashboard RunQuickOptimize; Disk DeleteItem disabled, including direct Execute | CleanTempAndCache rejects before enumeration/deletion; RecycleFileService and StorageCleanupService native mutation guards include beta policy |
| DNS switch/flush | Unavailable | Main SelectDns; Network SwitchDns/FlushDns | DnsSettingsService Change/Restore and SafetyMode mutation adapter; generic public process runner also refuses |
| Privacy and startup changes | Unavailable | Main/Privacy toggles and EnableAll/DisableAll; Startup ToggleItem | ApplyPrivacySetting and ToggleStartupEntry reject before adapters/journaling |
| Undo of system changes | Unavailable | Main RevertTransaction and History Rollback | TryRevert rejects before privacy/DNS/startup/power restoration |
| Power changes and RAM trimming | Unavailable | Dashboard SwitchPowerPlan; Main FlushRamStandby; Memory TrimWinCareRam | ApplyPowerPlan/SetPowerPlan; trimming adapters; public working-set wrapper guards before private P/Invoke |
| Scheduled maintenance, including preference save | Unavailable | Scheduler ToggleEnabled/SaveSettings | EnableForSession and SaveConfig reject even with injected runner; initial status explicitly says unavailable; no session can arm |
| App close/uninstall | Unavailable | Memory CloseApp; AppUninstall UninstallSelected; Main UninstallApp | NativeOptionalAppActions Close and Appx removal reject; classic uninstaller retains guarded process launch; injected UI delegates cannot bypass beta handler |
| Defender scan/remediation initiation and security handoff | Unavailable | QuickScan, FullScan, OpenSecurity; direct StartScan handler rejects | NativeDefenderActions Start and mutation runner reject; scan preparation cancellation never starts a scan; no remediation API is enabled |
| External ping probe | Unavailable | Network TestPing | PingHostAsync refuses before creating/sending a probe |
| Arbitrary process/PowerShell execution | Unavailable through public services | No user execution command | RunProcessPublic and RescuePowerShell.RunAsync refuse even if a caller labels arbitrary script as read-only; narrowly internal fixed inventory/provider reads remain |

No elevation command is enabled by this work. Read-only navigation/manual guidance is not remediation. Legacy executable filename and assembly identity remain for packaging compatibility; the actual window title and persistent banner explicitly identify the diagnostics-only private preview.

## Enforcement design

`Services/BetaSafetyProfile.cs` is an immutable compile-time policy. The application project defaults `WinCarePrivateBeta` to true and defines `WINCARE_PRIVATE_BETA`. There is no UI, saved preference, license, environment-variable or command-line switch that relaxes the compiled profile. An explicit different build using `-p:WinCarePrivateBeta=false` preserves normal-mode compatibility; it is not the beta distribution.

`SafetyMode` remains separate verification machinery. Beta does **not** turn it on or substitute synthetic diagnostics. Native reads still use their original implementations. Beta uses a separate normal data directory (`WinCarePrivatePreview`) and mutex identity; test data roots remain redirected to authorized scratch. Raw automatic diagnostic logs are suppressed in beta; the reviewed redacted export remains available.

Every mapped mutation command has a false CanExecute in beta, plus an early handler refusal because ICommand.Execute can bypass CanExecute. Direct service/native boundaries deny before mutations, confirmation callbacks, injected mutation adapters or recovery journaling. Refusal text is: “Unavailable in the diagnostics-only private preview. No system changes were requested.”

## Observed verification

| Gate | Result |
|---|---|
| Test-first cleanup boundary | RED: one failure, scratch fixture only; GREEN after policy enforcement |
| Test-first direct mutation matrix | RED: 19 failures / one prior passing cleanup case; then all passed |
| Test-first command matrix | RED: 25 failures; then all passed |
| Test-first native/public-runner/UI boundaries | RED: five failures; then all passed |
| Test-first scheduling persistence | RED: one failure; then passed |
| Normal-mode regression suite | **316 total / 316 passed / zero failed / zero skipped** |
| Beta suite | **90 total / 90 passed / zero failed / zero skipped**, comprising 54 beta-specific cases and 36 retained diagnostic/export/memory/Defender read tests |
| Four Release projects | App, tests, RenderShots, MakeIcon: exit 0, each zero warnings/errors |
| Inert packaging regression | **44 passed / zero failed** |
| Audit policy scenarios | **Five passed**, each checking all four project outcomes |
| Actual dependency restore/audit | **Four project audits passed**, high/critical transitive policy and fail-closed restore warnings |
| Separate self-contained publish | Exit 0, warnings-as-errors, explicit beta profile |
| Exact packaged executable smoke | Responsive window with diagnostics-only title; verification mode **off**, attempted beta environment override `false`; graceful close, exit **0** |

The current baseline already contained more tests than the historical 303-test migration receipt. Counts above are parsed from this run's TRX, not copied from earlier reports. Beta tests explicitly cover direct mutation calls with verification mode off, injected-adapter refusal, direct ICommand.Execute, honest refusal status, scan-preparation cancellation, real native RAM/diagnostic reads and no environment override. Existing export tests cover preview pinning/redaction, cancelled destinations and failure preservation. No actual Defender scan, remediation, user-app close/uninstall or real cleanup was needed.

Two full-suite issues were observed and resolved before the passing run:
1. A banner wrapper changed the root layout type expected by an existing sidebar test. The implementation now preserves the original root Grid and adds an auto-sized banner row.
2. Existing PerformanceUpgradeTests temporarily disabled process-wide verification mode for an injected power adapter while native refusal tests ran concurrently. One refusal test reached a nonexistent synthetic PID lookup instead of the safe-mode rejection. The environment-changing fixture is now in a nonparallel collection shared with beta environment tests. The passing full run follows that isolation fix. No real target was closed.

Failed receipts are retained; they are not success evidence. Final passing TRX files: `evidence/application-isolated/normal.trx` and `beta.trx`. Final command arrays, cwd, elapsed times and exit codes are in `evidence/final-*.json`; logs have corresponding names. `evidence/run.py` records the isolated subprocess environment. Normal and beta suites are wired into `ci/Test-ApplicationProfiles.ps1`, the isolated CI workflow and release-build helper. Hosted CI was not run.

All test fixture/temp/persistence data was directed to `C:/Users/blunt/AppData/Local/hermes/cache/scratch/` or the new evidence root. Inert packaging signatures/packages are fake fixtures, not real signing/certificate operations. No host MSI/MSIX installation, repair or uninstall was run. No commit, push, signing, certificate/private-key operation, elevation, reboot or external publication occurred.

## Blocking concurrent changes — do not apply wholesale

Final checkout comparison found six files changed by other work after the isolated snapshot:

- `Services/LicenseService.cs`
- `Services/PathSafety.cs`
- **`Services/StartupManagerService.cs` — overlaps this implementation**
- `website/index.html`
- `Tools/LicenseGen/LicenseGen.csproj`
- `Tools/RenderShots/Program.cs`

It also found 12 added files, enumerated in `evidence/checkout-conflicts.json`, including five test files, the next-candidate acceptance document and six Store listing images. These additions are **not part of this tested source snapshot**.

An existing artifact also changed concurrently: `artifacts/beta-2.0.0/WinCare Pro.exe` changed from SHA-256 `19734bbcf7ae6bc1231b61c7fea103a7caa9e3211c6b2a1931de8a0783395c54` to `2caae4439ae7cbc0261f45ee73d6882f6a7a2d36627feea82a466c445727f72d`. This workflow did not write that path. **61 of the 62 inventoried earlier artifact files matched their starting hashes; blanket preservation of the live checkout cannot be claimed.** The previous runtime-net10 candidate was not modified by this workflow.

Detailed conflict maps/diffs are retained in `evidence/checkout-conflicts.json`, `concurrent-checkout-changes.patch`, and `concurrent-artifact-changes.json`. No implementation files were applied to the checkout, and no candidate was copied into its artifacts directory. This report is the only checkout file written by this workflow. The isolated source, patch and verified portable candidate remain available for coordinated reconciliation.

## Remaining work

Reconcile concurrent changes with the source owner, applying the beta boundary without discarding the new startup/path/license/tool changes. Then produce a **new source identity, new candidate directory and fresh qualification receipts** for the combined source. The isolated candidate's receipts do not qualify today's live checkout or the concurrently replaced old executable.

Still open: independent source review, full interactive/accessibility/DPI qualification, real installer package construction and exact-payload lifecycle/repair restart policy, supported-PC matrix, hosted CI, production signing/trust and owner release approval. The packaged smoke proves launch/identity/responsiveness/clean exit only, not full native UI acceptance. This is neither a shipped beta nor an approved installer.
