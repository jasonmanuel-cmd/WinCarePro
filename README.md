# WinCare Pro — v2.0.0 Flight-Deck Edition

Native Windows 10/11 (`.NET 10` / `WPF`) system optimizer, disk & duplicate analyzer, 7-vector security posture auditor, and privacy toolkit — built for **100% offline operation, zero background telemetry, previewed actions, and conflict-checked Undo History**.

> **Collaboration & Architecture Notes:**
> Built collaboratively by **Jason Manuel** (`@jasonmanuel-cmd`) and **Antigravity** (Google DeepMind).
> - Read **[`ANTIGRAVITY_COLLABORATION_NOTES.md`](./ANTIGRAVITY_COLLABORATION_NOTES.md)** for the full story of who we are and every subsystem we engineered together.
> - Read **[`TOP_30_MAINTENANCE_AUDIT.md`](./TOP_30_MAINTENANCE_AUDIT.md)** for our audit of the top 30 PC maintenance & anti-malware utilities and the strict honesty rules derived from it.
> - Read **[`MONETIZATION-READY-CHECKLIST.md`](./MONETIZATION-READY-CHECKLIST.md)** for offline `WCP2.` ECDSA key generation, code-signing, and storefront deployment steps.

---

## Highlights in v2.0 Flight-Deck Complete Edition

- **SpaceX Crew Dragon–Inspired Flight-Deck UI + Full Obsidian Dark Deck:** Clean `#0B1120` obsidian command sidebar and top HUD telemetry pills (`MEM //`, `FREE //`, `INDEX //`, `AIR-GAPPED // OFFLINE`) with a live runtime toggle in **Options** (`Services/ThemePaletteService.cs`) to switch into **Full Obsidian Dark Deck** mode (WCAG AA contrast verified across both modes).
- **Real PC Speed & Kernel RAM Relief:** Sub-5 µs `GlobalMemoryStatusEx` physical RAM probe (`Services/NativeMemoryReader.cs`), reversible Power & Responsiveness switcher (`UndoKind.PowerPlan`), High-Impact Startup Manager (`UndoKind.StartupEntry`), Hardware/Boot Uptime/SMART health strip, and Authenticode-verified graceful app close (`VLC`, `Spotify`, `Discord`, `Steam`, `Epic`).
- **7-Vector Read-Only Security & Malware Posture Audit (`Services/SecurityThreatAuditService.cs`):** Inspects `HOSTS` hijacks, WinINET `PROXY`/`PAC` interception, `FIREWALL` profiles, `UAC` (`EnableLUA`), `SMARTSCREEN`, UEFI `SECUREBOOT`, and `STARTUP` LOLBins / `%TEMP%` droppers — plus single-file **SHA-256 + Authenticode + Mark-of-the-Web triage** and confirmed **Microsoft Defender targeted file scans**.
- **Deep Safe Cache Scanner & SHA-256 Exact Duplicate Finder:** Previews regenerable browser HTTP/Code/GPU caches, DirectX/GPU shader caches, and WER crash dumps (`Services/DeepCacheScannerService.cs`, skipping locked files and never touching passwords/cookies), plus a 3-stage SHA-256 exact duplicate finder (`Services/DuplicateFileFinderService.cs`, `≥ 1 MB`) with Recycle Bin removal.
- **100% Offline Cryptographic Licensing & Storefront:** Air-gapped ECDSA P-256 `WCP2.` license verification (`Services/LicenseService.cs`), `Tools/LicenseGen` CLI, static website (`website/`), and 7 `1280×800` Microsoft Store screenshots (`installer/StoreListing/`).
- **946 Passing Automated Checks (`0` warnings / `0` errors):** `902` xUnit unit/UI/security/accessibility tests (`WinCareDesktop.Tests`) + `44` MSI/MSIX/signing safety gates (`packaging/Test-Packaging.ps1`).

## Current views and behavior

