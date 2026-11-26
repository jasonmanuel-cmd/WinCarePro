# WinCare GitHub and testing gaps

## Outcome and scope

**Read-only investigation complete; release remains HOLD / NO_SHIP.** Observed host clock at discovery: `2026-11-24T22:14:09.728577-08:00`. Findings below distinguish live GitHub observations, inspected source, prior execution reports, and proposed work. This is not a new test run or accessibility certification.

Only this report was written. No app launch, UI interaction, install, download of executable/package assets, source edit, credential access/export, signing, purchase, commit, release, upload, or remote mutation was performed. Existing authenticated `gh` was used for reads; auth output was filtered to suppress token-related lines. Installer launch/lifecycle work belongs to the other agent and is not preempted or certified here. The working tree is concurrently owned; source observations describe the inspected snapshot.

Read `FINISH-NATIVE-GATES.md`, `FINISH-PACKAGING-FOLLOWUP.md`, `HOST-NATIVE-TEST-RESULTS.md`, `HOST-TEST-PREFLIGHT.md`, project/CI/signing/packaging files, shell and selected complete UI/test files. Earlier native-gates packaging defects are not repeated as current defects: current scripts select an explicit payload, enforce identity checks, separate signing stages, and disable publishing.

## 1. Live GitHub observations

- `origin`: `https://github.com/jasonmanuel-cmd/WinCarePro.git`; public; default branch `master`.
- Separate `fork` remote: `https://github.com/jasonmanuel-cmd/winget-pkgs.git`. This is not WinCare's application issue tracker. No winget submission was attempted.
- `gh auth status` exited 0; active account `jasonmanuel-cmd`, keyring-backed, HTTPS. No token value or credential file was read into the report.
- Paginated all-state issues endpoint returned no entries; all-state PR list and independently paginated pulls endpoint also returned no PRs. There is no discovered issue/PR backlog to import.
- Active workflow: `CI`, ID `376152896`. Remote `.github/workflows/ci.yml` content was decoded and compared in memory: **equal to local CI file**.
- Latest returned run: `37699087058`, created `2026-10-07T22:54:23Z`, commit `ea8e246b106e09d5fa7aa85e627d8706e8740bf0`; completed/failure. The bounded latest-ten run list contains failures throughout, not green evidence.
- Latest run's `build-and-test` and `security` jobs each have **no steps**. Both check annotations state: **“The job was not started because your account is locked due to a billing issue.”** This explains this run, not necessarily every historical failure. `gh run view --log-failed` returned exit 1 / `log not found: 113057935264`; annotation retrieval established the reason instead. No billing settings were accessed or changed.
- Remote `master` points to that same SHA. Local status includes many modified and untracked application/test files, including `Services/DefenderScan.cs`, `Views/DefenderView.xaml`, `WinCareDesktop.Tests/GuidanceUiTests.cs`, and `packaging/Test-Packaging.ps1`. Remote CI cannot establish correctness of that uncommitted working tree.
- Paginated releases endpoint returned `v1.0.0`, published `2026-10-06T07:52:42Z`, not draft/prerelease. Assets are `WinCare.msi` (60489728 bytes) and `WinCarePro-1.0.0-win-x64.zip` (60691796 bytes). Assets were **not downloaded**; signatures and byte equality to local historical installers are unknown. Local project version is 2.0.0. Do not present that old release as this candidate.

Reproducible read-only evidence commands, with `R=jasonmanuel-cmd/WinCarePro`:

