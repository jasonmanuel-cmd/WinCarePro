# Privacy Policy

**WinCare Pro — last updated 5 October 2026**

## The short version

WinCare Pro does not collect, transmit, sell or share any data. It has no
network code at all. Everything it knows stays on your computer.

## What the app stores

Two files, both under `%LOCALAPPDATA%\WinCarePro\`, created only when you use
the corresponding feature:

| File | What it holds | Why |
|---|---|---|
| `history.json` | A list of actions you performed, so Undo History survives a restart | Your undo history. Deletable at any time. |
| `crash.log` | Exception text, if the app crashes | So a crash can be diagnosed if you choose to send it. Only written on a fault. |
| `environment.txt` | OS version, DPI mode, which Windows tools are present | So a bug report can include it without asking you. Written on every launch. |

Nothing else is written. No registry keys are created outside the Windows
privacy settings you explicitly toggle yourself, and nothing is read from your
browsing history, documents, or files.

## What the app reads

WinCare inspects your system to do its job. It reads:

- Memory, CPU and disk usage
- Physical memory and battery state
- Disk SMART health
- Power plan
- Installed programs, for the uninstaller
- Windows privacy registry values, for the Privacy Shield toggles
- Which processes are using memory
- Directories under your temp folders, when you ask for a cleanup preview

It does not read file contents, only paths and sizes, and only in the temp
folders named in the app.

## What leaves your computer

Nothing, by default. There is no analytics, no crash reporting service, no
automatic update check, no telemetry, no advertising identifier, and no
outbound connection of any kind.

The **only** network call the app can make is a manual “Check for updates”
action. It sends one HTTPS GET to a version manifest you would point it at, and
it is never triggered on startup or in the background. Removing the network
code would not change any other behaviour.

If a crash happens, the details are written to `crash.log` on your machine. They
are only shared if you personally choose to send them to whoever is supporting
the app.

## Why there is no telemetry

A system optimizer that watches which programs you run, when you launch them and
how long you keep them could build a usage profile. WinCare deliberately does
not do this, and the Removable Apps list is a fixed curated list rather than
anything inferred from your behaviour — that is a design decision, not an
oversight.

## Children

WinCare is a system utility and is not directed at children. It collects no
data from anyone, including children.

## Third parties

None. No advertising, no analytics SDKs, no third-party services.

## Changes

Any material change to this policy will be noted here with a new date, and
shipped as an app update. Because the app does not collect data, a change here
cannot affect your privacy — it can only describe it more accurately.

## Contact

Questions about this policy: *(replace with a real contact address before
publishing — see `RELEASE.md`)*
