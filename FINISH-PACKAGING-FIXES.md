# Packaging/release fixes — HOLD / NO_SHIP

## Outcome and scope

The concrete packaging defects in `FINISH-NATIVE-GATES.md` were corrected locally. **Production remains HOLD / NO_SHIP.** No real signing, private-key access, certificate creation/export/import, Windows Installer execution, MakeAppx/WiX execution, package download/install, git tag/commit/push, upload, or release occurred. All executable tools in the regression run were inert PowerShell functions; fake MSI/MSIX files and fake signature markers are deliberately not usable packages or signatures.

Existing source/test work and old artifacts were retained. Application/UI/service source was not changed by this task. New fixture files were confined to Hermes scratch. The reviewed portable was not rebuilt or relabeled. Packaging scripts now have deliberately incompatible, explicit candidate-only inputs; old no-argument build/sign workflows must not be reused.

## Exact changes

- `sign-release.ps1`: explicit `Targets`, `Stage` (`Payload` or `Package`), approved `SignToolPath`, and explicit `TimestampUrl`. All target extensions/reparse status are validated before any signing begins; mixed payload/package batches are rejected. Credentials must already be supplied, with no discovery or generation. SignTool signing **and verification** errors throw. Verification uses `/pa /all /tw /v`; all nonzero statuses, including warnings, fail. No implicit MSI-first/publish-directory target discovery and no skip-timestamp option remain.
- `build-installer.ps1`: consumes only explicit `PayloadDir` into a **new** `OutputDir`; no clean/build/test/publish, deletion of prior publish output, injected update-source URL, or automatic signing. Default mode verifies executable/library signatures before packaging. WiX source version and payload executable file version must match the project. The private staging snapshot and extracted MSI payload are checked by relative path/file count/SHA-256, and extracted executable/library signatures are verified. Native administrative extraction is retained only as a future disposable-environment operation; it was mocked here.
- `build-msix.ps1`: same explicit immutable-input/new-output contract. Project version supplies the fourth version component; absent/malformed/mismatched versions fail with **no 1.0 fallback**. Publisher, display name and package identity are explicit, not invented defaults. XML attributes are assigned as XML, not regular-expression substitutions. Reserved metadata collisions are refused. Staged/extracted file hashes, unexpected package entries and final identity are checked. Removed all certificate-store lookup, self-signed creation/export and automatic signing. No signature-file-presence/installability claim remains.
- `packaging/Packaging.Common.ps1` (new): common checked-tool exit handling, signature verification, explicit version validation, non-reparse payload inventory, SHA-256 snapshots, new-directory staging, and candidate receipts. Receipts are marked HOLD and record pre-signing outer package hashes, not production approval.
- `release-to-github.ps1`: unconditional terminating HOLD **before** builds, git, tags, pushes or uploads. There is no approval-token invention or bypass switch. Implementing a future publisher is deliberately blocked on real channel/signing/review decisions, rather than accepting a caller-supplied boolean as approval.
- `packaging/Test-Packaging.ps1` (new): executable inert regression fixtures; real project scripts are copied into a scratch fixture repository, with fake signing/packaging/extraction tools and forbidden-command tripwires. A small existing versioned apphost is copied as input but never launched. The original apphost and reviewed artifacts are never signed or modified.
- `README.md`, `RELEASE.md`, `packaging/release-github.md`: correct reviewed-candidate identity, current test evidence and explicit HOLD; withdraw unsigned-freeware publishing instructions and old implicit build/sign commands. `STORE.md` now prominently marks its retained historical instructions withdrawn and corrects the preserved MSIX signature/version/payload status.

`-UnsignedCandidate` permits an explicitly unsigned **local payload candidate** only. Both builders always leave the outer package unsigned. Neither mode can enable the disabled publisher. In the future approved sequence: publish/review once -> sign/verify payload -> package those exact bytes without republishing -> separately sign/verify outer package -> re-extract and independently verify final signed-package metadata, payload identity/signatures and final outer hash. The candidate receipt becomes stale after outer signing and must not be reused as release evidence.

## Executed tests and evidence

Working directory: `C:/Users/blunt/Desktop/wincare-desktop`.

```text
powershell.exe -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File C:/Users/blunt/Desktop/wincare-desktop/packaging/Test-Packaging.ps1 -ScratchRoot C:/Users/blunt/AppData/Local/hermes/cache/scratch
```

Observed test-first evidence:

