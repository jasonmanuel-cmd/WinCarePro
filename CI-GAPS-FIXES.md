# Local CI gaps fixed — remote CI blocked; release HOLD / NO_SHIP

Date: November 24, 2026 (host Pacific time). This work closes the named local automation gaps in `GITHUB-TESTING-GAPS.md`; it does **not** establish a GitHub green run, supported-runtime migration, or release approval.

## Changes

- `.github/workflows/ci.yml`: runs the existing inert packaging regression suite in a child `pwsh` process after the build/test steps, even if application testing failed (unless cancelled). The child confines fake tools/signing environment; runner-local scratch contains fixtures. Application tests now produce TRX plus a console log. Test, packaging and render logs and fixture candidate receipts are retained with `if: always()` for 14 days; screenshots retain their existing separate upload. No fixture PFX or broad fixture payload directory is selected for upload. Read-only repository permissions and job timeouts are explicit.
- The workflow sets safe-mode/test-data/temp environment at job scope and telemetry settings at workflow scope. The old Environment step's process-only assignments did not persist to later steps. Logs now live outside the checkout, avoiding untracked-log failures in its existing cleanliness check.
- Fixed the existing multiline publish command to use YAML `run: |`, preserving PowerShell backtick/newline continuations instead of YAML folding. This is syntax/argument-wiring repair, not a new release or publish execution.
- `ci/Invoke-DependencyAudit.ps1`: explicit restore of all four projects (app, tests, RenderShots, MakeIcon), `--force --no-cache`, `NuGetAudit=true`, `NuGetAuditMode=all`, `NuGetAuditLevel=high`, and both `TreatWarningsAsErrors=true` and `-warnaserror`. Any restore warning/error fails the job, including high/critical advisories and advisory-source failures. Nonzero native exits are captured immediately; all project results and logs are retained. Low/moderate advisories are outside this threshold; passing is not a claim of zero vulnerabilities.
- `ci/NuGet.Config`: clears inherited package/audit sources and selects only the public NuGet v3 endpoint. NuGet 6.11 in the installed .NET 8 SDK uses `packageSources` for auditing; independent `auditSources` requires 6.12+. Both contain the same explicit endpoint so migration does not silently change the authority. No private feeds or credentials were read.
- `ci/Test-DependencyAudit.ps1`: five inert scenarios using **real installed dotnet/MSBuild**, with SDK-free dummy Restore targets and no package references/network. CLEAN must return success; NU1900, NU1903, NU1904 and NU1905 must return failure for every project and produce durable per-project error evidence. CI runs this policy regression before the real restore audit.
- This report is the fifth changed/new repository file. `packaging/Test-Packaging.ps1`, application source/projects, package/signing builders, and the disabled publisher were not edited by this task.

The ordinary CI package fixtures validate control flow, receipts and byte-identity checks with inert tools. They do not validate a real MSI/MSIX, actual signatures, Windows Installer lifecycle, or a final release payload manifest. Publisher `release-to-github.ps1` remains the unconditional HOLD. No new publication trigger/job was added; existing CI push/PR/manual triggers are unchanged.

## Executed local verification

All build/restore/test outputs were isolated in a separate source snapshot under:

```text
C:/Users/blunt/AppData/Local/hermes/cache/scratch/wincare-ci-gaps-20261124-222639/
```

The snapshot copied current tracked and untracked inputs, excluding generated/artifact/package binaries. It is not an approved immutable commit. The original checkout's reviewed portable and build outputs were not rebuilt. `WINCARE_SAFE_MODE=1`, `WINCARE_DATA_ROOT`, `TEMP`, and `TMP` pointed to isolated scratch for local execution.

