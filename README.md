# WinCare Pro

A system health dashboard for Windows 11. WPF, .NET 8, no installer required
to try it.

## Quick start

```powershell
.\build-release.ps1          # build, test, and publish a single self-contained exe
.\install.ps1                # install for the current user
.\install.ps1 -AutoStart     # ...and start with Windows
.\install.ps1 -Uninstall     # remove
```

`run_test.bat` launches the Release build, building it first if needed.

## What it does

| Panel | What it reports |
|---|---|
| Health score | 0–100, weighted: RAM 55%, disk 30%, privacy shields 10%, reclaimed space 5% |
| RAM | Live card, refreshed every 5s. Opens a menu: per-process working-set trim, standby flush, DNS flush |
| Storage | Live card. Opens a menu: temp preview, Recycle Bin, Windows Update cache, DISM cleanup |
| Power | Live card. Opens a menu: battery state, and switching between the three stock power plans |
| Maintenance Preview | Lists exactly what a cleanup would delete, with sizes, before you commit |
| System Check | Plain-language findings from this PC's real numbers |
| Diagnostics | CPU load, uptime, disk SMART health, top 5 memory users |
| Privacy Shield | 4 registry-backed toggles for telemetry |
| Removable Apps | Curated blocklist only — never guesses which apps you use |
| Uninstall Programs | Every program with a registered uninstaller, searchable, sorted by size |
| RAM & Network | Standby memory flush, DNS switching |
| History & Undo | Persisted to disk across restarts |

Every card on the dashboard is a menu, not a summary. Clicking a card opens
tools specific to what that card measures.

## Important behaviour

**Deleting files cannot be undone.** A maintenance run removes temp files for
good, so it is recorded as `DisplayOnly` in history and its button reads
"Dismiss", not "Revert". Only app uninstalls and privacy toggles are genuinely
reversible, and those perform real work on revert.

**Cleanup only ever deletes *inside* temp roots.** It enumerates each child of
`%TEMP%`, `%LOCALAPPDATA%\Temp` and `%PROGRAMDATA%\Temp` and deletes those. The
roots themselves are never touched — an earlier version deleted `%TEMP%`
outright, which raced every installer and updater on the machine.

**Uninstalling runs the vendor's own uninstaller.** The Uninstall Programs card
reads the standard uninstall registry keys and launches whatever uninstall
string the vendor registered, preferring a silent one when it exists. WinCare
never deletes a program's install folder: doing that leaves registry keys,
services, scheduled tasks and file associations behind, and breaks uninstall,
repair and upgrade. Every uninstall shows you the exact command before it runs,
and cannot be reverted — the record in history is honest about that.

**Recycle Bin, Update cache and DISM are destructive.** Each is behind a second
confirmation that only appears after you press "Review…", so a stray click
cannot remove anything. DISM component cleanup genuinely cannot be
interrupted and takes several minutes.

**"Free RAM" is largely theatre on a modern Windows.** Windows manages physical
pages itself, and anything that appears to manufacture free memory is not
telling you the truth. The RAM card therefore leads with per-process detail —
find the one program holding too much — and offers working-set trimming, which
asks a program to give up memory it holds but is not using. That can help when
something is paging; it does not add memory to the machine. The card reports
standby-list size rather than "available" memory, because standby is the
number that actually responds to a flush.

**Some actions need Administrator.** DNS switching and some uninstalls require
elevation. The header has a button to relaunch elevated. RAM, disk, temp cleanup
and privacy toggles all work without it.

## Layout

```
WinCareDesktop.csproj
App.xaml.cs                 global exception handler
Themes/Theme.xaml           palette, typography, control styles (single source of truth)
MainWindow.xaml             window shell: header, main column, sidebar, overlay
MainWindow.xaml.cs          per-region builders that populate the XAML shell
MainWindow.Tools.cs         per-card tool menus: RAM, Storage, Power, Uninstall
Models/Models.cs            POCOs; OptimizationTransaction carries its UndoKind
Services/
  SystemOptimizerService.cs   all WMI, registry, process and DNS work
  HistoryStore.cs             atomic JSON persistence
  InstalledProgramsService.cs uninstall-registry enumeration, command building
  StorageCleanupService.cs    Recycle Bin, Update cache, DISM, largest folders
  PowerService.cs             stock power plans (enumeration and switching)
ViewModels/MainViewModel.cs  state, commands, scoring
WinCareDesktop.Tests/        89 xunit tests
build-release.ps1            clean → build → test → publish
install.ps1                  per-user install / uninstall
```

