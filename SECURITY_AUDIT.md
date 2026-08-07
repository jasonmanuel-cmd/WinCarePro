# Security Audit: WinCarePro v1.3.0

Date: 2026-08-02

## Checks

- Bandit static scan over application source, excluding tests/build artifacts.
- `pip-audit` against `requirements.txt`.
- Manual review of privilege boundaries, subprocess use, URLs, licensing,
  registry mutations, deletion paths, and audit logging.

## Resolved

- Removed generic command-shell execution from command helpers, CHKDSK, Windows
  Update cache renaming, DNS changes, and registered uninstall commands.
- Escaped network-adapter names used by PowerShell and changed netsh fallbacks
  to argument arrays with checked return codes.
- Restricted the configurable Ollama endpoint to local HTTP loopback addresses.
- Added exact, logged System Changes Preview confirmations for privacy, DNS,
  TCP gaming, and RAM mutations.
- Retained file rescan validation, reparse-point rejection, double confirmation,
  and verified duplicate-copy preservation.
- Online licensing continues to fail closed when Gumroad cannot verify a key.
- License persistence now fails closed when the protected local signature cannot
  be computed; it cannot create an unsigned in-memory Pro entitlement.
- Interrupted or failed update downloads remove partial executables. Update
  verification requires HTTPS, SHA-256, a valid Authenticode signature, and an
  exact signer-subject match.
- Release signing and verification subprocesses no longer open console windows.
- Commercial packaging refuses a missing signing certificate, and CI no longer
  masks Bandit or dependency-audit failures.

## Results

- Runtime dependencies: no known vulnerabilities.
- No embedded API keys or credentials were found.
- 95 unit/regression tests and five WPF UI Automation tests pass on the host.
- Bandit reports no medium/high findings and `pip-audit` reports no known
  vulnerabilities in `requirements.txt`.
- Destructive Windows mutation tests were not executed on the daily-use host.

## Remaining boundary

The local license record is a convenience cache, not a defense against a local
administrator modifying application state. Commercial enforcement should use a
signed server-issued entitlement or periodic online verification.