- **Dashboard:** measured RAM/storage/battery values and the existing weighted score; no forced perfect score. Cleanup first presents a scrollable exact-file preview and requires confirmation. It does not silently flush DNS or memory. The memory action trims only WinCare's working set, not the Windows standby list.
- **Disk Analyzer:** partial estimates, at most 50 top-level results, two-level folder measurement; unreadable entries may be omitted. Removal confirms the full path, rejects protected/reparse paths, checks for changes during confirmation, and requests Windows recycle-only removal. It has no permanent-delete fallback. Restore a successfully recycled item through Windows Recycle Bin.
- **Startup Apps:** current-user/system Run REG_SZ entries and the user startup folder. Registry commands are copied to a dedicated same-hive WinCare disabled store and read back before the original is removed. Re-enable refuses collisions. Startup-folder `.disabled` entries remain visible and are renamed back without overwriting an existing target. Other registry value kinds are not offered; startup impact remains a command-name heuristic, not a measured boot time.
- **App Remover:** classic programs plus current-user non-framework/removable Store packages, with no 150-row truncation. Confirmation lists targets and intended commands. Invalid/failed/unverified removals stay listed. Partial inventory failures are disclosed while the other source remains visible. Structured outcomes distinguish verified removal, pending, reboot required, unknown and failed; only verified removal removes a row. Classic uninstall waits up to two minutes and checks registration afterwards. Store removal requires an explicit inventory-verification marker. Cancellation/timeout can stop waiting without stopping a vendor operation already in progress; refresh before retrying. Uninstall history is display-only, including legacy reinstall records.
- **Privacy Shield:** exactly two current-user settings: Advertising ID and Tailored Experiences. This does not switch off Windows diagnostic telemetry. Read failures, absent values and unsupported DWORD values display Unknown and are not counted as protected. Previous DWORD value or absence is journaled before writing, then read back. Undo refuses an intervening external change. Unsupported value kinds and failed history writes block mutation. “Set Both On” is not a restore-defaults operation; use History for exact prior values.
- **Network & DNS:** cumulative adapter byte totals, not live rates. Latency testing contacts Google DNS only when explicitly requested. DNS changes require selection and confirmation of a particular adapter GUID, alter IPv4 only, do not enable encryption, and preserve automatic/static mode plus ordered static servers in history. IPv6 is untouched. Restore checks for an external change before writing.
- **Auto-Clean:** disabled on every app launch, regardless of saved enabled settings. A fresh confirmation enables Daily/Weekly local-time runs for the current app session only. No task, startup entry or background registration is created. Scope: only top-level user TEMP files older than seven days; at most 100 attempted files, 100 MiB reclaimed, 1000 entries inspected, and a cooperative 15-second loop limit. No subfolders, recycle bin, RAM, DNS or system caches. Files are permanently deleted. Missed runs are not replayed as a catch-up queue. Save disables the active schedule until reconfirmed; closing the window stops timers and requests cancellation. Re-enabling is refused while the previous run is still stopping. After sleep, one due attempt may run, never a queue of missed days; reversing the local clock does not replay a consumed slot.
- **History & Logs:** awaited rollback with visible outcomes. Privacy and DNS are the supported undo types. Cleanup and uninstall cannot be reversed here. Startup restoration is through its own panel; disk restoration is through Recycle Bin. Not every helper action creates a history record.

## Guidance and local diagnostic reports

The dashboard now prioritizes **measured system-drive headroom**, **high RAM usage**, optional privacy preferences, then unknown evidence. These are explainable thresholds (90% disk used / under 10 GB free; 85% RAM), not causal diagnoses or guaranteed speedups. Suggestions open existing panels; they never apply a change. The original numeric score is unchanged and is labeled a heuristic; missing RAM/storage capacity displays Unknown and hides the dashboard score rather than presenting a zero reading as healthy.

