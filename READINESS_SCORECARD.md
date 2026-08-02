# WinCarePro Guardian Readiness Scorecard

No gate passes from source inspection or intent. Each requires the evidence below.
The product is commercially ready only when all ten gates pass on the release artifact.

| # | Gate | Required evidence | Current state |
|---|---|---|---|
| 1 | Product capability | Full automated suite plus baseline, change, protected experiment, proof receipt, keep, and revert flows | PASS locally: 78 tests on 2026-08-02 |
| 2 | Data safety | Empty/corrupt/boundary tests, atomic state writes, retention limits, sensitive-detail redaction | PASS locally |
| 3 | Action safety | Explicit approval, separate destructive approval, verified rollback protection, fail-closed handlers | PASS for automated flows and isolated real HKCU mutation/rollback; production-action VM matrix pending |
| 4 | Security | Medium/high Bandit scan, dependency audit, secret review, fixed-command bridge validation | PASS locally |
| 5 | Accessibility | Keyboard, focus, names, roles, values, live regions, high contrast, screen reader on release artifact | BLOCKED: clean-VM UI Automation evidence missing |
| 6 | Build reproducibility | Pinned dependencies, full build, release hash, packaged launch | PASS on development PC; independent clean build pending |
| 7 | Installation lifecycle | Clean Windows 11 install, launch, upgrade, uninstall, and residue inspection | PENDING clean VM |
| 8 | Publisher trust | Valid Authenticode chain and expected publisher on engine and installer | BLOCKED: certificate not configured |
| 9 | Delivery integrity | Live HTTPS installer, signed update manifest, SHA-256 verification, rollback from failed update | BLOCKED: owner URLs/provider configuration missing |
| 10 | Marketplace trust | Accurate claims, privacy/support/refund disclosures, onboarding, pilot feedback, crash/support process | IN PROGRESS; no public certification claim |

## Current release decision

**NO-GO for broad commercial release.** Local engineering gates are strong, but
the unsigned artifact, clean-machine evidence, real mutation/rollback evidence,
provider configuration, and customer-support proof prevent a 10/10 claim.

## Evidence commands

```powershell
.\build.ps1
ruff check guided_care.py guided_care_cli.py tests\test_guided_care.py tests\test_guided_care_cli.py
bandit -q -r guided_care.py guided_care_cli.py -lll -iii
pip-audit -r requirements.txt
dotnet build WinCarePro.Desktop\WinCarePro.Desktop.csproj -c Release --no-restore
Get-AuthenticodeSignature dist\WinCarePro.exe
.venv\Scripts\python.exe release\mutation_rollback_fixture.py --execute
git diff --check
```
