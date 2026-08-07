# WinCare Pro v1.3.0

Review-first Windows 11 maintenance software with Guided Care, system diagnostics,
privacy controls, cleanup tools, repair workflows, and local proof history.

Python 3.11+ · CustomTkinter (dark UI, blue accents) · psutil · Local AI / Rule-Based Heuristic Engine.

> **Target platform:** Windows 11 22H2 (build 22621) and later.
> Runs on earlier builds with a warning.
>
> Release signing, checkout, secure updates, clean-VM testing, and the accessible WPF interface are documented in [RELEASE_SETUP.md](RELEASE_SETUP.md).
> Objective release evidence is tracked in [READINESS_SCORECARD.md](READINESS_SCORECARD.md); no commercial-ready claim is made until all ten gates pass.

---

## 🚀 Quick Start

After running `build.ps1` once, double-click **`WinCarePro.vbs`** for a terminal-free
launch. `run.bat` delegates immediately to the same windowless launcher for
existing shortcuts. Windows may show one normal UAC consent prompt; no package
installation or terminal session runs during application startup.

---

## New in v1.3.0

### 1. Privacy controls (`privacy_engine.py`)
* **Bing Search Removal:** Disable Bing web search results & recommendations in the Windows 11 Start Menu.
* **Copilot & Windows Recall Disabler:** Registry toggle to disable Windows Copilot and Recall telemetry background agents.
* **Telemetry & Tracking Minimization:** Minimize telemetry level to Security (0), disable Advertising ID tracking, Location Services, and App Diagnostics tailored experiences.
* **Reviewable presets:** Maximum Privacy, Balanced Privacy, and Restore Defaults.

### 2. 🗑 UWP App Bloatware Uninstaller & Leftover Cleaner (`bloat_remover.py`)
* **PowerShell UWP Uninstaller:** Approved removal of selected pre-installed Microsoft apps.
* **Leftover Folder & Registry Scanner:** Scans `%APPDATA%`, `%LOCALAPPDATA%`, `%PROGRAMDATA%`, and startup registry keys for orphaned leftovers from uninstalled apps.

### 3. RAM and network tools (`performance_booster.py`)
* **RAM Standby Flusher:** Trims working sets across all running processes and reclaims standby memory cache without rebooting.
* **DNS Switcher:** Benchmark and switch system DNS to Cloudflare (`1.1.1.1`), Google (`8.8.8.8`), Quad9 (`9.9.9.9`), or Default DHCP.
* **TCP settings:** Reviews and, after approval, changes `TcpAckFrequency` and `TCPNoDelay`; results vary by network and application.

### 4. 🛡 Windows Native Baseline & Bloat Cleaner (`win_baseline.py`)
* Differentiates essential Windows core files (`csrss.exe`, `lsass.exe`, `svchost.exe`) from background bloat, updater daemons, and optional printer/driver services (`spooler`, `Fax`).
* Reviewable presets for printer services, Game Mode, optional background items, and telemetry settings.

### 5. Local advisor and software update assistant (`ai_engine.py`)
* Generates plain-English AI reports, explains process origins, and integrates with `winget` for 1-click app upgrades.
* Can connect to a user-run local Ollama service (`gemma3:1b`) or use deterministic offline rules.

---

## 📋 Complete Feature Overview

### Guided Care + Proof

The native WPF dashboard provides a read-only Guided Care workflow: run a scan,
review a deterministic ranked plan, choose one of five care profiles, inspect
local proof history, and read a weekly score/risk summary. Snapshots and timeline
events stay under `%LOCALAPPDATA%\WinCarePro\care`; no Guided Care data is sent
to a cloud service.

Guided Care never turns recommendations into shell commands. Risky work still
opens the existing Safety Center or Undo Center, where the normal preview,
approval, restore, and rollback safeguards apply. Stopping or timing out a scan
starts no repair action and records the interruption when the operation deadline
still allows it.

| Tab | What it does |
|---|---|
| **Dashboard** | Health Score (0-100), live CPU/RAM/Disk gauges, quick actions |
| **AI Advisor** | AI Diagnostic Audit, winget software update scanner, AI process explainer, AI Chat Assistant |
| **Privacy Shield** | Disable Start Menu Bing Search, Copilot/Recall, Advertising ID, Telemetry minimization |
| **Bloatware Remover** | UWP app bloatware uninstaller + AppData/Registry leftover folder cleaner |
| **RAM & Network** | Standby RAM cache flusher, Fast DNS switcher (Cloudflare/Google/Quad9), TCP Gaming Latency tweak |
| **Bloat & Baseline** | Windows baseline process scanner, Printer & driver task cleaner, 1-click performance presets |
| **Diagnostics** | Full system scan: disk SMART, free space, memory pressure, Event Log errors, driver issues |
| **Event Triage** | Decodes System-log errors into a ranked cause list with safe 1-click fixes |
| **Repairs** | SFC /scannow · DISM RestoreHealth · chkdsk /f /r · Reset Windows Update · Network reset |
| **Optimize** | Startup manager, optional-services manager, power plans, visual effects |
| **Processes & Cleanup** | Process manager, Authenticode signature inspector, dry-run temp & cache cleaner |
| **Old & Duplicate Files** | Review-first Desktop/Downloads scan for 180-day-old installers and SHA-256-confirmed duplicates |
| **Maintenance** | 1-Click Full Maintenance, winget updates, storage analyzer, quick tools launcher |
| **Settings** | Themes, auto restore point toggle, **Undo Center** (revert service/startup changes) |

The Guided Care WPF shell and local bridge build and pass automated tests on the
development PC. Commercial release still requires a signed installer, clean
Windows 11 VM install/launch/uninstall evidence, UI Automation verification in
that VM, controlled mutation/rollback testing, and live checkout/update-provider
configuration. These are release gates, not certified results.

---

## 🔒 Safety & Reversibility
* **Restore Points:** System Restore Point created before any repair.
* **Undo Center:** Every service or startup change is backed up and can be reverted with 1 click in **Settings → Undo Center**.
* **Built-In Tools:** Uses Microsoft's native SFC, DISM, chkdsk, netsh, powercfg, and winget commands.