```text
git remote -v
gh auth status                       # filter token-related lines before display
git status --short
gh repo view jasonmanuel-cmd/WinCarePro --json nameWithOwner,defaultBranchRef,isPrivate,url
gh api --paginate repos/jasonmanuel-cmd/WinCarePro/issues?state=all&per_page=100
gh api --paginate repos/jasonmanuel-cmd/WinCarePro/pulls?state=all&per_page=100
gh workflow list -R jasonmanuel-cmd/WinCarePro --all
gh run list -R jasonmanuel-cmd/WinCarePro --limit 10 --json databaseId,headSha,name,conclusion,status,createdAt,url
gh run view 37699087058 -R jasonmanuel-cmd/WinCarePro --json jobs,conclusion,headSha
gh api repos/jasonmanuel-cmd/WinCarePro/check-runs/113057935515/annotations
gh api repos/jasonmanuel-cmd/WinCarePro/check-runs/113057935264/annotations
gh api --paginate repos/jasonmanuel-cmd/WinCarePro/releases?per_page=100
```

Quote query-string endpoint arguments when entering them in bash. These commands were issued through Python subprocess argument arrays during this investigation, not unquoted shell strings.

## 2. Ranked concrete gaps and minimum closure evidence

### P1 — GitHub CI is administratively blocked, not a demonstrated code failure

**Verified:** latest run never started its jobs; billing annotation above. **Next:** owner resolves the account restriction through GitHub or provides an approved alternative runner/account. Do not spend money or move credentials automatically. After source is approved and captured, require one actual clean Windows run with retained test and build evidence. Rerunning the same job before the account restriction is addressed is not a code fix.

### P1 — Supported runtime decision is now overdue

**Verified:** app and test projects target `net8.0-windows`; CI requests `8.0.x` (`WinCareDesktop.csproj:27`, tests project:4, CI:27,116). Microsoft's current policy and `dotnet/core` state **.NET 8 reached end of support November 10, 2026**, before this November 24 investigation [S4]. Self-contained publishing does not remove the bundled runtime support issue.

**Recommended:** approve a supported runtime migration, preferably evaluate .NET 10 LTS (published support end November 14, 2028 [S5]), and its supported Windows versions. This is a scoped compatibility task, not permission to change framework targets here. Rebuild and rerun the existing suites plus packaging/lifecycle/UI checks affected by the runtime; preserve source/runtime/package provenance. Do not call the current binary supported merely because it launches. No claim is made that the existing NuGet packages have a specific vulnerability.

### P1 — Current local work has no immutable remote/source-to-binary gate

**Verified:** substantial dirty/untracked source, old remote head and old published release. No `global.json`, `packages.lock.json`, `Directory.Packages.props`, or repo NuGet config was found outside generated/artifact directories. Direct app packages are `CommunityToolkit.Mvvm 8.2.2` and `System.Management 8.0.0`; test packages are Test SDK 17.11.1, xUnit 2.9.2 and VS runner 2.8.2.

**Recommended:** once the owning agent finishes, create a reviewed source manifest including untracked files; record SDK, resolved dependencies and complete publish hashes. Pin the approved SDK and use an appropriate dependency lock/restore policy. Only an owner-approved future commit/push can make remote CI cover this work. Do not reset this tree or rebuild over the reviewed artifact. Existing byte-identical package extraction is valuable but is not proof that future edited source equals the reviewed portable.

### P1 — Native/UI acceptance remains distinct from unit and fixture passes

**Reported prior execution, not rerun here:** 303 application tests and 44 inert packaging cases; real unsigned MakeAppx pack/unpack in `HOST-TEST-PREFLIGHT.md`; real WiX build and administrative extraction with complete seven-file payload equality, plus 11 bounded cleanup assertions in `HOST-NATIVE-TEST-RESULTS.md`. These discharge their named scopes. WiX `-nologo` compatibility is no longer an unknown on this host.

**Still requires scoped evidence:** keyboard/Narrator/UIA, DPI/high contrast, normal-mode consent flows, remaining native actions and channel-specific package lifecycle. Host install/repair/uninstall is concurrently delegated; consume that agent's exact result rather than assuming it passed or rerunning it. Prior reports do not establish upgrade/downgrade, MSIX lifecycle/trust, supported-OS matrix or recovery coverage. No new library alone closes these gates.

### P2 — Existing CI omits the packaging regression gate and durable test evidence

