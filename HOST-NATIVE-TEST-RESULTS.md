# Bounded native host validation — November 24, 2026

## Outcome

**PASS for the authorized unsigned MSI build/admin-extraction and isolated real cleanup checks. Overall HOLD / NO_SHIP remains.** No MSI installation was performed. No product source, production safety boundary, or prior artifact was changed.

- Real WiX MSI build succeeded; real Windows Installer administrative extraction returned 0.
- All **7** extracted payload files match the reviewed portable byte-for-byte. The entire administrative image contains those files plus exactly its root administrative MSI database.
- Freshly compiled, unchanged `SystemOptimizerService` performed real Windows file deletion against an explicitly injected disposable root: **18 bytes removed, 11 assertions passed**, with exactly 3 expected refusals (changed, locked and outside-root files).
- Final verification at **2026-11-24T22:08:05.835583-08:00**: **1,749 pre-existing repository files unchanged; zero missing; zero new** before this report was written. All 184 copied source/support files remained identical to their repository counterparts.
- Prior 303 application tests and 44 inert packaging tests were not rerun. No application/packaging source changed; the focused fresh native checks and clean scratch build were the relevant additional evidence. Historical suite counts are not fresh results here.

## Authority and boundaries

The user's authorization to test the spare desktop, and this task's explicit permission for scratch-only administrative extraction, supersede the earlier prohibition for that operation. This does **not** establish backup existence or authorize MSI installation. Recovery remains unverified.

Read `HOST-TEST-PREFLIGHT.md`, `FINISH-PACKAGING-FOLLOWUP.md`, `FINISH-NATIVE-GATES.md`, `GEMINI.md`, current `build-installer.ps1`, all of `Packaging.Common.ps1`, all of `installer/WinCare.wxs`, project files, cleanup implementation/constructor/logging, `PathSafety`, `HistoryStore`, `SafetyMode`, and the existing cleanup-boundary test before the relevant operations. Historical packaging warnings were evaluated against current source, not treated as current code.

All new execution files, fixture inputs, outputs and child-process TEMP/TMP/TMPDIR were confined to:

```text
C:/Users/blunt/AppData/Local/hermes/cache/scratch/wincare-native-363d0f35aa5d40dc98120761b055936b
```

Below `$S` means that exact directory; `$R` means `C:/Users/blunt/Desktop/wincare-desktop`. A new scratch bootstrap `C:/Users/blunt/AppData/Local/hermes/cache/scratch/wincare-native-run.py` created the unique root and baseline. The requested report is the only repository addition.

No elevation/UAC launch, signing/key/certificate operation, downloads/tool installations, reboot, DNS/privacy/startup/security change, app uninstall, live Defender scan, arbitrary process closure, git reset/commit/push, or release was performed. PowerShell ExecutionPolicy Bypass was process-local. Existing installers and publish output were not used. No broad temp scan or existing user-data cleanup was run. Windows Installer may maintain its own service/event bookkeeping; this report does not claim exhaustive host-wide write monitoring.

## MSI review before extraction

WiX source contains no custom actions or extensions. Its normal-install Registry, Shortcut, MajorUpgrade and removal authoring was identified and deliberately **not exercised**. Payload input is the complete reviewed portable copied into a new scratch directory, with the required executable hash checked before build.

The build and extraction were split to inspect the actual resulting database **before** executing Windows Installer. The wrapper uses the same production helper/build arguments; `extract.ps1` executes unchanged production lines 24–54 for extraction, whole-image inventory, source/stage/extracted hash checks and receipt creation. This is not a claim that the whole production entry script ran uninterrupted.

Read-only MSI database inspection (`MsiOpenDatabaseW` with null read-only persistence) found:

- `CustomAction`: absent/zero rows.
- `AdminExecuteSequence`: CostInitialize, FileCost, CostFinalize, InstallValidate, InstallInitialize, InstallAdminPackage, InstallFiles, InstallFinalize only.
- Admin UI sequence: costing plus ExecuteAction; quiet extraction was used.
- Directory rows are relative descendants of TARGETDIR (`LocalApp`, `PMenu`, `WinCare Pro`); no absolute destination override or custom redirection was present.
- ProductName `WinCare Pro`, ProductVersion `2.0.0`, ProductCode `{66CFF114-90ED-4A21-B432-87ABED1DE127}`; seven File rows.