| Check | Actual result / evidence |
|---|---|
| Installed toolchain discovery | Only SDK **8.0.428**; Microsoft.NETCore.App, Microsoft.WindowsDesktop.App and Microsoft.AspNetCore.App **8.0.34**. Local pwsh **7.7.0**. No .NET 10 SDK/runtime installed. |
| Audit policy regression | **5 scenarios passed**, exit 0. Each checks all four project exits and reports. `audit-policy.stdout.txt`; fixture logs beneath `audit-policy-ca888f3d99314e17b5d78abdd3eb3729/`. |
| Real dependency restore/audit | All **4 project restores exit 0**, against explicit NuGet source, transitive/high-or-critical policy. `real-audit/audit-results.json` and per-project logs. This is a local audit result for this snapshot and current advisory data, not remote CI or permanent security clearance. |
| Release app build | Exit 0, **0 warnings / 0 errors**, `build.stdout.txt`. |
| Application tests with TRX | Exit 0, **303 passed / 0 failed / 0 skipped**. Actual TRX counters parsed; `test-reports/application.trx`, `application-tests.stdout.txt`. |
| Existing inert packaging suite | Exit 0, **44 passed / 0 failed**; `packaging.stdout.txt`. Fixtures beneath `packaging-ab5a3c17ae944e0f99e059da563a5017/`, including actual fixture candidate receipts. No real signing or installer tools called. |
| Workflow structure/syntax | PyYAML 6.0.3 parsed YAML and checked required steps, report retention, triggers, safe mode and permissions. PowerShell parser accepted **12** extracted run blocks/scripts without errors. No actionlint or hosted Actions execution claim. |
| Exit/argument wiring | Test and packaging wrappers preserve native exits 0 and 23 (**4 checks**). Publish argument vector verified through a harmless native Python argument recorder, without running publish. `workflow-validation.stdout.txt`. |
| Whitespace/preservation | Scoped `git diff --check` exit 0 (LF/CRLF normalization warning only). Publisher, app/test projects, and reviewed portable SHA-256 unchanged; `verification.json`. Reviewed portable remains `3e082629060d9580b74e34fdfe996e105bc5dabcb64fec6ff940bb3026db1859`. |

Commands executed from the scratch source snapshot (REPORTS and SCRATCH denote absolute scratch paths above):

```powershell
pwsh -NoLogo -NoProfile -NonInteractive -File ci/Test-DependencyAudit.ps1 -ScratchRoot SCRATCH
pwsh -NoLogo -NoProfile -NonInteractive -File ci/Invoke-DependencyAudit.ps1 -ReportsDirectory REPORTS
dotnet build WinCareDesktop.csproj -c Release --no-incremental --no-restore --nologo -v minimal
dotnet test WinCareDesktop.Tests/WinCareDesktop.Tests.csproj -c Release --no-restore --nologo -v quiet --logger "trx;LogFileName=application.trx" --results-directory REPORTS
pwsh -NoLogo -NoProfile -NonInteractive -File packaging/Test-Packaging.ps1 -ScratchRoot SCRATCH
```

The audit command writes restore intermediates: use an isolated source copy for local reproduction, not a reviewed artifact directory. CI itself uses a fresh checkout. Logs record the expanded paths. No real publish, render, host installer lifecycle, scan, cleanup, certificate/private-key operation, signing, SDK installation, commit, push, upload, remote dispatch, or billing change was performed.

Verification-tool issue: a first publish argument assertion used a PowerShell function substitute, which splits colon-form arguments differently from native executables. The checker was corrected to a harmless native Python argument recorder and passed; no workflow change was needed for that checker mismatch. Initial web extraction was unavailable; primary documentation was retrieved read-only via HTTPS instead.

## .NET 10 migration feasibility — assessment only

**Feasible candidate, not validated compatibility.** Current app/tests/tools all target `net8.0-windows`. .NET 8 and 9 reached end of support **November 10, 2026**; this investigation is November 24, 2026. Self-contained output still bundles that unsupported runtime. .NET 10 LTS support runs to **November 14, 2028** [S1–S2]. Microsoft's live release metadata returned SDK **10.0.404**, runtime **10.0.15**, release date **November 10, 2026**, active support [S3]. These are discovery facts, not permission to install or a tested SDK pin.

### Exact prerequisites and change scope for a separately approved migration

