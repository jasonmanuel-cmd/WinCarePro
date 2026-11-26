# Packaging follow-up — HOLD / NO_SHIP

## Outcome

Both independently reproduced P2 defects in `FINISH-PACKAGING-REVIEW.md` are fixed locally and covered by regressions observed failing before their respective fixes. The final inert packaging suite reports **44 passed, 0 failed, exit 0**. This is control-flow and fixture byte-identity evidence, not real packaging, signing, installation, native accessibility or production approval. The publisher remains intentionally disabled, not completed.

Read the review, prior `FINISH-PACKAGING-FIXES.md`, all four entry scripts, common helper and complete fixture harness before execution. Existing dirty application work and historical artifacts were preserved. No resets, commits or git network operations occurred.

## Source changes

- `packaging/Packaging.Common.ps1`: replace process-current-directory `Path.GetFullPath(OutputDir)` with PowerShell session `GetUnresolvedProviderPathFromPSPath`, explicitly requiring the FileSystem provider. Relative new output paths now follow the same PowerShell location semantics as input paths. Existing-output and inside-payload rejection remain in place.
- `build-installer.ps1`: inventory the **entire administrative extraction tree** using the existing non-reparse inventory before selecting the unique executable root. Every file must belong to the expected payload at that root, except exactly root-level `WinCare.msi`, the administrative-image database. No blanket `.msi` extension, sibling-directory or prefix-similarity exception exists. Existing subtree file-count/hash checks, staged/source checks and extracted signature checks remain. Root discovery now uses the checked inventory instead of a separate recursive enumeration.
- `packaging/Test-Packaging.ps1`: add location divergence and five outside-subtree regressions, realistic inert administrative-database metadata, and bounded assertion coverage described below. The fixture database intentionally differs from the outer mock MSI; administrative MSI bytes are not expected to be identical to the original package.
- This report is the only new repository file. `build-msix.ps1`, signing and disabled publisher scripts are unchanged by this follow-up.

The administrative-database exception is a narrow layout allowance, **not validation of that database's tables or behavior**. Real MSI metadata and administrative-image behavior remain gated on the approved disposable environment. The inventory detects unexpected files, not empty directory semantics. Trusted isolated ancestors remain required; no filesystem race/hard-link/ancestor-junction security claim is made.

## Actual test-first execution

Every suite invocation used this exact underlying command from `C:/Users/blunt/Desktop/wincare-desktop`:

```text
powershell.exe -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File C:/Users/blunt/Desktop/wincare-desktop/packaging/Test-Packaging.ps1 -ScratchRoot C:/Users/blunt/AppData/Local/hermes/cache/scratch
```

The scratch `run.py` wrapper captured stdout/stderr and propagated each exit code. Evidence directory:

```text
C:/Users/blunt/AppData/Local/hermes/cache/scratch/wincare-packaging-followup/
```

| Evidence prefix | Observation | Exit |
| --- | --- | --- |
| `red-f1` | 23 passed / 1 failed: relative output created under the process location, not the distinct PowerShell location | 1 |
| `green-f1` | 24 passed / 0 failed after session-provider resolution fix | 0 |
| `red-f2` | 24 passed / 5 failed: all five outside-subtree files wrongly accepted before inventory fix | 1 |
| `green-f2` | 29 passed / 0 failed after whole-image inventory fix | 0 |
| `final-suite` | 44 passed / 0 failed after bounded assertion expansion | 0 |

Each prefix has `.stdout.txt` and `.stderr.txt`; stderr was empty. Exact final result:

```text
RESULT: 44 passed; 0 failed. Fixtures: C:\Users\blunt\AppData\Local\hermes\cache\scratch\packaging-e94e2b5446d542fb9f53dd8c119d835c
```

New/strengthened coverage:

1. PowerShell and process working directories deliberately differ, both within scratch; relative candidate output must resolve beneath PowerShell's location and must not create the process-location alternative. Both locations are restored afterward.
2. Reject extracted `unreviewed.dll`, `Other\nested.dll`, `WinCare Pro-other\native.dll`, `Other\WinCare.msi`, and `unexpected.msi`; each rejection must occur without a candidate receipt. Existing success paths now include the allowed root administrative database.
3. Separate extracted-manifest byte mismatch (exact `Payload identity mismatch: AppxManifest.xml` assertion) from **semantic architecture** rejection. The semantic fixture edits only the scratch source manifest to x86 before staging; staged/extracted manifest hashes must match and the exact `MSIX identity verification failed.` branch must fire without a receipt. It does not pretend that each individual identity-field comparison has an independent branch test.
4. Signature verifier warning exit 2 fails closed; missing password refuses before any signer call; package-stage success signs and verifies the two explicit inert MSI/MSIX files in order.
5. MSI explicit unsigned candidate succeeds without any signing-tool call, permits administrative metadata, and records HOLD, unsigned-payload status and the actual outer mock-package hash.
6. MSI and MSIX exit-zero/no-package cases fail before extraction.
7. MSIX existing-output sentinel remains untouched and no tool is called.
8. All five reserved MSIX paths are refused before pack: Assets, AppxManifest.xml, AppxSignature.p7x, AppxBlockMap.xml and literal [Content_Types].xml.
9. XML-special publisher/display text round-trips unchanged; project version component 65536 is refused.