**Verified:** `.github/workflows/ci.yml` builds, runs the application test project, publishes, checks an EXE exists, renders panels, uploads PNGs, and checks tree cleanliness. It does **not** invoke `packaging/Test-Packaging.ps1`, validate a package receipt, retain TRX/test logs as artifacts, or compare a complete payload manifest. Screenshot count is not a UI verdict. `windows-latest` is not the Windows desktop support matrix.

**Minimum proposal:** add the existing inert packaging suite to an ordinary unprivileged Windows job; supply a runner-local scratch root, capture its actual exit, and preserve test/log/receipt artifacts on failure. Produce TRX for application tests. Keep real MSI lifecycle and interactive desktop tests in a separately approved environment. No additional testing framework is necessary for this change. A later release job should consume exactly the approved payload rather than republish it.

### P2 — Security job lists advisories but has no explicit fail-closed audit policy

**Verified:** CI:120–125 calls `dotnet list package --vulnerable --include-transitive` and an advisory outdated-package listing; its independent security job has no explicit restore step. No local `NuGetAudit`/`WarningsAsErrors` policy was found in searched project/source/CI files. Actual job behavior cannot be inferred from the blocked run.

**Minimum proposal:** explicit project restore/audit with approved audit source, transitive coverage and severity threshold; fail on high/critical findings and distinguish advisory-service failure from a clean scan. Microsoft's NuGet documentation defines NU1900/NU1905 data-source failures and NU1903/NU1904 high/critical warnings, with `WarningsAsErrors` controls [S6]. Use the existing SDK rather than adding a commercial scanner. Run the real dependency audit only in an authorized workflow; this investigation is not a vulnerability-clearance result.

### P2 — Signing is implemented as a gate but not production-proven; publishing intentionally disabled

**Verified:** `sign-release.ps1:5–8,18–35` accepts explicit stage/targets/tool/timestamp, checks credential presence, signs then verifies. `release-to-github.ps1:5` throws unconditionally. The old implicit signing-order/publisher-bypass bugs in early reports are not current source findings.

**Minimum proposal:** owner chooses identity, custody and channel; validate signed payload -> package exact payload -> sign outer package -> extract/hash/signature/chain/timestamp checks. Keep publication disabled until acceptance and source/artifact binding pass. Existing approved SignTool is enough if an isolated approved signing setup exists. Azure's `artifact-signing-action` is an optional alternative, not a required product dependency: current README documents Windows runners, OIDC, signing account/certificate profile and explicit file lists [S7]. It requires separately approved account/identity/roles/costs and workflow integration; current local PFX script is not automatically compatible with a service action. Do not copy its sample wholesale or sign mutable broad globs.

## 3. Static accessibility inspection — HOLD, not runtime failure certification

Platform: WPF desktop. Target surfaces inspected in full: main shell, shared theme, App Remover, Network/DNS, Memory and Defender views; existing UI-test files. XML inventory also inspected accessibility attributes across App.xaml and every view. The acceptance reference is the existing native UI gate; WCAG mappings below are useful review categories, not a legal-compliance claim.

Positive source evidence: shared buttons have focus visuals (`Themes/Theme.xaml:149–165`); newer dashboard/Defender/Memory/Options views contain explicit names/live-region declarations. `GuidanceUiTests.cs:40–58` checks four dashboard buttons' names/focusability/focus style plus read-only preview. Those are meaningful checks, not keyboard or screen-reader execution.

