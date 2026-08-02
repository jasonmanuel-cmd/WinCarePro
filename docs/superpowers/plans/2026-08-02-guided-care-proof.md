# Guided Care + Proof Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship a complete local Guided Care workflow with baseline history, deterministic planning, profiles, proof, weekly reporting, a fail-closed JSON bridge, and an accessible WPF dashboard.

**Architecture:** Python owns domain logic and local persistence. A versioned JSON CLI composes the existing scanner and exposes read-only dashboard/scan commands plus guarded injected-action execution. WPF invokes that bridge and renders the results; AI never produces executable commands.

**Tech Stack:** Python 3.11+ stdlib, existing psutil/Python engines, pytest, .NET 8 WPF, System.Text.Json.

**Status:** Implemented and locally verified on 2026-08-02. External release gates remain documented in `RELEASE_CHECKLIST.md`.

## Global Constraints

- No new dependency, cloud service, paid model, scheduler, account, or telemetry.
- Local state lives under `%LOCALAPPDATA%\\WinCarePro\\care`; snapshots cap at 30 and timeline events cap at 1,000.
- Unknown commands, actions, profiles, malformed state, denied approval, absent handlers, and interrupted work fail closed.
- AI may explain only; it cannot create shell text or action identifiers.
- Full-plan approval never covers deletion, application removal, registry, DNS/network, or long-running repairs.
- WPF controls require UI Automation names, keyboard operation, visible focus, high-contrast resources, and live status.
- Existing repair screens remain the execution surface for risky actions until clean-VM mutation and rollback gates pass.

---

### Task 1: Guided Care domain and persistence

**Files:**
- Create: `guided_care.py`
- Create: `tests/test_guided_care.py`

**Interfaces:**
- Consumes: scanner findings/metrics dictionaries and existing `AutoRepairEngine` concepts.
- Produces: `CareStore`, `CarePlanner`, `ProofEngine`, `CareProfiles`, and `WeeklyReport` with JSON-serializable results.

- [ ] Write failing tests for empty/corrupt stores, 30/1,000 retention boundaries, deterministic ranking, five profiles, proof statuses, cancellation/interruption events, approval denial, missing handlers, and weekly score/risk summaries.
- [ ] Run `..\\..\\.venv\\Scripts\\python.exe -m pytest tests/test_guided_care.py -q` and confirm failures are caused by the missing module.
- [ ] Implement immutable care action/snapshot/outcome records, atomic snapshot persistence, append-only bounded timeline, deterministic planner, profile catalog, injected allowlisted executor, proof comparison, and report generator using stdlib only.
- [ ] Re-run the focused tests, then the full normal pytest suite.
- [ ] Commit only `guided_care.py` and `tests/test_guided_care.py`.

### Task 2: Versioned JSON bridge

**Files:**
- Create: `guided_care_cli.py`
- Create: `tests/test_guided_care_cli.py`

**Interfaces:**
- Consumes: Task 1 classes, `core.scanner.Scanner`, `core.logger.AppLogger`, and `core.health.HealthScore`.
- Produces: one JSON object on stdout for `dashboard`, `scan`, `profiles`, `timeline`, and `weekly-report`; nonzero exit with a JSON error object for invalid input.

- [ ] Write subprocess tests proving every command returns schema version `1`, malformed/unknown commands fail closed, scan cancellation records an event, and dashboard works with empty state.
- [ ] Run the focused tests and confirm RED failures caused by the missing CLI.
- [ ] Implement argparse command dispatch, bounded read-only scan, local store composition, and JSON-only stdout. Send diagnostics to stderr and never accept raw command text.
- [ ] Re-run focused and full tests.
- [ ] Commit only the bridge and its tests.

### Task 3: Accessible WPF Guided Care dashboard

**Files:**
- Modify: `WinCarePro.Desktop/MainWindow.xaml`
- Modify: `WinCarePro.Desktop/MainWindow.xaml.cs`

**Interfaces:**
- Consumes: Task 2 JSON commands via a fixed Python executable/script resolution path.
- Produces: dashboard cards, profile selection, ranked plan, proof/timeline list, weekly report view, Start Scan, Refresh, Open Safety Center, Open Undo Center, and Stop controls.

- [ ] Add a build smoke check that fails before the new named controls and event handlers exist by compiling the intended XAML/code-behind change.
- [ ] Replace the preview-only dashboard with the complete Guided Care layout; keep risky action execution routed to the existing full WinCare Pro screens.
- [ ] Implement async bridge invocation with a 30-second dashboard timeout, cancel token, stderr/error handling, schema validation, accessible live status, and no shell command construction.
- [ ] Run `dotnet build WinCarePro.Desktop\\WinCarePro.Desktop.csproj -c Release --no-restore` and require zero warnings/errors.
- [ ] Commit only the two WPF files.

### Task 4: Product documentation and verification

**Files:**
- Modify: `README.md`
- Modify: `RELEASE_CHECKLIST.md`

**Interfaces:**
- Consumes: Tasks 1-3 behavior and commands.
- Produces: accurate customer-facing capability description and explicit release gates.

- [ ] Document Guided Care + Proof, local history/privacy, profiles, weekly report, approval model, and exact remaining clean-VM/accessibility/mutation gates without claiming certification.
- [ ] Run placeholder scan, Python compile, Ruff, full pytest, WPF Release build, Bandit medium/high, and dependency audit; record actual results in the task report.
- [ ] Inspect `git diff --check` and the complete branch diff for unrelated changes or secrets.
- [ ] Commit the documentation and any verification-only corrections.
