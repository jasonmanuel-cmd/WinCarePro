# Independent packaging review — HOLD / NO_SHIP

## Verdict and findings

**Bounded fixture pass, not approval of the complete packaging contract or production release.** Independently executed the supplied suite: **23 passed, 0 failed, exit 0**. Additional inert probes confirmed the defects below. No production scripts were fixed. Publication is unconditionally disabled; this is a safety interlock, **not completed release automation**.

No repository `REVIEW.md` was found. Blocking criteria for this review are the requested path safety and exact-payload verification, using `FINISH-PACKAGING-FIXES.md:11-19,40-47` and `RELEASE.md:14-24` as the claim source. Findings prevent an unqualified statement that those contracts are fully verified; they do not invalidate the observed 23 passing fixtures. Production independently remains NO_SHIP because native acceptance, real tool compatibility, signing and release authorization are outstanding.

### F1 — P2: relative OutputDir resolves against the wrong working directory

- **Confidence:** high; confirmed by an independently authored inert reproducer.
- **Path/lines:** `packaging/Packaging.Common.ps1:59-62,72-78` (both builders call this helper).
- `PayloadDir` is resolved using PowerShell's provider location, but `OutputDir` uses `[IO.Path]::GetFullPath`, which uses the process current directory. After `Set-Location`, the two can differ. Neither builder requires fully qualified paths.
- Trigger: start PowerShell in the review scratch directory, change location to the probe's `ps-location`, then initialize a candidate with a relative output name and a valid scratch payload. Expected output is below `ps-location`; actual output was a sibling under the process start directory. The initializer successfully created it there.
- Evidence: `probes-result.json` reports `relative_output_matches: false`, with both full paths. This can place candidate writes outside the caller's intended directory. Existing-output refusal still works; no existing data was overwritten by this probe.
- Suggested correction, not applied: resolve new filesystem paths through the PowerShell session's provider-path API, or explicitly require fully qualified filesystem paths. Add a regression where PowerShell and process locations differ. All existing fixture output paths are absolute (`Test-Packaging.ps1:90`), so they cannot detect this.

### F2 — P2: MSI identity check ignores extracted files outside the app subtree

- **Confidence:** high for the validation bypass, not a claim of an observed bad real MSI.
- **Path/lines:** `build-installer.ps1:29-37`.
- The script finds one `WinCare Pro.exe`, compares only its containing directory with the source snapshot, and verifies signatures only for those expected files. It never inventories the rest of the extraction tree.
- Independent inert extraction returned the exact expected app subtree plus `extracted/unreviewed.dll`. The builder completed and wrote `candidate.json`; `probes-result.json` reports `msi_extra_file_accepted: true`. The added file was inert text, never executed. The corresponding MSIX probe failed with `Unexpected package identity content: unreviewed.dll`.
- Thus MSI checks prove parity of the selected subtree, not absence of additional delivered payload elsewhere. A packaging regression adding another destination can escape this gate. Current `installer/WinCare.wxs:96-98` harvests the requested source into INSTALLFOLDER; this review did **not** find it currently adding the extra DLL. This finding concerns the new validator's incomplete contract.
- Suggested correction, not applied: reconcile the whole administrative image with explicit allowances for administrative-image metadata, or independently inspect MSI File/Directory tables and extracted contents. Do not blindly count the administrative MSI itself as unexpected payload. Add the outside-app-directory fixture; the existing corruption fixture changes only `native.dll` inside the chosen root (`Test-Packaging.ps1:47-57,143-146`).

## Independent execution and safety boundary

Before running anything, read all of `sign-release.ps1`, `build-installer.ps1`, `build-msix.ps1`, `release-to-github.ps1`, `packaging/Packaging.Common.ps1`, and `packaging/Test-Packaging.ps1`. Also read the change report, current README/release/Store/distribution docs, project version, both installer source manifests, native-gate report and prior validation/preservation records.