`msi-tables.json` retains all inspected rows, including normal-install Registry/Shortcut/InstallExecuteSequence tables. The actual extraction log lists only ADMIN and the eight expected administrative actions. It does not list WriteRegistryValues, RegisterProduct, PublishProduct, RemoveExistingProducts or CreateShortcuts.

Before/after exact-target readback (`host-before.json`, `host-after.json`) agrees: `MsiQueryProductStateW` = **-1** for the new ProductCode, and `HKCU\Software\WinCare\WinCare Pro` absent. The new product was not registered by this operation. No UAC/elevation requirement was encountered.

## Exact commands and observed results

All commands were executed through `$S/run.py`, which captures argv, cwd, start/end timestamps, individual exit, stdout and stderr to `<name>.json`, `<name>.stdout.txt`, and `<name>.stderr.txt`. Its Python subprocess working directory is `$S`; timeout is 540 seconds. Commands below use the defined path substitutions, not unspecified default artifacts.

### Build and inspection

```powershell
powershell.exe -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "$S/build.ps1"
```

Inside `build.ps1`, current `Resolve-PackageVersion` and `Initialize-PackageCandidate` create the unsigned candidate. Exact native build argv:

```powershell
& 'C:/Users/blunt/.dotnet/tools/wix.exe' build -arch x64 -d "SourceDir=$S/msi candidate/payload" "$R/installer/WinCare.wxs" -o "$S/msi candidate/WinCare.msi" -nologo
```

**WiX returned 0**, demonstrated by `Invoke-CheckedTool` continuing to database inspection. The first containing wrapper exited 1 afterward because its scratch COM record inspector could not access `FieldCount`; this was a harness failure, not a successful entire wrapper or MSI build failure. No extraction had occurred. The MSI was retained, not rebuilt or replaced. Read-only inspection was then completed successfully using existing Python ctypes and Windows MSI APIs:

```text
python "$S/inspect_msi.py"
```

Evidence `inspect.json`: exit **0**, complete database rows. No source fix was warranted.

### Administrative extraction

```text
python "$S/host_state.py" before
powershell.exe -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "$S/extract.ps1"
python "$S/host_state.py" after
```

Underlying production invocation:

```text
msiexec /a "$S/msi candidate/WinCare.msi" /qn TARGETDIR="$S/msi candidate/extracted" /l*v "$S/msi candidate/extract.log"
```

`extract.json`: exit **0**, HOLD candidate receipt emitted only after current production checks passed. The administrative image app root is `$S/msi candidate/extracted/LocalApp/WinCare Pro`. No `/i`, repair, upgrade or uninstall command was issued.

### Fresh-source cleanup harness build and execution

`copy_source.py` copied source/support files into `$S/source copy`, excluding original bin/artifacts/publish/installers and compilation intermediates. Only selected cached restore assets were initially copied. A first `--no-restore` build failed because the scratch WPF build lacked `obj/WinCareDesktop.csproj.nuget.dgspec.json` (MSB3030); origin: introduced scratch setup, not product source. It was replaced by a fresh **offline** restore/build against an empty local source, reusing installed package caches without acquisition:

```text
dotnet build "$S/source copy/WinCareDesktop.csproj" --no-restore -c Release -p:NuGetAudit=false
dotnet build "$S/source copy/WinCareDesktop.csproj" -c Release --source "$S/empty-feed" -p:NuGetAudit=false
dotnet build "$S/cleanup-harness/Cleanup.csproj" -c Release --source "$S/empty-feed" -p:NuGetAudit=false
dotnet "$S/cleanup-harness/bin/Release/net8.0-windows/Cleanup.dll" "$S"
```

First command exit **1** as above. Subsequent commands each exit **0**; both successful builds report **0 warnings, 0 errors**. Logs: `source-build`, `offline-source-build`, `cleanup-build`, `cleanup`.

