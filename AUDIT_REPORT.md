# WinCarePro v1.3.0 Audit Report

Audit date: 2026-07-28  
Target: Windows 11 daily use and portable commercial build

## Verified

- CodeGraph initialized: 19 Python files, 676 symbols, 1,959 relationships.
- Complete source GUI constructed successfully as Administrator.
- Full read-only 14-stage Windows diagnostic completed with no failed checks.
- PyInstaller 6.21 produced `dist/WinCarePro.exe`.
- Packaged executable opened a responsive `WinCare Pro v1.3.0 — Administrator` window.
- Automated tests, compilation, and import checks pass.
- No real cleanup, registry, service, DNS, application-removal, or repair action was executed during the audit.

## Resolved findings

### Critical

- Arbitrary `WCP-*` strings could activate Pro offline. Activation now fails closed unless Gumroad verifies the purchase.

### High

- Background RAM working-set trimming ran automatically without a visible opt-in. Startup automation was removed; manual RAM cleanup remains available.
- Duplicate cleanup permanently deleted paths without rescan metadata validation or guaranteed last-copy preservation. Deletion now validates scan identity, size, timestamp, and link status, and preserves a verified duplicate.
- Registered uninstall commands used a command shell unnecessarily. They now launch without `shell=True`.

### Medium

- The existing duplicate engine was not accessible from the application. It is now integrated under Processes & Cleanup.
- Version metadata disagreed between the source header and the product. All visible source metadata now reports v1.3.0.
- Installer generation was tied to one absolute checkout path. It now resolves its own directory and produces a path-independent spec.
- Leftover-folder deletion could report success when `rmtree` silently failed. Success is now counted only after removal.

## New cleanup safety contract

- Scans only the Windows-resolved Desktop and Downloads folders by default.
- Flags supported installer/archive files after 180 days and confirms duplicates using SHA-256.
- Excludes WinCarePro, symbolic links, reparse points, inaccessible paths, and files changed after scanning.
- Selects nothing automatically and requires two irreversible-deletion confirmations.
- Logs each success/failure and reports partial outcomes and reclaimed bytes accurately.

## Release status

- Daily-use read-only/startup gate: **PASS**.
- Security source/dependency gate: **PASS after remediation**.
- Accessibility gate: **BLOCKED** because CustomTkinter canvas controls do not
  expose application names, roles, values, or full keyboard traversal to UIA.
- Daily-use Windows mutation gate: **NOT RUN** to avoid altering this PC. Restore, registry, service, DNS, uninstall, repair, and cleanup actions still require controlled fixture/VM verification.
- Portable executable build and launch gate: **PASS on this PC**.
- Commercial clean-machine installer/uninstaller gate: **PENDING**. A clean Windows 11 VM, installer packaging, uninstall cleanup, code signing, and live Gumroad purchase verification are external release requirements.

WinCarePro is not labeled “100% certified” until the pending mutation and clean-VM gates are completed.
# Release Upgrade Status — 2026-07-28

## Implemented

- Secure update checks use an HTTPS manifest, SHA-256 verification, Authenticode validation, and an expected publisher subject.
- Checkout opens only a configured public HTTPS URL; Gumroad license activation remains fail-closed.
- Release tooling signs with SHA-256, timestamps, verifies the signature, and writes the update manifest.
- Windows Sandbox and clean-VM smoke scripts verify signature and launch behavior.
- A futuristic native WPF accessibility shell is implemented with named Windows controls, keyboard shortcuts, live status text, and high-contrast-friendly colors.
- Both the production PyInstaller executable and WPF project build successfully.
- CodeGraph is synchronized: 30 files, 831 nodes, 2,305 edges.

## Verified

- Python tests: 30 passed.
- Python compilation: passed.
- PyInstaller production build: passed.
- WPF Release build: passed with zero warnings.
- Bandit medium/high findings: zero.
- Production EXE SHA-256: `52C3D3F8985B6AA024FA11276B511264C0C2368DE0D717FD96E09BD2D652E214`.

## Open commercial gates

- **High:** `dist\WinCarePro.exe` is not signed because no code-signing certificate is installed.
- **High:** clean-VM certification is not recorded because Windows Sandbox is unavailable on this host.
- **High:** checkout and update URLs require the owner's live Gumroad/download configuration.
- **Medium:** WPF UI Automation testing is blocked on this PC by a Windows/.NET font-cache initializer crash (`System.UriFormatException`) before application code runs. The shell compiles on .NET 8 LTS and must be exercised in the clean VM.
- **Medium:** the WPF shell currently fronts the complete Python production engine; feature-by-feature native migration is not yet complete.

WinCarePro is not yet certified for commercial release. No “100%” claim is made until these gates have recorded evidence.
