# WinCare launch playbook and roadmap

Status: PROPOSED PLAN — not release approval or a delivery guarantee.
Planning baseline: November 24, 2026 session evidence. Target private beta: December 14, 2026. Dates below assume focused engineering work, timely owner decisions, and no major safety defects. Move dates rather than waive gates. No purchases, account changes, publication, or SDK installation are authorized by this document.

## 1. Launch decision in one page

Start with a free, invitation-only beta for 10–20 people, using a small validated feature set. Expand only after evidence from different PCs. Target a public beta in January 2027 and consider a paid release in late January–February 2027; do not announce a fixed paid date yet.

Positioning proposal: “Understand your Windows PC, preview supported maintenance changes, and stay in control.” Avoid antivirus replacement, guaranteed speed gains, registry miracle fixes, scary countdowns, or unsupported recovery promises.

Proposed pricing hypothesis, not validated demand:
- Private/public beta: free, no card required, no automatic conversion to paid.
- Founding launch offer: US$19.99 for one PC, one major version.
- Regular offer: US$29.99 for one PC, one major version.
- Define maintenance/security-support duration and upgrade terms before checkout exists. Do not promise lifetime updates or all future major versions.
- No preorder collection. Early testers receive an optional founding offer after release, not an obligation.
- Prices are proposals; taxes, fees, support cost, licensing and refund terms need owner decisions before sales.

## 2. Starting evidence and limits

Existing local evidence (not proof of the next binary):
- 303 application tests, 44 inert packaging regressions, clean Release build.
- Five dependency-audit policy scenarios and four project audits passed under the documented high/critical policy.
- Real MSIX pack/unpack and MSI build/extraction verified payload identity.
- Bounded native cleanup passed 11 assertions using disposable files only.
- Install, safe-mode launch, repair and uninstall returned success; original data was preserved.

Open gates:
- Repair reboot-suppression behavior remains unproven; do not call its safety validation complete.
- Supported-runtime migration and revalidation are not done. .NET 10 is the assessed candidate, not yet installed or tested here.
- Keyboard, screen-reader, DPI/high-contrast and remaining native features lack acceptance evidence.
- Local CI edits are not a successful hosted CI run. The GitHub billing block needs owner resolution and rechecking.
- Signing identity, final source snapshot, final package and publishing approval are absent. Publisher is deliberately disabled.
- Spare-host test permission is not proof of a backup.

Evidence files: CI-GAPS-FIXES.md; HOST-NATIVE-TEST-RESULTS.md; HOST-INSTALLER-LIFECYCLE.md; HOST-TEST-PREFLIGHT.md; GITHUB-TESTING-GAPS.md; FINISH-NATIVE-GATES.md; FINISH-PACKAGING-FOLLOWUP.md. Independent repair review is retained under the lifecycle scratch evidence directory. Archive necessary evidence outside automatically pruned scratch before relying on it long-term.

## 3. Narrow beta scope

Candidate inclusion, only after validation on the new supported-runtime build:
- Read-only system overview and understandable guidance.
- User-requested diagnostic export with a preview, redaction and explicit consent.
- Cleanup preview and explicitly confirmed cleanup limited to tested paths and behavior.
- Clear progress, partial failures, cancellation behavior and accurate results.

Default OFF or unavailable until independently accepted:
- Automatic/scheduled cleanup.
- DNS, privacy and startup changes and associated undo.
- App closing and uninstall helpers.
- Defender initiation/remediation-related controls.
- Any feature whose rollback, permission failure or concurrent-change behavior is unverified.

Do not rely on release notes alone: unavailable actions must be disabled at command/service boundaries as appropriate, not just hidden visually. Do not describe a reduced beta as complete Pro. If cleanup is not ready, ship a clearly named diagnostics-only private preview instead, only after that preview's own gates pass.

## 4. Dated roadmap

### Phase A — baseline and decisions | November 25–29, 2026
Owner: product owner, with engineering support.
- Approve beta scope, tentative price and Windows support matrix.
- Preserve source, existing artifacts and reports in a backed-up location; confirm recovery arrangements before more host-changing tests.
- Inventory changes and prepare a reviewed source snapshot; do not discard existing uncommitted work.
- Resolve GitHub billing through the owner's account; never expose credentials in chat.
- Select signing/distribution route and budget; investigate lead times without assuming approval is instant.
Exit: written scope, named owner, backup location, issue list and signing/CI decisions or documented blockers.