Guidance uses a separate timestamped read-only sample of RAM, system-drive capacity and the two supported privacy preferences. Samples older than one minute (or ahead of the clock) are marked stale and their actionable recommendations are withheld until rescanned. Live cards are separate observations. Rescan runs off the UI thread, has busy/cancel/error feedback, and discards results after cancellation. Windows reads are cooperatively cancellable between probes, not interruptible while a native query is blocked.

**Preview local report…** shows the exact JSON before saving through Save As. It includes sample/review UTC times, numeric headroom, two privacy states and guidance. Identifiers, file paths, app/process lists, command lines, credentials, logs and raw errors are omitted by construction. The internal `environment.txt` is never copied into this report. Nothing is uploaded. Metrics/times can still be sensitive; review before sharing. Rescanning does not silently change an open preview; reopen it to refresh. Save errors retain the preview for retry. A staged local write avoids truncating an existing report if writing fails.

Current **reviewed local candidate**: `artifacts/rescue-reviewed-portable/WinCare Pro.exe`; retain all sidecars. SHA-256: `3e082629060d9580b74e34fdfe996e105bc5dabcb64fec6ff940bb3026db1859`. This is unsigned and remains **HOLD / NO_SHIP**. See `RESCUE-BLOCKER-FIXES.md` and `FINISH-NATIVE-GATES.md` for the remaining native/VM gates. `rescue-actions-portable` is an earlier candidate. Previous artifacts are retained. Safe-mode instances scope their mutex to the isolated data root, avoiding interference with a running normal instance; production single-instance identity is unchanged.

## Bounded Rescue slice

- **Memory & Apps:** existing manual RAM/CPU observations are preserved. A separate bounded review allows individually confirmed graceful closing of signed, registered, standard-Program-Files VLC only, with same-user/session, process/start/image/window and protected-role checks repeated before the native request. Unknown apps remain manual. No force, tree termination, global RAM flush, or promised performance gain.
- **Defender:** manual protection/signature reads plus named Quick/Full confirmations and guarded `Start-MpScan` requests. Existing Defender policy can remediate threats; no hidden elevation or policy changes. Manual lifecycle refresh matches provider, request-time window, user and Scan ID; caller ownership cannot be proven from events. Missing/ambiguous/incomplete history stays unknown or blocks initiation. Bounded detection-history summaries are not a clean verdict. Windows Security Protection history remains the remediation handoff; WinCare does not cancel actual scans, delete threats, change exclusions or reboot.
- **Options:** atomic app-only preferences for 5/15/30/60-second live-header refresh (15 default) and opt-in future local environment/crash files (off default). Static UI uses no animated transitions. Reset touches app preferences only; scheduler and critical safeguards are unchanged. Corrupt/unsupported settings use private defaults with visible feedback; save failures retain applied values. Safe mode blocks preference writes and the Windows Security handoff. Manual diagnostic exports and required recovery history are separate from this privacy option.

## Cleanup safety

Manual cleanup accepts only files inside the configured temporary roots and never deletes root directories. Preview stores each file's size, creation time and last-write time. Execution consumes that snapshot, skips changed/locked/reparse/out-of-root files, deduplicates paths, retains empty directories and reports actual successfully removed bytes plus error counts. New files appearing inside a previewed folder are not implicitly authorized. Metadata checking reduces stale-preview risks but is not a handle-based defense against a malicious concurrent filesystem replacement.

## Safe verification

`--safe-mode` or `WINCARE_SAFE_MODE=1` selects read-only UI verification. Set `WINCARE_DATA_ROOT` to a scratch directory to isolate history, schedule preferences, environment reports and crash logs. Native mutation entry points are guarded; the default cleanup root set is empty and the scheduler cannot activate. Explicit injected test doubles/fixture roots can exercise mutations only in fixtures. Host telemetry, installed-application enumeration and startup enumeration remain genuine read-only queries. Tests initialize safe mode before constructing the UI. RenderShots also forces safe mode.