`CleanupRoots` is the production explicit injection point. Before preview, the harness assigns only `$S/cleanup-fixtures/approved`; `HistoryStore` receives `$S/cleanup-fixtures/history.json`. Safe mode is enabled to prevent unrelated system mutations; explicit cleanup snapshots still run the unchanged actual `File.Delete` path. No production guard was edited, bypassed with reflection, or replaced with a mock. The harness does not launch the UI or the reviewed single-file EXE. It tests a fresh assembly compiled from identical current source, not binary equivalence to the reviewed portable.

## Cleanup action journal

All files below were newly created by this harness. No pre-existing fixture directory is accepted.

1. Create approved files `delete-a` (11 bytes), nested `delete-b` (7), `changed` (13), `locked` (17); create sibling `outside-root` (19).
2. Confirm explicit root identity; read-only preview returns exactly four root entries (including nested-directory snapshot).
3. Call cleanup without confirmed candidates: returns 0 and all four original files remain.
4. Append to `changed` after preview; create nested `added-after-preview` (23 bytes).
5. Add an explicit outside-root candidate and duplicate the valid `delete-a` candidate.
6. Hold `locked` with Windows FileShare.None; invoke real cleanup against the confirmed candidates.
7. Observe exactly `delete-a` and `delete-b` removed; returned byte count **18**, duplicate not counted twice. Changed, locked and outside-root targets refused with exactly **3** reported errors. Newly added file preserved. Nested directory retained. No history file written.
8. Dispose only the harness's own lock. Retain all surviving fixture files and logs for inspection; no recursive cleanup afterward.

`cleanup-results.json` contains **11 passed assertions**, exact service assembly path, fixture paths and the production log messages. Final readback verifies the two deletions and the exact four remaining fixture files. This does not cover recycle-bin COM, ACL denial, reparse/TOCTOU races, multiple volumes, broad maintenance, scheduler, cancellation, UI consent, or any other native system action.

## Hashes and final verification

```text
MSI: $S/msi candidate/WinCare.msi
Bytes: 60735488
SHA256: aac88f6cdfde7cbe4784f683cb9ec488d3050db4c62d2245c75e4e64af897d8a

Administrative database: $S/msi candidate/extracted/WinCare.msi
SHA256: a922fba1e83aa862b627d1271e7af3461d33f7c35caadc80b3cf9345fd9a3b05

Reviewed, input, staged and extracted WinCare Pro.exe
SHA256: 3e082629060d9580b74e34fdfe996e105bc5dabcb64fec6ff940bb3026db1859

Fresh cleanup service assembly: $S/cleanup-harness/bin/Release/net8.0-windows/WinCare Pro.dll
SHA256: f58af022718a58ebd2ec977dad22b93c7617c9f0fcab096fd3794187236586d1
```

`verification.json` contains all seven payload hashes, file counts, MSI actions, source identity and baseline comparisons. Receipt SHA256 matches independent hashing; it explicitly remains unsigned candidate / HOLD. Administrative database bytes legitimately differ from the outer MSI.

```text
python "$S/verify.py"
```

First verification stopped on the scratch verifier comparing Windows backslash paths with slash literals; origin: introduced harness assertion. Corrected only its path serialization to `.as_posix()`. `verification.json` / `verification-final.stdout.txt` record the one successful full rerun, exit **0**. This was the only final-verification correction cycle. No product changes were required. Verification was stopped after passing; this report was then created using the file tool's confirmed write.

Reusable procedure: baseline bytes; use a unique scratch root and process-local temp; package only a verified copied payload; split build from extraction; inspect actual MSI custom actions/sequences and directories read-only; allow only the approved administrative operation; execute explicit-root cleanup with injected history and a fresh-source harness; preserve refusal cases and individual child exits; verify payload, fixture and repository state separately. Windows COM automation proved unsuitable for this inspector; typed `msi.dll` read-only calls worked. Offline WPF scratch builds need complete restore metadata, best regenerated against an empty local source with audit disabled rather than partially copying assets.

## Still open — not conflated with these passes

Actual MSI install/repair/upgrade/downgrade/uninstall and installed launch; MSIX lifecycle/trust; approved signing credentials and signed payload/package chain/timestamp verification; accessibility/interactive UI; other native actions; supported OS/runtime matrix; final source-to-distributed-binary binding; owner-approved distribution and working release automation. No installation or final release approval is inferred. Backup/recovery capability remains unknown. These are remaining tests, not reasons to deny the bounded successes above.