The existing 23 tests remain represented (the previously broad incorrect-identity test now truthfully names and asserts manifest byte mismatch). Existing signing, extraction failure, corruption, version, output preservation and unconditional publication HOLD tests still pass. No real executable/signature/package validity is inferred from inert markers and directory copies. Native executable argument marshalling remains untested.

## Installed WiX help — compatibility remains unverified

Read-only command discovery located `C:/Users/blunt/.dotnet/tools/wix.exe`. Only help-option invocations were made; no build command, input source, package or output path was supplied.

- `wix.exe --help -nologo` displayed help identifying **WiX 5.0.2+aa65968c419420d32e3e1b647aea0082f5ca5b78**, listed `--nologo`, and returned exit 127. This does **not** prove the trailing single-dash option was parsed.
- `wix.exe -nologo --help` returned exit 1 / WIX0118: additional argument `-nologo` unexpected.
- Control `wix.exe --nologo --help` also returned exit 1 / WIX0118. Thus these top-level option-order probes do not establish build-subcommand compatibility or isolate the single-dash spelling as a defect.

The two captured control results are in `wix-help.json`, produced by `check_help.py`. **Build-mode `-nologo` compatibility remains explicitly unverified**, and the production invocation was not changed speculatively. Tool help is not packaging smoke-test evidence. No real WiX build, MakeAppx, SignTool or Windows Installer operation was run.

## Preservation and final verification

`before.json` records a fresh SHA-256 baseline of **1,747 repository files excluding .git**, including existing dirty source and artifacts. `verification.json`, produced by `verify.py` after the final suite, records:

- **1,744 baseline files unchanged**; exactly the three source/test files listed above changed; zero baseline files missing. At that verification point no new repository files existed; this report was subsequently created with tool-confirmed write verification.
- All six PowerShell entry/helper/test scripts parsed successfully: zero parser errors, exit 0.
- Scoped `git diff --check` exit 0; only the existing LF-to-CRLF normalization warning for build-installer.ps1. Untracked helpers/tests are covered by parsing and the content baseline, not represented as tracked diff entries.
- Reviewed portable freshly hashed to the required identity:

```text
artifacts/rescue-reviewed-portable/WinCare Pro.exe
SHA256 3e082629060d9580b74e34fdfe996e105bc5dabcb64fec6ff940bb3026db1859
```

The initial baseline command saved the full manifest successfully, then its display-only portable lookup raised KeyError because Windows manifest keys use backslashes. Final verification read the actual portable path and matched the required hash. No baseline data was lost or replaced. No failed fix/verify cycles occurred; the intentional pre-fix red runs above are regression evidence, not implementation failures.

All fixture writes were under Hermes scratch. Mock functions intercepted tool calls; the existing versioned apphost was copied but never launched. Fake PFX text was only an inert existence fixture, not a real credential. No signing/private-key/certificate operation, real packaging/MSI extraction/install, elevation, registry/host-setting change, download, Defender scan, cleanup, app closure, release or upload occurred. Prior 303 application tests/build results remain attributed historical evidence, not rerun here.

## Remaining gates

- Authorized disposable-VM real packaging and argument/tool compatibility, MSI database metadata, complete final signed payload/outer identity, trust/chain/timestamps, installation/upgrade/repair/uninstall.
- Native actions and interactive accessibility/DPI acceptance.
- Owner-approved signer, credential custody, timestamp endpoint, runtime/toolchain, publisher/Store identity, channels and final source/artifact binding.
- A separately designed and reviewed fail-closed publisher; the current unconditional HOLD is intentional, not successful release automation.

Reusable bounded workflow: inspect scripts and interceptors; snapshot existing bytes; demonstrate each new regression failing before its minimal fix; rerun affected/full inert fixtures; capture exact outputs; hash-check untouched files; parse scripts; report native gates separately. This workflow was recorded here rather than modifying profile skills outside the authorized local script/test/doc scope.

**No production release claim.**