```powershell
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
# Set TEMP/TMP to an isolated scratch directory before tests.
dotnet test WinCareDesktop.Tests/WinCareDesktop.Tests.csproj --no-restore
dotnet build WinCareDesktop.csproj -c Release --no-restore
dotnet run --project Tools/RenderShots/RenderShots.csproj --no-restore -- <scratch-output-directory>
```

RenderShots navigates all eleven current views, waits for existing scans to finish, and captures WPF renderings. Explicit Rescue fixtures cover memory samples/busy/cancel/error, Defender protection modes/error and Options failure, plus minimum-window renders. Additional explicitly named fixture captures cover unknown privacy state and partial/loading/busy/outcome/empty app inventory. These are not evidence of native hover, keyboard/UAC flows, destructive behavior, every resolution/DPI, or visual coverage of all failure states. Existing layout tests exercise multiple window widths.

## Earlier portable artifact (superseded)

The completion artifact is under `artifacts/completion-portable/`. Launch `WinCare Pro.exe --safe-mode` for inspection. Keep all published files together: native libraries may be sidecars even with managed single-file bundling. The verification-only runtime override in `artifacts/cached-runtime-verification.targets` uses already-cached .NET 8.0.30 runtime packs with the installed SDK's apphost because 8.0.34 runtime packs were absent. It is not a recommendation to distribute an older runtime. Rebuild with an approved current runtime before any release. No installer, registry registration, signing or publication is performed by this completion task.

## Architecture

- `MainWindow.xaml` / `ViewModels/MainViewModel.cs`: navigation and shared state.
- `Views/`, `ViewModels/`: dedicated MVVM panels; `Themes/Theme.xaml`: shared styles.
- `Services/SystemOptimizerService.cs`: telemetry, manual cleanup snapshots, privacy and history.
- `Services/PathSafety.cs`, `RecycleFileService.cs`: target guards and recycle-only shell operation.
- `Services/StartupManagerService.cs`: reversible startup command storage.
- `Services/AppxService.cs`, `InstalledProgramsService.cs`: package/program enumeration and commands.
- `Services/DnsSettingsService.cs`: adapter-specific IPv4 DNS journal/readback/restore.
- `Services/SchedulerService.cs`, `SchedulerRuntime.cs`: preferences and session-consented bounded lifecycle.
- `Services/SafetyMode.cs`, `HistoryStore.cs`: verification boundaries and atomic persistence.
- `WinCareDesktop.Tests/`: original tests plus test-first completion regressions.

## Packaging and release status — HOLD / NO_SHIP

`FINISH-VALIDATION.md` records the November 24, 2026 full suite: **303 passed, 0 failed, 0 skipped**, and a non-incremental Release build with **0 warnings, 0 errors**. These are safe local tests/build evidence, not native-action or distribution approval. They did not rebuild the reviewed portable.

The packaging fixes are covered separately by **23 inert PowerShell fixtures** (`packaging/Test-Packaging.ps1`); no real signing, installer execution, certificate creation, or publishing occurred in those fixtures. See `FINISH-PACKAGING-FIXES.md` for exact results and limits.

`build-installer.ps1` and `build-msix.ps1` now require an explicit existing payload, a new output directory and explicit tool selection. Neither republishes or signs automatically. By default they require verified timestamped executable/library signatures before packaging; `-UnsignedCandidate` is only a local candidate mode and never a release approval. Every outer package remains unsigned until separately authorized package-stage signing. MSI verification includes a native administrative extraction and must only be run in an approved disposable environment, not on this preserved host.

`sign-release.ps1` requires explicit targets and a Payload or Package stage; signing and verification failures throw. No automatic certificate creation/export is performed. `release-to-github.ps1` unconditionally stops at HOLD before builds, tags, pushes or uploads, with no override. Channel, production signing identity, approved runtime/toolchain, native acceptance, and final signed-artifact review remain unresolved. See `RELEASE.md`.