| Finding / rule | Source evidence and user risk | Status and smallest next check |
|---|---|---|
| FORM-1, major potential, manual; WCAG 1.3.1/3.3.2/4.1.2 | App Remover filter (`Views/AppUninstallView.xaml:26–30`) has adjacent text but no programmatic label/name; row checkbox (:105) has no content/name association with the adjacent app name. DNS adapter ComboBox (`Views/NetworkMonitorView.xaml:96`) has a tooltip but no explicit name/label. | Verified markup gap; runtime peer-derived names **unverified**. Inspect UIA Name/LabeledBy and Narrator; name the filter, selection target and adapter purpose if absent. Do not blanket-label every normal text button. |
| BTN-1, major potential, manual; WCAG 4.1.2 | Shell navigation button contains Border/Grid/text (`MainWindow.xaml:99–122`) without explicit automation name. Memory row action uses repeated “Close this app…” (`Views/MemoryView.xaml:16`); DNS rows repeat “Apply DNS” (:122). | Inspect actual names and row context first. Bind a concise title/target name where runtime output is blank or ambiguous; verify selection state too. This is not a claim that composite WPF content always yields a blank name. |
| Focus/keyboard evidence gap; WCAG 2.1.1/2.4.3/2.4.7 | Current `PanelDismissTests.cs:22–46` executes navigation commands; it no longer tests the historical modal Escape/Tab behavior described in the remote commit message. Source search found no explicit shell focus-transfer handling. | Walk Tab/Shift-Tab through all navigation and current panels; confirm visible focus, command activation, actual consent-dialog Escape/default/return behavior. Native dialogs may supply correct defaults; source absence alone is not a defect. |
| Status announcement evidence gap; WCAG 4.1.3 | New views declare `LiveSetting=Polite`; App Remover status (:63) does not. No `LiveRegionChanged` raising was found in source search. | Capture actual UIA/Narrator events for busy/completed/partial/error states. A live-setting attribute alone is not an announcement trace. Avoid reading high-frequency telemetry continuously. |
| Contrast/high-contrast evidence gap; WCAG 1.4.3/1.4.11 | Theme uses fixed color values; no `HighContrast`/`SystemColors` handling was found. Palette tests sample selected static pairs, not every rendered state. PrimaryButton uses white foreground, while its inherited template changes the border background on hover (`Theme.xaml:178–179,190–205`). | Observe hover as well as keyboard focus, disabled and high-contrast rendering; the template/style interaction is a concrete **risk to verify**, not a measured failure here. |
| DPI/layout evidence gap; WCAG 1.4.4/1.4.10 (adapted desktop review) | Layout tests use 900/1100/1280/1600 widths at height 720; current window minimum is 980x650. Test checks the right edge, not full navigation/state scrolling or mixed DPI. | Test actual 1180x760 and 980x650 at 100/125/150/200%, text enlargement and mixed-DPI movement. Do not equate the requested 900 test width with actual width after WPF minimum-size coercion. |

No explicit `AutomationProperties.AutomationId` was found in inventoried XAML. This is **not itself an accessibility defect**: WPF x:Name/default peers may expose IDs. For repeatable external tests, inspect actual IDs and add stable IDs only where needed. No UI was launched or attached, so target size, focus order, screen-reader output, live events and rendered contrast remain unobserved. Safe-mode non-mutating navigation can be coordinated after the installer owner releases the launch slot; scan starts, DNS changes, uninstall/close-app confirmations and other host mutations remain outside this audit.

## 4. GitHub tools worth using — not installing here

| Tool | Verified fit | Minimum justified use / limit |
|---|---|---|
| FlaUI / UIA3 [S1] | Official README describes Windows UI Automation with WPF support and attach/launch helpers. Live latest-release API returned v5.0.0, published February 25, 2025; release notes add .NET 8 support. Repository not archived. | Optional **test-only** dependency in a separate desktop smoke project after approval. Attach to an owner-launched safe-mode candidate by verified PID; inspect names/states and keyboard paths, not only InvokePattern clicks. Start with navigation, read-only sampling, cancel and confirmation decline. Needs an interactive desktop and bounded waits; not a replacement for Narrator, DPI or native action tests. No product dependency or WinAppDriver server is required merely to try UIA3. |
| Accessibility Insights for Windows [S2, S3] | Microsoft's repo/overview describes an interactive UIA inspector and Axe.Windows rule evaluation with locally saved results. Latest-release API returned v1.1.2924.01; GitHub published_at is January 3, 2025 while body labels it the January 9 production release. Repository not archived. | Best first manual companion for UIA properties and saved accessibility findings, plus keyboard/Narrator traces. **External operator tool**, not bundled into WinCare. Its README documents telemetry; review/decline telemetry and avoid issue-upload features under this task's no-upload rule. No installation was performed. |
| Existing SDK/NuGet + existing packaging harness [S6, local CI] | Already matches the stack and most immediate missing automation. | First choice: fix actual CI availability, wire existing fixtures and explicit advisory gates. No second unit-test framework or broad redesign. |
| Azure Artifact Signing action [S7] | Official Microsoft/Azure repo documents Windows-only runner requirements, OIDC and explicit targets. | Conditional later option if owner chooses the service. Not a remedy for billing-locked Actions or absent publisher approval. Keep unsigned candidate tests separate from credentialed release jobs. |

