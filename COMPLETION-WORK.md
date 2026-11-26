# WinCare completion work

## Accepted goal
Finish the existing Windows WPF desktop application with truthful, safe, working behavior; do not force a health score of 100. Preserve the existing UI direction. No publication, signing purchases, real uninstall, privacy/DNS mutation, or destructive cleanup on this PC is authorized by this development task.

## Baseline (audit report, parent verification pending)
Commit 06c8acc738c1c07ea02e15280619fe140c5110d5, project 2.0.0. Debug build reported zero warnings/errors. Safe subset reported 132 tests passed. Two tests excluded because they write real scheduler configuration. Existing passing tests do not prove mutation paths safe. Source was clean before this note.

## Completion gates
1. Isolate all test persistence and system mutations; run full suite only after reviewing safety.
2. Fix disk deletion confirmation/protected paths/recovery; truthful confirmed uninstall including Store apps; reversible startup state.
3. Fix accurate privacy state/readback/history and DNS adapter resolution with previous-state preservation.
4. Implement explicit opt-in bounded auto-clean lifecycle without activating previously persisted unreviewed schedules; surface failures. Do not silently enable destructive defaults.
5. Fix actual reclaimed-byte accounting, maintenance preview/confirmation, awaited truthful rollback; correct network/disk/standby behavior or labels.
6. Verify full isolated tests, clean Release build, read-only rendered/live UI, and local deliverable launch. Report remaining administrative/destructive/manual checks as unverified, never as passed.

## Safety / execution
Use temporary fixtures under Hermes scratch. Never execute cleanup/uninstall/registry/network changes against the user's actual configuration for verification. No automatic dependency upgrades, Git commit/push/release, or modification of other profiles. Preserve uncommitted work.

## Observed audit findings to reproduce
- DiskAnalyzerViewModel deletes targets recursively without confirmation.
- AppUninstallViewModel ignores exit/errors and reports successes; omits Store apps and truncates to 150.
- StartupManagerService loses disabled Run entries and mishandles .disabled files.
- PrivacyViewModel fails to read back state; four claimed shields correspond to two settings.
- DNS query uses invalid NetConnectionID property on Win32_NetworkAdapterConfiguration.
- SchedulerService only stores preferences; unsafe tests write real schedule.json.
- Cleanup counts directory bytes before deletion; Quick Optimize hides failures and skips preview.
- History rollback status is set before asynchronous completion.
- Disk depth/result limits are unlabeled; network shows cumulative bytes as live throughput and auto-pings externally.
- Standby flush caches first result instead of executing subsequent requests.

## Acceptance refinements from independent plan review
Review: C:/Users/blunt/AppData/Local/hermes/cache/scratch/wincare-plan-independent-review.md. Findings are plan gaps/read-time source traces, not verified regressions in concurrent implementation.

Conservative implementation defaults (not authorization to execute mutations on this PC):
- Temp cleanup acts only on the confirmed file snapshot within allowed roots; refuse changed targets, root deletion, traversal and reparse escapes. Revalidate after confirmation. Disk recycle must never silently fall back to permanent deletion.
- Auto-clean is explicitly app-open-only, temp-only, with new consent required. No OS task registration, old-schedule activation, or catch-up storms. Define bounded items/bytes/time and test concurrency/cancellation with fake time.
- Persist recovery state successfully before reversible mutation. Refuse undo when the user/admin has since changed the same value; no silent overwrite.
- Every uninstall entry point uses confirmed structured outcomes; uninstall history is display-only unless recovery is actually proven. Distinguish pending/reboot/unknown from verified removal.
- DNS uses explicit stable adapter identity and disclosed IPv4-only scope, preserving automatic/static mode and ordered servers; IPv6 untouched.
- Test/render/safe-launch composition blocks production mutation adapters, uses isolated stores and fake clocks; constructors/navigation must not trigger cleanup or external probes.
- Deliverable target: local self-contained portable Windows publish, with version/source identity and verified safe-mode launch. No installation, signing or public release.
- Verify dashboard and all existing navigation views plus loading/empty/failure/partial/busy and confirmation states. Reconcile UI/README/history claims. Native hover, administrator and destructive checks remain explicitly unverified unless separately observed safely.

These refine safety and verification, not promises that the checks have passed. Unresolved behavior or inability to satisfy a gate remains open.

## Status
Local implementation and bounded review passes finished. Final evidence: FINISH-UP-RESULTS.md and INDEPENDENT-REVIEW.md. Final isolated TRX: 191 executed/passed, zero failed/not executed. Normal SDK-targeted .NET/WindowsDesktop 8.0.34 portable restored/published and safe-mode launched successfully; no cached-runtime override. Parent independently parsed final TRX and matched all portable file hashes and captured source hashes against finish-evidence-summary.json.

Current local candidate: C:/Users/blunt/Desktop/wincare-desktop/artifacts/completion-portable-normal/WinCare Pro.exe (keep all native DLL sidecars).

Acceptance remains HOLD, not 100% or production-ready: native/admin/destructive integration requires a disposable Windows environment; full modal/keyboard/DPI and remaining crash/filesystem/scheduler adversarial matrices are incomplete. No real user settings were mutated, no installer/signing/public release performed. Background implementation workers have finished; further validation is not silently continuing. Old cached-runtime portable is superseded by completion-portable-normal.
