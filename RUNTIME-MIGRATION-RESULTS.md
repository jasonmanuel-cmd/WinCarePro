# .NET 10 migration — implemented; local automated gates passed

## Current result after authorized acquisition recovery

November 24, 2026, Pacific time. **Runtime migration complete; HOLD / NOT BETA APPROVED.** The historical blocked-attempt report below is superseded by this section.

### SDK acquisition

Refreshed official releases.json selected the same SDK **10.0.404**, runtime **10.0.15**, URL and SHA-512. Recovery reused **25 complete ranges / 202,954,802 bytes**, including complete ranges recovered from the initial contiguous partial download. It recorded retained-part SHA-256 integrity, used at most two workers, validated HTTP 206/exact Content-Range, and resumed partial ranges. No mirror or alternate CDN was used. Recovery completed in **307.54 seconds**, within the 30-minute budget; both tracked recovery/build jobs exited 0.

The assembled official ZIP is **303,618,098 bytes**. SHA-512 matched the official value quoted in the historical section **before extraction or execution**. Isolated SDK: `C:/Users/blunt/Desktop/wincare-runtime-migration-20261124-230712/sdk/dotnet.exe`. Its --info reports SDK 10.0.404, MSBuild 18.9.15 and .NETCore/WindowsDesktop/AspNetCore runtimes 10.0.15. No machine install, persistent PATH/registry changes, elevation or certificate generation occurred. Only subprocess environments prepend the isolated SDK path.

Receipts in the evidence root below: `evidence/sdk-download-verification.json`, `sdk-info.txt`, `releases-10.0-recovery.json`, `retained-parts-integrity.json`, `acquisition-recovery.log`, `acquisition-recovery-status.json`. The original blocked report is separately archived as `evidence/initial-blocked-report.md`.

### Applied changes and dependency choices

- All four projects now target `net10.0-windows`: app, tests, RenderShots and MakeIcon.
- New global.json pins `10.0.404`, `rollForward: disable`, `allowPrerelease: false`.
- Both CI setup-dotnet steps consume `global-json-file: global.json`; official setup-dotnet v4 metadata confirms that input. Framework-specific paths in `install.ps1` and `packaging/Test-Packaging.ps1`, CI's WPF comment and README/GEMINI framework descriptions were updated. Installer helper was not executed. Publisher remains disabled.
- **System.Management 8.0.0 → 10.0.15** aligns the Microsoft WMI dependency with the selected .NET 10 servicing line. Exact package availability was confirmed in official NuGet flat-container metadata. Fresh restore/audit, all builds and the existing suite passed; complete native WMI acceptance is not claimed.
- Retained **CommunityToolkit.Mvvm 8.2.2**, **Microsoft.NET.Test.Sdk 17.11.1**, **xunit 2.9.2**, **xunit.runner.visualstudio 2.8.2**: real compilation/source generation/discovery and all tests passed without warnings, so no compatibility failure justified a gratuitous upgrade. This is not an upstream support-duration guarantee.
- No dependency-lock policy change. All four resolved assets files, RID deps/runtimeconfig, exact source hashes and SDK receipt are preserved with the candidate.

Official .NET 9/10 breaking-change indexes and focused WPF/native-search articles were retrieved and retained under evidence. Review covered BinaryFormatter, GetXmlNamespaceMaps, empty Grid definitions, DynamicResource, mixed WPF/WinForms MenuItem/ContextMenu ambiguity, single-file native search and NuGet audit/pruning. Scoped source searches found no BinaryFormatter/GetXmlNamespaceMaps/custom native-search override or mixed MenuItem/ContextMenu usages. All four projects compiled cleanly, including MakeIcon's WPF/WinForms combination. Existing WPF tests passed. The documented native-search change preserves default AssemblyDirectory probing; no speculative loader edits were made. No production behavior-code fix was required. This is not complete UI/native/accessibility acceptance.

Primary references and their local snapshots:

```text
https://learn.microsoft.com/en-us/dotnet/core/compatibility/9.0
https://learn.microsoft.com/en-us/dotnet/core/compatibility/10.0
https://learn.microsoft.com/en-us/dotnet/core/compatibility/interop/10.0/native-library-search
https://learn.microsoft.com/en-us/dotnet/core/compatibility/wpf/10.0/empty-grid-definitions
https://learn.microsoft.com/en-us/dotnet/core/compatibility/wpf/10.0/dynamicresource-crash
https://raw.githubusercontent.com/actions/setup-dotnet/v4/action.yml
https://api.nuget.org/v3-flatcontainer/system.management/index.json
```

### Observed verification and preservation