1. Original signing script with inert signer returning success and verifier returning failure: printed NOT VALID for both selected targets, then the regression failed with `FAIL: signature verification failure was accepted.` (exit 1). Adding a terminating verification error made that regression pass.
2. Original release script with inert git tripwire: failed the HOLD assertion with `GIT REACHED` (exit 1). Replacing the unsafe publisher with an unconditional HOLD made the regression pass.
3. Explicit candidate-interface/payload fixtures before implementation: **2 passed, 21 failed** (exit 1), principally missing explicit `PayloadDir`/`OutputDir`/`SignToolPath` interfaces. Those contract failures are distinct from the two reproduced behavioral defects above.
4. Implemented pipeline regression run: **23 passed, 0 failed**, exit **0**. Exact returned final line:

```text
RESULT: 23 passed; 0 failed. Fixtures: C:\Users\blunt\AppData\Local\hermes\cache\scratch\packaging-4ae4a00e1f7944b38939cf0b7fae34de
```

Covered assertions:

- Signing verification failure; signing failure; missing targets; mixed-stage targets before partial signing; missing credentials.
- Publisher HOLD before any external command.
- MSI sign-before-package ordering and exact copied signed-payload bytes without publish; unsigned-payload rejection before packaging; pack failure; extraction failure; changed extracted payload; preservation of existing output.
- MSIX project-derived version and unchanged payload; unsigned-payload rejection; explicitly unsigned candidate without signing/certificate creation; changed extracted payload; incorrect identity; pack failure; unpack failure; missing publisher; mismatched version override; invalid version; missing project version with no fallback.

No fixture claims actual trust, timestamp validity, package format validity or native installability. Fake signatures/archives deliberately exercise control flow and byte-identity checks only. The real installed signing/packaging tools were never called. No real final signed package exists from this task.

The unchanged application source already has the separately executed **303 passed / 0 failed / 0 skipped** full suite and Release **0 warnings / 0 errors** documented in `FINISH-VALIDATION.md`. Those are attributed prior validation results, not a rerun by this packaging task. Packaging did not modify application/test inputs and no redundant application suite was launched.

## Preservation and final verification

The pre-edit artifact/publish/installer preservation snapshot is `C:/Users/blunt/AppData/Local/hermes/cache/scratch/wincare-packaging-evidence/preserved-before.json` (**68 files**). Final `verification.json` alongside it records **all 68 unchanged**, **181 unrelated prior-validation inputs unchanged**, **six PowerShell scripts parsed with zero syntax errors**, and scoped `git diff --check` exit **0**. Git emitted LF-to-CRLF normalization warnings only; no whitespace error was reported. The reviewed executable hash matched exactly. Fixture success above is the pipeline verification; this final pass checks preservation and source scope. Existing runtime artifacts are not promoted to fresh outputs.

Reviewed portable identity expected and retained:

```text
artifacts/rescue-reviewed-portable/WinCare Pro.exe
SHA256 3e082629060d9580b74e34fdfe996e105bc5dabcb64fec6ff940bb3026db1859
```

## Remaining decisions / not done

1. Choose and authorize production channel(s), signer/credential custody, timestamp endpoint, publisher/Store identity and approved runtime/toolchain. PFX/password is still a supported explicit interface, and `/p` appears in the child process argument list; whether to replace it with an approved signing service/token is a production decision. No credentials, endpoints or signatures were invented.
2. Complete the disposable native acceptance matrix in `FINISH-NATIVE-GATES.md`, plus final real package extraction, trust, installation/upgrade/repair/uninstall and interactive accessibility/DPI acceptance. Default MSI candidate verification performs administrative extraction and must not be run on this preserved host. An actual packaging-tool smoke run is still open.
3. Produce and independently review a new final signed candidate from an approved source snapshot/runtime. Reverify embedded payload after outer signing, bind final hashes/source identity to review, and only then design/review a fail-closed publisher. The unconditional HOLD intentionally does not implement a pretend approval system.
4. Regenerate stale winget version/URL/hash/per-user scope only from that final approved asset; settle channel-specific OS support and update-source policy. Existing MSI/MSIX and winget metadata remain non-release historical outputs.
5. No CI, network, Store submission, release authorization or production security approval is implied by these local fixture results.

Tooling issue encountered: full-file `write_file` replacement was conservatively refused as a partial-read overwrite; targeted/full-content `patch` was used after inspection instead. It did not alter the refused target. No implementation verification failures remained in the completed 23-fixture run.