**Recommended order:** resolve CI account access and runtime decision; finish current source/native owner work; capture approved source and rerun existing automated gates; perform the smallest manual UIA/keyboard/Narrator pass; only then add a narrow FlaUI regression harness for repeatable failures. Do not add both a new UI automation server stack and an accessibility library to the shipping app.

## 5. Exact primary sources and retrieval limitations

Sources were read November 24, 2026 via live `gh` API, primary repository raw text, and available web search. The requested `web.run` interface was not exposed by this subagent runtime. Native web extraction failed with a search-only-backend error; available search automatically fell back to other search backends, and primary README retrieval succeeded using in-memory HTTPS reads. No external source code was executed. A guessed older WPF documentation raw path returned 404 and is not used as evidence.

```text
[G1] https://github.com/jasonmanuel-cmd/WinCarePro
[G2] https://github.com/jasonmanuel-cmd/WinCarePro/actions/runs/37699087058
[G3] https://api.github.com/repos/jasonmanuel-cmd/WinCarePro/check-runs/113057935515/annotations
[G4] https://api.github.com/repos/jasonmanuel-cmd/WinCarePro/check-runs/113057935264/annotations
[G5] https://github.com/jasonmanuel-cmd/WinCarePro/blob/ea8e246b106e09d5fa7aa85e627d8706e8740bf0/.github/workflows/ci.yml
[G6] https://github.com/jasonmanuel-cmd/WinCarePro/releases/tag/v1.0.0
[S1] https://github.com/FlaUI/FlaUI
     https://raw.githubusercontent.com/FlaUI/FlaUI/main/README.md
     https://api.github.com/repos/FlaUI/FlaUI/releases/latest
     https://github.com/FlaUI/FlaUI/releases/tag/v5.0.0
[S2] https://github.com/microsoft/accessibility-insights-windows
     https://raw.githubusercontent.com/microsoft/accessibility-insights-windows/main/README.md
     https://raw.githubusercontent.com/microsoft/accessibility-insights-windows/main/docs/Overview.md
[S3] https://api.github.com/repos/microsoft/accessibility-insights-windows/releases/latest
     https://github.com/microsoft/accessibility-insights-windows/releases/tag/v1.1.2924.01
[S4] https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core
     https://raw.githubusercontent.com/dotnet/core/main/release-notes/8.0/README.md
[S5] https://raw.githubusercontent.com/dotnet/core/main/release-notes/10.0/README.md
[S6] https://learn.microsoft.com/en-us/nuget/concepts/auditing-packages
     https://learn.microsoft.com/en-us/nuget/reference/errors-and-warnings/nu1901-nu1904
[S7] https://github.com/Azure/artifact-signing-action
     https://raw.githubusercontent.com/Azure/artifact-signing-action/main/README.md
```

Reusable investigation procedure: inspect actual remotes and sanitize auth output; paginate issue/PR/release reads; inspect job steps and check annotations before diagnosing red CI; compare local/remote workflow bytes; distinguish source peers from runtime accessibility; consult primary tool docs; preserve current native passes while ranking only unclosed gates. Kept here instead of writing profile skills outside the report-only authorization.
