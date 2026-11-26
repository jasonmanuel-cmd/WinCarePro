# Privacy Policy

**WinCare Pro v2.0 — last updated 25 November 2026**

## The short version

WinCare Pro does **not** collect, transmit, sell, or share any personal or diagnostic data. It runs **100% offline** with zero background telemetry, zero analytics SDKs, and zero activation servers. Even Pro license verification uses offline asymmetric ECDSA cryptography on your PC.

## What the app stores locally

All app files live locally on your computer under `%LOCALAPPDATA%\WinCarePro\` (or, when installed as a packaged Microsoft Store MSIX app, inside Windows' per-package isolated `%LOCALAPPDATA%\Packages\<PackageFamilyName>\LocalCache\Local\WinCarePro\` directory):

| File | Created By Default? | What it holds | Why |
|---|---|---|---|
| `history.json` | When you perform an action | Action log and prior-state payloads for reversible settings | So Undo History survives app restarts. Deletable at any time. |
| `options.json` | When you save preferences | Header refresh interval (5–60s) and local-diagnostics toggle | Remembers your lightweight app preferences. |
| `license.json` | When you activate a key | Your signed `WCP2.` offline license token and activation timestamp | Keeps Pro activated across restarts with zero server checks. |
| `audit.json` | When you run rescue actions | Bounded action outcome labels (no file paths or PIDs) | Local safety audit trail for Defender/app-close requests. |
| `schedule.json` | When you save scheduler settings | Session-only maintenance schedule preferences | Remembers schedule frequency (requires fresh session consent). |
| `crash.log` | **Off by default** (opt-in in Options) | Exception text if an unhandled fault occurs (capped at 2 MB) | Helps you inspect or share a bug report only if you choose to. |
| `environment.txt` | **Off by default** (opt-in in Options) | OS version, DPI mode, and basic runtime details | Optional local troubleshooting summary; never uploaded. |

If you manually click **Preview local report… → Save report as…** on the Dashboard, WinCare shows you the exact sanitized JSON text first and writes it only to the file path you choose in the Windows Save dialog.

## What the app reads locally

To measure headroom and display diagnostics on your screen, WinCare reads:

- Physical RAM, CPU usage, battery state, and system-drive capacity
- Disk SMART health status and active Windows power scheme
- Startup entries (`Run` registry keys and your Startup folder)
- Installed Win32 and Microsoft Store applications (for App Remover)
- Two current-user privacy registry values (Advertising ID & Tailored Experiences)
- Running process names, PIDs, and working-set sizes (when you sample Memory & Apps)
- Microsoft Defender status and event log scan records (only when you click Read on the Defender page)
- File paths, timestamps, and sizes inside temporary directories or a folder you select in Disk Analyzer (file contents are never read)

## Network activity

- **Background / Startup / Licensing:** **Zero network calls.**
- **Manual Latency Test (Network & DNS tab):** Only if you explicitly click **Test Latency**, Windows sends standard ICMP pings to measure round-trip latency.
- **Manual Update Check (if configured):** Only if triggered manually by the user when an `update-source.txt` manifest URL is configured.

## Third parties & children

WinCare Pro embeds **no** advertising, analytics, tracking pixels, telemetry SDKs, or cloud SDKs. If you purchase a license key through a merchant of record (such as Microsoft Store, Lemon Squeezy, or Paddle), payment processing is governed by that checkout provider's privacy policy; WinCare Pro itself never receives or transmits payment data. WinCare is a general Windows utility and collects no data from anyone, including children.

## Contact

Support & privacy inquiries: `support@wincarepro.example` *(replace with your live support email before public launch — see `MONETIZATION-READY-CHECKLIST.md`)*.