| Gate | Actual result |
|---|---|
| SDK ZIP SHA-512 | Matched before extraction/execution |
| Source-copy SDK selection | 10.0.404, exit 0 |
| Audit-policy regression | **5 scenarios passed**, each checks all four project outcomes |
| Real restore/audit | **4 projects passed**, explicit official NuGet feeds, transitive high/critical policy, all restore warnings fail closed |
| Release app/tests/RenderShots/MakeIcon builds | **All four exit 0; each 0 warnings / 0 errors** |
| Application TRX | **303 total / 303 passed / 0 failed / 0 skipped** |
| Inert packaging regressions | **44 passed / 0 failed**, exit 0 |
| Self-contained win-x64 publish | Exit 0; official-feed restore/audit and warnings-as-errors; no cached-runtime override |
| Runtime identity | RID runtimeconfig includes .NETCore and WindowsDesktop **10.0.15** |
| Source application | **224 source/configuration/documentation inputs match tested copy** |
| Preservation | **2,059 unrelated baseline files unchanged**, including old reviewed artifacts and publisher |
| Candidate copy | **17 payload/metadata files hash-verified** |
| Whitespace | Scoped git diff --check exit 0 |

Nine existing config/documentation files changed, plus new global.json; this report was separately updated. The complete original baseline remains preserved. Source manifest covers original source inputs plus global.json, excluding outputs and this generated report. Canonical identity (SHA-256 of sorted compact JSON path-to-hash map): `ff45e0df252915cec6c8bd9be4f174a14a55f8abf0c22b58534193056dc1b016`. Final verification: `evidence/migration-final-verification.json`.

No counts were inferred from old reports. Audit threshold does not certify zero low/moderate vulnerabilities or permanent security clearance. PyYAML was absent in the active interpreter; it was not installed. CI substitutions were checked directly against official action metadata and preserved source bytes, not represented as a hosted Actions run or comprehensive YAML-parser validation.

### Commands, isolation and evidence

Evidence root: `C:/Users/blunt/Desktop/wincare-runtime-migration-20261124-230712/`. All restore/build/test/publish commands ran from its `source/` copy **before** checkout edits. Fully expanded commands, working directories, elapsed times and exits: `evidence/migration-command-results.json`; exact executable argument arrays/environment/order: `evidence/wincare_net10_build.py`. Recovery and final application scripts are retained alongside it.

With `$D = <evidence-root>/sdk/dotnet.exe`, `$E = <evidence-root>/evidence`, `$S = C:/Users/blunt/AppData/Local/hermes/cache/scratch/wincare-net10`:

```powershell
& $D --version
pwsh -NoLogo -NoProfile -NonInteractive -File ci/Test-DependencyAudit.ps1 -ScratchRoot $S
pwsh -NoLogo -NoProfile -NonInteractive -File ci/Invoke-DependencyAudit.ps1 -ReportsDirectory "$E/net10-audit"
& $D build WinCareDesktop.csproj -c Release --no-incremental --no-restore --nologo -v minimal
& $D build WinCareDesktop.Tests/WinCareDesktop.Tests.csproj -c Release --no-incremental --no-restore --nologo -v minimal
& $D build Tools/RenderShots/RenderShots.csproj -c Release --no-incremental --no-restore --nologo -v minimal
& $D build Tools/MakeIcon/MakeIcon.csproj -c Release --no-incremental --no-restore --nologo -v minimal
& $D test WinCareDesktop.Tests/WinCareDesktop.Tests.csproj -c Release --no-build --no-restore --nologo -v quiet --logger 'trx;LogFileName=application.trx' --results-directory "$E/net10-test-results"
pwsh -NoLogo -NoProfile -NonInteractive -File packaging/Test-Packaging.ps1 -ScratchRoot $S
& $D publish WinCareDesktop.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:PublishTrimmed=false --configfile '<evidence-root>/source/ci/NuGet.Config' -p:NuGetAudit=true -p:NuGetAuditMode=all -p:NuGetAuditLevel=high -p:TreatWarningsAsErrors=true -warnaserror -o '<evidence-root>/candidate/net10-win-x64' --nologo -v minimal
```

Environment: WINCARE_SAFE_MODE=1; isolated WINCARE_DATA_ROOT, TEMP, TMP, DOTNET_CLI_HOME under `$S`; NUGET_PACKAGES under `<evidence-root>/nuget-packages`; explicit DOTNET_ROOT; telemetry/first-run disabled; DOTNET_GENERATE_ASPNET_CERTIFICATE=false. Existing test initialization additionally isolates persistence. Inert packaging tools use fake fixtures only, not genuine signing/key/certificate operations.

### Separate candidate

```text
C:/Users/blunt/Desktop/wincare-desktop/artifacts/runtime-net10-20261124-230712/
  portable/WinCare Pro.exe
  portable/<required native sidecars and PDB>
  metadata/<source hashes, assets, deps, runtimeconfig, SDK and commands>
  manifest.json
```

