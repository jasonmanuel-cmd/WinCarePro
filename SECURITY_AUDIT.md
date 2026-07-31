# Security Audit: WinCarePro v1.3.0

Date: 2026-07-28

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

## Results

- Runtime dependencies: no known vulnerabilities.
- No embedded API keys or credentials were found.
- Windows mutation E2E tests were not executed on the host.

## Remaining boundary

The local license record is a convenience cache, not a defense against a local
administrator modifying application state. Commercial enforcement should use a
signed server-issued entitlement or periodic online verification.