The supplied harness copies scripts and a versioned apphost to a unique scratch directory. The apphost is never launched. Explicit tools are PowerShell mock functions; `Start-Process` intercepts the only Windows Installer call. Git, gh, dotnet and certificate creation/export names are tripwired. Fake PFX content and signatures are inert fixture text; no real credentials are accessed. These are inspected control-flow guarantees for the current scripts, not an OS sandbox or a general interceptor for arbitrary absolute-path executables.

Fresh command, repository working directory:

```text
powershell.exe -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File C:/Users/blunt/Desktop/wincare-desktop/packaging/Test-Packaging.ps1 -ScratchRoot C:/Users/blunt/AppData/Local/hermes/cache/scratch
RESULT: 23 passed; 0 failed. Fixtures: C:\Users\blunt\AppData\Local\hermes\cache\scratch\packaging-3f5d8f33fea740a3a387f7baff78badd
Exit: 0
```

Independent probes used the real repository builder/helper code, new scratch payloads, and separately written inert tools. Their command ran with the evidence directory as the process working directory:

```text
powershell.exe -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File C:/Users/blunt/AppData/Local/hermes/cache/scratch/wincare-packaging-review/probes.ps1
Exit: 0
warning_rejected: true (mock verifier exit 2)
msi_extra_file_accepted: true
msix_extra_file_rejected: true
relative_output_matches: false
```

Probe exit 0 means the recorded observations were reproduced, **including defects**, not that the implementation passed those contracts. No real SignTool, WiX, MakeAppx, msiexec, git, installer, app launch, elevation, certificate/key access, installation, download of packages/tools, commit, push, upload or release was performed. Only this report and scratch evidence were created. A tool marked the probe command auto-approved under an existing rule; authorization for the inert scratch work came from this task, not from that automatic label.

## True tool compatibility — bounded assessment

Primary documentation was consulted read-only: Microsoft Learn's SignTool reference and MakeAppx packaging reference, FireGiant's wix.exe command reference, Microsoft Learn's msiexec reference and Path.GetFullPath API reference. These establish documented interfaces, not successful execution against the installed tool versions.

- SignTool's `/all`, `/pa` and `/tw` verification policy is coherent with documented multiple-signature verification and timestamp warnings. Exit 2 represents warnings; the common wrapper rejects it, independently exercised with an inert verifier. Signing explicitly selects SHA256 digest/timestamp algorithms. Actual timestamp, chain, revocation, PFX handling and native argument marshalling remain untested.
- MakeAppx `pack /d ... /p ... /v` and `unpack /p ... /d ...` match documented command forms. Microsoft explicitly warns that MakeAppx validation does not guarantee installability. The fixtures copy directories, not genuine archives; they do not test schema/image validation, native filename encoding, generated metadata or real extracted manifest bytes.
- WiX `build`, `-arch`, `-d`, `-o` are documented. Current general documentation spells the logo switch `--nologo`, while this script uses `-nologo`; compatibility of that spelling with the owner-approved version was not established here and is an **unresolved compatibility check**, not a confirmed defect. The mock does not parse or validate that switch, WiX XML, harvest semantics, package schema or native quoting. No real WiX invocation was authorized.
- `/a` is a real Windows Installer administrative installation, not a harmless ZIP reader. The docs correctly restrict this path to an approved disposable guest. The script itself has no VM-enforcement mechanism; do not invoke it on this host merely because `-UnsignedCandidate` is present.

Reference addresses (read-only documentation, not execution instructions):

```text
https://learn.microsoft.com/en-us/windows/win32/seccrypto/signtool
https://learn.microsoft.com/en-us/windows/msix/package/create-app-package-with-makeappx-tool
https://docs.firegiant.com/wix/tools/wixexe/
https://learn.microsoft.com/windows-server/administration/windows-commands/msiexec
https://learn.microsoft.com/en-us/dotnet/api/system.io.path.getfullpath
```

The direct extraction backend could not fetch pages; search retrieval returned primary documentation text instead. Failed/narrow queries were not treated as positive compatibility evidence. Selected search output is retained in scratch.

## Regression assertion review and remaining risks