Executable: **123,459,442 bytes**, SHA-256 `3dc6a1c690edbd13a39a625956f357b938fd644c67c2fbeb6233ad86bba4280e`.

Manifest SHA-256: `34c6eb38220301c85a2d7f5cfe7c0c0dcc46b3a330eec6dff729f393a008c9ce`.

Keep all portable files together; managed single-file publish produced native sidecars. Candidate is unsigned and built from an identified dirty-source snapshot, not an approved immutable commit. It has **not been launched as a packaged app**. Old reviewed outputs were not overwritten or relabeled.

### Remaining gates

The bounded migration and named automated checks are complete, not the full launch-playbook milestone. Open: Windows support-matrix approval (no blanket Windows 10/11 promise), reduced-beta feature gates, source review, packaged safe-mode launch, native consent/cancellation/error/recovery acceptance, candidate installer lifecycle and repair reboot suppression, keyboard/Narrator/focus/high-contrast/DPI acceptance, hosted CI after owner billing resolution, production signing/trust and release/channel approval. Old payload receipts cannot qualify this new candidate. No cleanup, native remediation, installer lifecycle, reboot, commit/push, signing, external publication, billing change or tester contact occurred. Publisher remains disabled. No beta availability is claimed.

---

## Historical initial blocked-attempt report — superseded above

Date: November 24, 2026 (host Pacific time).

**NOT COMPLETE / HOLD.** The approved bounded .NET 10 migration stopped at the official SDK download prerequisite. No project was retargeted, no SDK pin was added, no dependency was changed, and no new candidate was built or published. Existing test counts are historical baseline evidence, not results for .NET 10.

## Preserved baseline and source identity

Durable local evidence root (outside the repository and automatically pruned scratch):

```text
C:/Users/blunt/Desktop/wincare-runtime-migration-20261124-230712/
```

- `baseline/`: full copy of all **2,068 files / 2,565,163,247 bytes**, including `.git`, dirty/untracked source, reports, generated outputs and existing artifacts. Each copied file was SHA-256 checked against the original before proceeding. This is a local recovery copy, not proof of off-device backup.
- `source/`: separate development copy, excluding `.git`, `bin`, `obj`, `artifacts`, `publish`, `msix-stage` and `Screenshots`; no migration edits or builds occurred there.
- `evidence/baseline-sha256.json`: exact file-level baseline identity; the dirty source is not identified solely by HEAD.
- `evidence/baseline-git-status.txt` and `baseline-git-head.txt`: original worktree inventory and HEAD `06c8acc738c1c07ea02e15280619fe140c5110d5`.
- `evidence/preservation-verification.json`: final full baseline recheck, **2,068 files checked; zero changed, missing or added files**, before creating this report. That includes every existing reviewed portable/package and the disabled publisher. This report is the sole subsequent repository addition.

## Discovery and blocked download

Read `CI-GAPS-FIXES.md` and `WINCARE-LAUNCH-PLAYBOOK.md` in full, all four project files, CI workflow, audit scripts and release-build helper. Confirmed installed SDK **8.0.428** and Microsoft.NETCore.App / Microsoft.WindowsDesktop.App / Microsoft.AspNetCore.App **8.0.34**. No isolated SDK was extracted or run.

Fresh official metadata was downloaded from:

```text
https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json
```

Its `latest-sdk` was **10.0.404** and `latest-runtime` **10.0.15**. These are discovered metadata values, not a tested toolchain. Raw metadata and the exact selected file are retained in `evidence/releases-10.0.json` and `evidence/sdk-selection.json`.

Selected Windows x64 SDK ZIP:

```text
https://builds.dotnet.microsoft.com/dotnet/Sdk/10.0.404/dotnet-sdk-10.0.404-win-x64.zip
Expected bytes (official server HEAD): 303618098
Expected SHA-512 (official metadata):
330ddd2665b43cd2e428216a0d8ea1ac7bb19b5434fdc530e62010dc3b52823a620bf95489d628e1ed6f5122401793ee4887cca4a019122361a1231497f632ee
```

Actual executed attempts:

1. `python C:/Users/blunt/AppData/Local/hermes/cache/scratch/wincare_sdk_setup.py` — foreground tool timed out after **600 seconds**, exit **124**. Partial ZIP size **45,088,768 bytes**. Tool output additionally reported MSYS bash child-copy/signal-pipe errors (Win32 errors 299 and 5); no causal conclusion is asserted.
2. Read-only HTTP HEAD confirmed the expected content length above.
3. `python C:/Users/blunt/AppData/Local/hermes/cache/scratch/wincare_sdk_ranges.py` — alternative bounded HTTP range retrieval, requiring HTTP 206 and exact Content-Range. Exited **1**, `TimeoutError: The read operation timed out`. Some complete ranges were retained under `download-parts/`, but the archive was not assembled.