### Phase B — supported candidate and safety | November 30–December 6
Owner: engineering; owner approves tool/service installation and spend.
- Approve and obtain the supported SDK; migrate all four projects and tooling paths in an isolated development copy/branch.
- Review dependencies and breaking changes; pin the tested toolchain.
- Execute build, application, packaging and audit checks; fix regressions.
- Fix/verify repair invocation semantics and safe restart handling; never test by risking an unapproved host reboot.
- Implement reduced-scope feature gates, then test that blocked actions cannot execute.
- Produce a new candidate with source identity, version, runtime/dependency inventory and hashes.
Exit: all automated gates pass on the new candidate; no unresolved data-loss/security/reboot-safety blocker. Earlier build receipts are not reused as proof.

### Phase C — beta qualification | December 7–13
Owner: engineering plus human test operator.
- Test install/launch/repair/uninstall on the exact new package; preserve existing user data.
- Test enabled features' confirmation, cancellation, refusal, error and partial-success paths.
- Verify keyboard-only navigation, visible focus, readable names, screen-reader behavior, contrast and supported DPI settings.
- Get consented testing on additional PCs/configurations. Prefer at least three distinct PCs total and record actual OS/account/display conditions; this is a proposed minimum, not a claim of broad compatibility.
- Complete signing/trust verification for distributed installers; do not ask public testers to disable protection or import a developer root certificate.
- Prepare install guide, known issues, privacy/data handling summary, support contact, uninstall instructions and feedback form.
Exit: beta checklist signed off by owner. If any gate fails, reschedule; do not relabel failure as beta quality.

### Phase D — private beta | target December 14–28
Owner: product owner for recruitment/support; engineering for fixes.
- Invite five testers first; expand toward 10–20 only after reviewing their results.
- Free access, no payment details, opt-in diagnostic sharing.
- Use short observed sessions and a fixed test script; no unsupervised destructive experiments.
- Triage reports daily during the opening week; release a new candidate only with fresh regression evidence.
- Review results December 28. Holiday availability may reduce useful feedback; extend instead of lowering evidence requirements.
Exit: no unresolved critical/high-impact safety defects, installation results understood, real value feedback, and a decision to expand, narrow or delay.

### Phase E — public beta | January 2027, conditional
- Publish a signed, versioned candidate through the approved channel with explicit known limitations and supported OS scope.
- Start with a controlled audience rather than a broad promotion campaign.
- Validate support capacity, diagnostic privacy, update behavior and incident response.
- Interview testers about usefulness and price; do not treat compliments as purchase intent.
Exit: stable evidence across the chosen support matrix; sustainable support; agreed commercial terms; no safety gate waived.

### Phase F — paid release | late January–February 2027, conditional
- Approve exact signed artifact and source snapshot; verify final embedded payload and final hashes after signing.
- Validate checkout, licensing, refunds/cancellation and recovery from payment/licensing outages using provider test facilities first.
- Publish clear one-PC/version/support terms and launch price; no invented discount deadline.
- Owner explicitly approves public release. Implement/review publisher separately; never remove HOLD merely to meet a date.
- Launch small, monitor, then expand. Do not promise a shipment date until these gates have evidence.

## 5. Private-beta tester playbook

Recruit: begin with people who understand beta limitations and have backed-up PCs. Suggested mix: nontechnical users, technical users, laptops/desktops, standard/admin accounts, and different display scaling. Accessibility participation must be voluntary; protect personal information.

Invitation draft:
“I'm testing WinCare, a Windows maintenance app focused on clear previews and controlled changes. The private beta is free. I'd like feedback on setup, clarity and the features marked ready for testing. Some features are intentionally unavailable. Please use a backed-up PC, and don't send passwords or personal files. Would you be willing to try it and share a short report?”

Tester session:
1. Record app version/hash, Windows version, account type and display scaling without collecting unnecessary identifiers.
2. Install and launch; note every warning and confusing screen. Stop for unexpected elevation/reboot requests.
3. Navigate with mouse and keyboard; explain what each action appears to do before clicking it.
4. Preview approved disposable test files; decline once and confirm nothing changes.
5. Confirm the bounded action, compare preview/result, and verify unrelated files remain intact.
6. Exercise supported cancellation/error cases under guidance; never deliberately damage the tester's system.
7. Export only consented redacted diagnostics; uninstall and confirm expected data-preservation behavior.
8. Ask: What helped? What felt risky? What would you normally use instead? Would you choose this at the proposed price, and why?