## Styling

All colours, fonts and reusable control styles live in `Themes/Theme.xaml` as
resource keys. Nothing in code-behind hard-codes an RGB value, and a test
(`Every_foreground_comes_from_a_theme_resource`) enforces that. To add a dark
theme, override the palette keys — no C# changes needed.

## Tests

```powershell
dotnet test WinCareDesktop.Tests                    # all 89
dotnet test WinCareDesktop.Tests --filter Category=Ui   # UI only
```

Two layers:

- **Service** — history round-tripping and corrupt-file recovery, scoring
  bounds (including a regression test for the old flat-50 floor), the
  non-destructive guarantee of the temp scan, DNS notification, and the
  process runner's timeout/deadlock behaviour.
- **Against real system state** — the uninstaller and power services are tested
  against this PC's actual registry and power configuration. It found 33 real
  programs with correct sizes and publishers, and it caught that *no* power plan
  was ever marked active: `Guid.ToString("B")` wraps the value in braces and
  upper-cases it, so every comparison against a bare lowercase constant failed
  and the whole switcher silently rejected valid input. The suite goes red when
  that is reintroduced.
- **Visual tree** — constructs the real `MainWindow` on a shared STA thread and
  asserts the tree is sane: every region populated, score ring reflects the
  score, telemetry cards use the shared style, logging past the trim threshold
  does not throw, and showing the window logs no crash.

The UI tests call `window.Show()` rather than only `Measure`/`Arrange`. That
matters: a malformed item template in the DNS list threw only when realised
inside a live `ItemsControl` during the first real layout pass, so
construction-only and layout-only variants both passed while the app logged an
`ArgumentException` on every launch. The bug and the fix were each verified by
reintroducing the fault and confirming the suite goes red.

**Structure alone cannot catch contrast bugs.** Icons that inherit the window's
`Foreground` rendered white-on-white and were invisible while every layout
assertion still passed. There is now an explicit test that every pictograph uses
a brush dark enough to read on a light card, and another that the sub-screen
panel is opaque and scrollable. Both were confirmed to go red when the fix is
reverted.

Run tests with a clean build. A stale `WinCare Pro.dll` in the test output
folder masks source changes and will report the old result. Watch for this if
you restore a file with `Copy-Item`: it carries the *source* file's original
timestamp, which is older than the compiled dll, so MSBuild skips recompiling
and you test yesterday's code. Touch the file, or clean, after any restore.

Tests must never touch real user data. `TestService.New()` builds a
`SystemOptimizerService` whose undo history lives in a throwaway temp file; the
parameterless constructor points at the real
`%LOCALAPPDATA%\WinCarePro\history.json`, and using it from a test appended the
test's own transactions to the user's undo history on every run.

## Screenshots

Structural tests cannot tell you a panel is unreadable. `Tools/RenderShots`
rasterises the real window offscreen with `RenderTargetBitmap` and writes PNGs of
the main view and all seven sub-screens:

```powershell
dotnet run --project Tools/RenderShots -c Release -- "$env:TEMP\shots"
```

This needs neither a visible window nor a screen-capture API, so it works
headless. It found several defects that no test did: telemetry cards collapsed
behind the module list, DNS rows rendering with a null foreground, invisible
icons, and a sub-screen panel that was transparent enough to be unreadable.

Two caveats if you extend it:

- Rasterise `Window.Content`, not the `Window`. The outer element includes the
  title bar, which leaves a dead band at the bottom of every shot that is not a
  real layout bug.
- Install a `DispatcherSynchronizationContext` before constructing the window.
  Without it, `await` continuations run on the threadpool and touch
  `DependencyObject`s from the wrong thread, which looks exactly like an app bug
  but is an artefact of the harness. (The underlying app bug was real, though:
  `VM` was being dereferenced inside `Task.Run`.)

## Requirements

- Windows 10 1809 or later, x64
- .NET 8 SDK to build; the published exe needs no runtime