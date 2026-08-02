# Guided Care + Proof Design

## Goal

Turn WinCare Pro from a collection of tools into a local, accessible PC-care workflow that scans, explains, previews, obtains approval, executes only allowlisted actions, verifies results, records history, and supports recovery.

## Product flow

`Scan -> Ranked care plan -> Preview -> Approval -> Execute -> Verify -> Timeline/report`

The WPF desktop application is the primary experience. Python remains the production diagnostics and repair engine. The bridge exchanges versioned JSON and accepts fixed command and action identifiers; it never executes AI-generated shell text.

## Components

1. **Care Dashboard** shows health score, urgent risks, recent change, profile, and one Start Guided Care action.
2. **Baseline Store** retains bounded local snapshots of health metrics and findings. Data never leaves the PC.
3. **Care Planner** maps findings to ranked, deterministic actions using safety, impact, confidence, and reversibility.
4. **Proof Engine** compares pre/post metrics and reports `verified`, `improved`, `unchanged`, or `failed`.
5. **Profiles** provide versioned Gaming, Work, Privacy, Battery, and Restore Defaults recommendations. Selecting a profile does not mutate Windows.
6. **Health Timeline** records scans, approvals, executions, outcomes, cancellations, and rollbacks in append-only JSONL.
7. **Weekly Report** summarizes score change, completed work, unresolved risks, and next steps from local history.
8. **Safety Center** provides reviewed-plan approval, separate destructive-action confirmation, activity history, Undo Center access, and a Stop control.

## Agent operating model

- **Health Analyst:** read-only scanner and baseline comparison.
- **Repair Planner:** deterministic ranking and plain-English explanation.
- **Guarded Executor:** just-in-time action invocation limited to a fixed allowlist after confirmation.

AI output is advisory and cannot create commands or action identifiers. A full reviewed plan can receive one approval, but deletion, application removal, registry, DNS/network, and long-running repair actions always require a separate confirmation.

## First complete package

The shipped vertical slice includes the dashboard, local baseline/history, ranked care plan, before/after proof model, five profiles, weekly report, cancellation state, JSON bridge, and WPF presentation. Existing WinCare Pro repair screens remain the execution surface for risky actions until their rollback and clean-VM mutation gates are proven. The initial executor accepts only injected, allowlisted handlers; missing handlers are reported as skipped, never inferred or synthesized.

## Storage and privacy

State lives under `%LOCALAPPDATA%\\WinCarePro\\care`. Snapshot history is capped at 30 entries. Timeline history is capped at 1,000 events. Writes use temporary-file replacement where state is rewritten; events are append-only JSONL. Paths and secrets are not included in care history.

## Failure handling

- Read-only scans have a bounded timeout and may be retried once by the user.
- Mutations are never retried automatically.
- Interrupted mutations are marked interrupted and never auto-resumed.
- Unknown commands, malformed JSON, unknown profiles, unknown actions, and absent handlers fail closed.
- Stop cancels scans between checks and prevents new actions. Native Windows repairs that cannot safely stop report that cancellation is pending at a safe boundary.

## Accessibility

All WPF controls expose UI Automation names, keyboard operation, visible focus, high-contrast-aware resources, and polite live status. The layout remains usable at 200% scaling. Clean-VM UI Automation and screen-reader validation remain release gates.

## Verification

- Python tests cover empty/corrupt state, retention boundaries, ranking, profiles, proof outcomes, reports, unknown inputs, approval denial, cancellation, and handler failures.
- The JSON bridge is exercised as a real subprocess.
- The WPF project must build with zero warnings and expose the complete dashboard without mock data.
- Ruff, compile checks, the full normal pytest suite, Bandit medium/high scan, dependency audit, and clean git diff review gate completion.
- No commercial or accessibility certification claim is made until signed installer, clean-VM mutation, UI Automation, screen-reader, install/update/uninstall, and provider checks are recorded.

## Assumptions

- Local-only operation is the default; no account, cloud service, scheduler, paid model, or new dependency is required.
- Existing diagnostic and repair engines remain authoritative.
- “Complete package” means the full guarded workflow above, not autonomous unrestricted Windows control.