Feedback fields: version, Windows build, action, expected/actual behavior, reproduction steps, impact, optional redacted screenshot/log, contact consent. Never request passwords, private keys or full unfiltered system dumps.

## 6. Release gate checklist

Every included capability needs evidence tied to the final candidate:
- [ ] Supported runtime/toolchain and dependency audit.
- [ ] Source snapshot reviewed, artifact identity recorded, no unknown changes.
- [ ] Application and packaging checks pass; real package contents verified.
- [ ] Install/launch/repair/uninstall pass, restart policy verified, existing data preserved.
- [ ] Enabled native actions pass confirmation, cancellation, partial failure and boundary checks.
- [ ] Unsupported/untested actions cannot execute.
- [ ] Keyboard, screen reader, focus, contrast and supported DPI checked.
- [ ] Signed distribution artifact verified; instructions never require weakening security.
- [ ] Privacy/support/known issues/uninstall documentation reviewed.
- [ ] Owner approves release and channel; rollback/withdrawal procedure rehearsed.

Private beta may have cosmetic issues disclosed in known issues. It may not waive data integrity, honest results, consent or installation safety.

## 7. Metrics and stop rules

Track counts with denominators, not flattering percentages from a tiny group:
- Invitations, actual installs, successful launches, completed sessions and useful feedback reports.
- Install/uninstall failures by version and configuration.
- Crashes, unexpected changes, misleading results, recovery failures and support time.
- Ability to understand preview and complete a task without coaching.
- Voluntary return usage and concrete willingness-to-pay feedback.

Suggested go/no-go: at least 10 completed private-beta sessions; no unresolved critical safety issue; every failure triaged; all enabled-feature gates met. This is a planning threshold, not statistical proof of reliability.

Stop distribution immediately for unexpected deletion outside scope, security/privacy exposure, destructive consent bypass, misleading success after failure, broken recovery promises or unexpected restart. Preserve evidence, warn affected testers privately without exposing their data, disable the affected function or withdraw downloads, and provide tested recovery guidance. Do not blindly auto-downgrade binaries or undo settings that may have changed since the action. Resume only after reproduction, fix, independent review and regression validation.

## 8. Pricing and sales experiment

During private beta, show the same honest offer to a small group and record reasons for acceptance/rejection. Ask about alternatives and the job they need done. Do not add a subscription unless recurring value and ongoing service costs justify one.

Before charging, decide: license transfer/reinstall rules, what one PC means, offline behavior, support duration, paid-upgrade policy, refund terms, payment provider, sales jurisdictions and support contact. Obtain appropriate professional advice for jurisdiction-specific obligations rather than copying another product's policy. Track actual payment fees, signing/distribution costs, support time and refund rates before concluding the price is profitable.

If testers value only a free commodity cleanup feature, improve differentiation or narrow the product before selling. Do not compensate with fear-based marketing.

## 9. Weekly operating rhythm and responsibilities

Owner: approve scope, budget, accounts/identity, recruitment, price, terms and release. Engineering: implement and produce test evidence. Independent reviewer: verify fixes and artifacts. Test operator/testers: provide actual interaction evidence. These are roles, not people already engaged.

Monday: choose one release-blocking outcome and named owner; update dates based on evidence.
Midweek: review failing tests, blockers and tester reports; cut scope before adding features.
Friday: demonstrate the exact candidate, review acceptance evidence, and decide continue/hold/expand.

Keep a compact board: Backlog / Ready / Doing / Verify / Done / Blocked. A task is Done only with artifact, command/result or observed behavior, candidate identity and reviewer where needed. A written plan or delegated task is not execution.

## 10. Next five actions, in order

1. Owner confirms December 14 is a target, not a promise, and agrees to the narrow free-beta scope.
2. Back up source/evidence and resolve account/signing prerequisites; no billing or credential changes without owner action.
3. Approve supported-SDK setup and execute migration with a new candidate; preserve existing reviewed binary.
4. Close repair-safety and interactive accessibility gaps; run the full candidate acceptance checklist.
5. Prepare recruitment/docs and invite the first five testers only after beta gates pass.

This playbook does not itself schedule jobs, contact testers, create GitHub issues, install software, buy signing, accept payment, or publish a release.