**SHA-512 verification was not reached. No archive was extracted or executed.** Partial downloads are untrusted, not usable SDKs. No machine install, PATH/registry mutation, admin operation or certificate generation occurred. Per the task's explicit download-blocker stop rule, no third acquisition attempt or unverified workaround was attempted.

The exact helper scripts are copied to `evidence/` for reproducibility. Download failures above are transcribed from actual tool outputs; they are not described as successful download receipts.

## Project/tooling inventory and dependency decisions

All four projects remain `net8.0-windows`:

| Project | Existing direct dependencies | Migration status |
|---|---|---|
| `WinCareDesktop.csproj` | CommunityToolkit.Mvvm 8.2.2; System.Management 8.0.0 | Unchanged, .NET 10 compatibility not tested |
| `WinCareDesktop.Tests/WinCareDesktop.Tests.csproj` | Microsoft.NET.Test.Sdk 17.11.1; xunit 2.9.2; xunit.runner.visualstudio 2.8.2 | Unchanged, .NET 10 discovery/STA execution not tested |
| `Tools/RenderShots/RenderShots.csproj` | App project reference | Unchanged |
| `Tools/MakeIcon/MakeIcon.csproj` | No PackageReferences; WPF and WinForms enabled | Unchanged |

No speculative package upgrades or compatibility claims were made. Evaluating the supported System.Management line and test SDK/adapter compatibility remains pending actual .NET 10 restoration/build/test evidence and official breaking-change review.

Confirmed framework-specific migration touch points: both setup-dotnet entries in `.github/workflows/ci.yml` currently use `8.0.x`; its WPF comment references `net8.0-windows`; `packaging/Test-Packaging.ps1` and `install.ps1` hardcode the net8 apphost path. README and GEMINI identify .NET 8; historical evidence reports must remain historical. `build-release.ps1` defaults to the existing `publish/` directory and removes its selected output directory: do not invoke it against reviewed output. A successful resumed migration must use an explicitly separate new candidate directory. No such script was executed here.

## Commands and actual verification counts

Executed from `C:/Users/blunt`, except initial read-only git discovery in the repository:

```text
python C:/Users/blunt/AppData/Local/hermes/cache/scratch/wincare_runtime_snapshot.py
python C:/Users/blunt/AppData/Local/hermes/cache/scratch/wincare_sdk_setup.py
python C:/Users/blunt/AppData/Local/hermes/cache/scratch/wincare_sdk_ranges.py
dotnet --list-sdks
dotnet --list-runtimes
python C:/Users/blunt/AppData/Local/hermes/cache/scratch/wincare_migration_verify_preservation.py
```

| Gate | This run |
|---|---|
| Full baseline snapshot and hash verification | Passed, 2,068 files |
| Final original-file preservation verification | Passed, 2,068 files; no differences |
| Official metadata retrieval | Passed; retained raw metadata |
| Complete SDK download / SHA-512 / extraction | Blocked / not reached / not performed |
| Retarget / SDK pin / dependency edits | Not performed |
| Fresh four-project restore/audit | Not run |
| Release build of all projects | Not run |
| Application tests | Not run; no new pass count |
| 44-case packaging regression suite | Not run |
| Five audit-policy scenarios | Not run |
| Self-contained win-x64 publish and payload manifest | Not run; no candidate exists |
| Tested-source to checkout identity check | Not applicable; no tested migrated source exists |

## Remaining gates and safe resumption

First obtain the complete official ZIP successfully and match its SHA-512 before extraction/execution. If resuming later, refresh official metadata rather than assuming this discovered version is still current. Preserve this baseline and recheck the current checkout for concurrent work before applying anything.

Then perform the authorized migration on the isolated source copy: review .NET 9/10 breaking changes; retarget all four projects; pin the actually tested SDK; align CI/tool paths; evaluate only justified dependency changes; run official-feed restore/audit, all Release builds, application tests, inert packaging regressions and audit-policy tests with safe mode and isolated persistence/temp; publish to a separate new directory; preserve resolved dependency/runtime metadata and complete payload hashes. Apply only verified minimal changes and compare source identity.

Not closed by this work: reduced-beta feature gates, Windows support-matrix approval, native action acceptance, repair reboot-suppression safety, new-candidate installer lifecycle, keyboard/Narrator/focus/DPI/high-contrast acceptance, hosted CI billing/run gate, source review, production signing/trust, release/channel approval and tester contact. Earlier package receipts cannot qualify a future runtime-changed payload. Publisher remains disabled. No beta availability or release approval is claimed.
