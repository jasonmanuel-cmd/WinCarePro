# Release checklist — HOLD / NO_SHIP

Production publication is **disabled**. `release-to-github.ps1 -Version 2.0.0` throws before any build, git, signing or network command. There is no bypass flag. A green local test or a candidate receipt is not a release approval.

## Current evidence and candidate

- Current reviewed portable: `artifacts/rescue-reviewed-portable/WinCare Pro.exe`, SHA-256 `3e082629060d9580b74e34fdfe996e105bc5dabcb64fec6ff940bb3026db1859`; preserve native sidecars. It is unsigned. Earlier `publish/`, MSI and MSIX are different/unreviewed candidates, not release assets.
- `FINISH-VALIDATION.md`: November 24, 2026 full suite **303 passed, 0 failed, 0 skipped**, followed by Release build **0 warnings, 0 errors**. This is existing fresh validation evidence, not a fresh publish or native acceptance.
- `FINISH-PACKAGING-FIXES.md`: **23 inert packaging/signing/release fixtures passed**. No actual signatures, installer execution, Store submission or publication were exercised.
- `FINISH-NATIVE-GATES.md`: existing MSI unsigned; existing MSIX has a signature but fails trust verification and carries old version/payload. Do not redistribute either. Neither signature-file presence nor package construction proves installability.

## Candidate pipeline (separately authorized execution only)

1. Freeze and review the full source snapshot, including tracked changes and untracked inputs; a clean `git diff` alone is insufficient. Select an approved runtime and toolchain. Publish once into a **new** candidate directory, then review/test that candidate. Do not overwrite the preserved reviewed artifact.
2. If an update-source file is desired, approve its endpoint and add it **before** payload review/hashing. Packaging no longer injects a default endpoint or changes payload files.
3. With separately approved credentials and timestamp endpoint, sign the candidate's executable payload using `sign-release.ps1 -Stage Payload -Targets <explicit-file-list> -SignToolPath <approved-tool> -TimestampUrl <approved-endpoint>`. Third-party signed libraries must also pass verification; do not silently replace their signatures. Credentials come only from `WINCARE_SIGN_PFX` / `WINCARE_SIGN_PWD`. The current PFX interface exposes the password in the child process argument list; production signer/service and isolation policy remain an owner decision. No credential is supplied or created by this work.
4. Package **that same payload**, without another publish:
   - MSI: `build-installer.ps1 -PayloadDir <candidate> -OutputDir <new-directory> -WixPath <approved-tool> -SignToolPath <approved-verifier>`.
   - MSIX: `build-msix.ps1 -PayloadDir <candidate> -OutputDir <new-directory> -MakeAppxPath <approved-tool> -SignToolPath <approved-verifier> -Publisher <approved-subject> -PublisherDisplayName <approved-name> -IdentityName <approved-identity>`.
   These commands are interface descriptions, not authorization to run them on this host. MSI administrative extraction uses Windows Installer and belongs in an approved disposable guest. Neither builder restores packages, rebuilds, republishes, signs, creates certificates, or overwrites an existing output directory.
5. The project must contain one explicit three-part numeric Version. MSIX derives its fourth part as zero; an optional `-Version` must match exactly. Both builders check the executable's file version; MSI also checks WiX source version. There is **no 1.0 fallback**. Current project version is 2.0.0 / package version 2.0.0.0.
6. Builders verify copied/extracted payload relative paths and SHA-256 hashes, check signatures by default, and produce `candidate.json` marked **HOLD / NO_SHIP**. The outer MSI/MSIX is still unsigned. `-UnsignedCandidate` explicitly permits unsigned payloads for local verification only; it cannot enable publishing and is not a Store approval mode.
7. After separate authorization, sign the outer package with `sign-release.ps1 -Stage Package -Targets <explicit-package-list> ...`. Signing or verification errors/warnings fail the script. Re-extract the final signed package and independently recheck payload hashes/signatures and metadata, then record the final outer hash; the earlier candidate receipt predates signing and becomes stale. Native installation, upgrade/downgrade, repair/uninstall and trust tests remain required. No automated final signed-package release attestation is implemented here.
8. An owner-approved release change must replace the unconditional HOLD only after all acceptance evidence and channel decisions exist. It must bind approval to the exact source, final signed artifacts and hashes, refuse dirty/untracked source drift, check every subprocess exit, and revalidate immediately before any tag/upload. Do not restore the old build-and-publish shortcut.

## Decisions and native gates still open

- Disposable Windows guest identity/reset baseline and the full matrix in `FINISH-NATIVE-GATES.md`: Defender, signed VLC, cleanup/recycle, permissions, privacy/DNS/startup, scheduler, uninstall and interactive accessibility/DPI/consent coverage.
- Approved production signer/credential custody, trusted identity, timestamp service, and channel: direct MSI/portable, Store MSIX, or both. No self-signed production credential or assumed Store reservation.
- Approved runtime/dependency security review and packaging toolchain. Cached preview tools are availability evidence only.
- Supported OS claims by channel. Current MSIX manifest minimum is `10.0.19041.0`; do not inherit older 17763 claims as tested support. MSI/WPF supported-build acceptance remains to be established.
- Final signed package install/upgrade/uninstall and payload parity. A passing fixture is not a Windows Installer or MakeAppx acceptance result.
- Regenerate winget version/URL/hash/scope from the final approved asset. Existing winget metadata is stale; WiX declares `perUser`, not machine. Do not publish those manifests unchanged.
- Publisher/support identity, licensing/privacy/legal review, update endpoint policy, CI and authorized distribution/release process. No pricing, reputation guarantee or purchase recommendation is implied.

Keep historical reports unchanged. `FINISH-PACKAGING-FIXES.md` supersedes the script-defect descriptions in the earlier audit only where backed by its fixture evidence; it does not clear native or production gates.