1. Obtain approval to install/use a supported **Windows x64 .NET 10 SDK** (the observed current SDK above is the candidate), including Windows Desktop targeting/runtime components needed for WPF build/test. Runtime-only installation is insufficient for retarget/build. No .NET 10 compile or execution can be claimed from this machine's SDK 8 inventory. Visual Studio was not inventoried because its usual `vswhere.exe` was absent; CLI validation does not require an IDE. If IDE work is wanted, separately verify its supported .NET 10 tooling.
2. Approve and pin the SDK via `global.json` and align **both** CI setup-dotnet steps. Decide dependency lock/restore policy and preserve resolved dependency/source manifests. No global.json or dependency lock is added prematurely here; current CI still explicitly selects 8.0.x and is **not** a supported-runtime release gate.
3. Retarget all four projects together: `WinCareDesktop.csproj`, `WinCareDesktop.Tests/WinCareDesktop.Tests.csproj`, `Tools/RenderShots/RenderShots.csproj`, `Tools/MakeIcon/MakeIcon.csproj`. Update hardcoded `net8.0-windows` apphost paths in `packaging/Test-Packaging.ps1` and `install.ps1`, plus documentation/tool expectations. Do not execute the historical installer helper just to assess it.
4. Direct dependency inventory: app has **CommunityToolkit.Mvvm 8.2.2**, **System.Management 8.0.0**; tests have **Microsoft.NET.Test.Sdk 17.11.1**, **xunit 2.9.2**, **xunit.runner.visualstudio 2.8.2**. RenderShots references the app; MakeIcon has no PackageReferences. Scratch assets resolve the app's Toolkit implementation from net6.0 and System.Management/System.CodeDom 8.0.0 from net8.0; test infrastructure includes Newtonsoft.Json 13.0.1 and System.Reflection.Metadata 1.6.0 transitively. Lower-target assets suggest no immediate target-framework barrier, **not** proof of .NET 10 runtime, analyzer/source-generator or adapter compatibility. Reassess a supported System.Management line and test SDK/adapter versions rather than blindly retaining or upgrading everything; run fresh restore/audit and actual compile/tests before choosing exact package versions.
5. Review **both .NET 9 and .NET 10** breaking changes for an 8-to-10 jump. Scope includes Toolkit generated MVVM code/analyzers, WPF resources/layout/dispatcher/UI automation, native interop/WMI, filesystem/registry/process handling, test discovery/STA fixtures, and self-contained win-x64 publishing. Microsoft's .NET 10 list specifically calls out empty Grid definition collections and incorrect DynamicResource usage; MakeIcon also enables both WPF and WinForms, relevant to newly ambiguous MenuItem/ContextMenu names [S4]. These are review targets, not diagnosed WinCare failures.
6. Approve a concrete Windows support matrix. Microsoft's current .NET 10 OS table enumerates supported Windows 11 editions/releases and selected Windows 10 Enterprise/IoT releases; it does **not** justify a blanket “all Windows 10/11” promise [S5]. Align documentation and installer/MSIX minimum-version policy only after that decision. RID win-x64 does not prove ARM-native support or desktop support on every server listed for .NET.
7. Build a **new** candidate under the approved SDK/dependency/source snapshot. Repeat restore/audit, app and packaging suites, publish/complete payload manifests, then the separately authorized native package lifecycle and keyboard/Narrator/DPI/UI acceptance. A runtime migration changes payload hashes and invalidates reuse of the old candidate's receipts/signatures/lifecycle evidence as proof of the new binary. Do not overwrite or relabel the current reviewed portable.

## Blockers not closed

- **GitHub billing lock:** `GITHUB-TESTING-GAPS.md` previously observed run `37699087058` jobs never starting because the account was locked for billing. This task did not re-query, trigger or alter GitHub. Local workflow repairs do not lift that restriction. Owner resolution and an actual clean Windows run of approved source remain required.
- **Runtime:** current .NET 8 binaries remain unsupported. No retarget or SDK install was authorized/performed; the assessment above is not migration completion.
- **Source-to-artifact/release:** extensive existing dirty/untracked work remains preserved. No commit, remote source binding, production signing, channel approval or publish exists from this task. Real final payload verification and channel-specific acceptance remain separate. Consume the other agent's installer-log findings without assuming success or rerunning host lifecycle work.
- **Interactive acceptance:** this work's safe-mode tests and inert packages do not certify Narrator, keyboard navigation, mixed DPI/high contrast or native action consent/recovery behavior.

## Primary references and reusable procedure

Retrieved November 24, 2026; primary source snapshots are retained in the scratch evidence directory.

```text
[S1] https://devblogs.microsoft.com/dotnet/dotnet-8-9-end-of-support/
[S2] https://raw.githubusercontent.com/dotnet/core/main/release-notes/10.0/README.md
[S3] https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json
[S4] https://learn.microsoft.com/en-us/dotnet/core/compatibility/10.0
[S5] https://raw.githubusercontent.com/dotnet/core/main/release-notes/10.0/supported-os.md
[S6] https://learn.microsoft.com/en-us/nuget/concepts/auditing-packages
```

Reusable bounded procedure: preserve the dirty checkout and reviewed artifacts; copy current inputs into scratch; validate audit policy with real MSBuild warning fixtures before a real dependency audit; test exact workflow exit propagation and native argument vectors; parse TRX rather than trusting a summary alone; retain receipts/logs without fake credential fixtures; distinguish local results from hosted CI, runtime migration and release acceptance. Recorded here rather than modifying profile-level workflows outside the delegated repository scope.