**Checked and clean within inspected/observed bounds:** sign and verify failures throw; explicit target/stage checking precedes signing; no automatic credential generation; no implicit republish or update URL injection; existing-output refusal; project-derived version without fallback; staged/source hash preservation; MSIX extra-file rejection; candidate receipts say HOLD and record unsigned outer hashes; release script stops unconditionally before any tool. Docs distinguish preserved artifacts from new outputs and withdraw the historical Store workflow.

**Verification gaps (not additional ranked defects):**

- `packaging/Test-Packaging.ps1:71-74,174-177`: the wrong-identity mock changes the extracted manifest bytes; the broad `*identity*` assertion is satisfied by the earlier hash error. It does not directly prove `build-msix.ps1:49-50`'s semantic identity/architecture branch executes. Source inspection shows the branch exists.
- `packaging/Test-Packaging.ps1:94-204`: no fixture for warning exit 2 (covered independently here), native executable argument handling, package-stage signing success, absent password, MSI unsigned-candidate success, reserved MSIX metadata collisions, XML-special publisher text, missing package despite exit 0, version component limits, or MSIX existing-output preservation.
- `Packaging.Common.ps1:33-43,59-75` and `sign-release.ps1:19-25`: direct payload root/descendant reparse entries and direct signing-target reparse flags are checked, but ancestor junctions, output ancestors, hard-link aliasing and concurrent path replacement are not a handle-based safety boundary. No adversarial filesystem-race acceptance was performed. Use isolated trusted inputs/ancestors; no claim of malicious-filesystem resilience.
- `build-installer.ps1:15-17,29-37`: source WiX version is checked, but final MSI ProductVersion/scope/upgrade metadata is not independently queried here. `RELEASE.md:23-24` correctly leaves final signed-package metadata/attestation and release binding to a later authorized gate.
- `STORE.md:11-168` retains obsolete instructions and claims, but the new opening explicitly withdraws them. They were not reapproved; the historical comment claims in `installer/AppXManifest.xml` likewise are not current trust/channel evidence.

## Preservation, evidence identity and spec axis

Evidence root: `C:/Users/blunt/AppData/Local/hermes/cache/scratch/wincare-packaging-review/`.

- `review-before.json`: fresh content-hash baseline for selected source/docs inputs and all files in the author's 68-file preservation manifest. `initial-result.json`: independent comparison showed **68 checked, zero mismatches** before fixture execution; also records the actual command, exit and fixture result.
- `fixtures.stdout.txt`, `fixtures.stderr.txt`: captured fresh original suite output. `probes.ps1` and `probes-result.json`: independently authored reproducer and actual observations. `run_review.py`: fresh suite runner/baseline collector.
- `final-verification.json`: final independent hash comparisons, syntax-parser output and source-scope checks. This is the final authority for observed preservation, rather than simply accepting the author's `verification.json`.
- Reviewed EXE freshly measured SHA256: **`3e082629060d9580b74e34fdfe996e105bc5dabcb64fec6ff940bb3026db1859`**, exactly the requested identity. No rebuilding or signing was performed. Its unsigned status is attributed prior native inspection plus byte preservation, not a new trust-tool run.

**spec_axis: assessed.** Claims that implicit republishing, automatic credentials, nonterminating sign verification, version fallback and unsafe publication were removed are met within source inspection/inert execution. Exact MSI-wide extraction identity and consistent explicit-output path semantics are not fully met (F1/F2). The release script satisfies unconditional disabling only; approved production publishing, immutable release-source binding and final signed-asset attestation remain unimplemented by design. The prior **303 application tests and zero-warning Release build** are attributed to `FINISH-VALIDATION.md`; neither was rerun or substituted for native packaging evidence.

**could_not_assess:** real packaging/signing compatibility, trust/timestamps, package installation/upgrade/repair/uninstall, native application actions, interactive UI/accessibility, approved channel/signer/runtime and final signed artifact/source binding. They require the separately authorized disposable environment and owner decisions; none is discharged by these fixtures.

This is independent of the packaging author. Separate independent validation of this reviewer's new findings was unavailable; the concrete reproducer and code trace are provided for another reader. No source remediation or production approval is implied.
