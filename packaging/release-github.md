# GitHub / winget distribution — HOLD / NO_SHIP

The old unsigned freeware/build-and-upload instructions are withdrawn. `release-to-github.ps1` now refuses every invocation before any build, git tag, push or GitHub upload. There is no override.

Follow `../RELEASE.md` and `../FINISH-PACKAGING-FIXES.md` for candidate packaging and the unresolved release decisions. Both executable payload and final direct-download package require approved signing and verification, regardless of price. Packaging consumes an explicitly selected existing payload; it does not republish or generate an update-source endpoint.

The preserved reviewed portable is `artifacts/rescue-reviewed-portable/WinCare Pro.exe` with its sidecars. It remains unsigned and on HOLD. Existing MSI/MSIX and winget manifests are not approved release assets.

Before a future separately authorized release: approve channel/identity/runtime and native acceptance; freeze the complete source and final signed artifact hashes; independently review package extraction/signatures and installation; regenerate winget metadata with actual final version, URL, SHA-256 and per-user MSI scope. Do not run the historical push/upload commands or publish the stale manifests. No release automation beyond the fail-closed HOLD is authorized or implemented by this correction.
