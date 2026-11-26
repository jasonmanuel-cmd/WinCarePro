# End User License Agreement (EULA)

**WinCare Pro v2.0 — 25 November 2026**

*(Drafted to match exact v2.0 application behavior. Review jurisdiction/contact details before public sales — see `MONETIZATION-READY-CHECKLIST.md`.)*

## 1. License Grant & Scope

WinCare Pro is licensed, not sold.
- **Community / Beta Evaluation:** You may install and evaluate WinCare Pro free of charge on computers you own or control.
- **WinCare Pro Paid License (`WCP2.` key or Microsoft Store purchase):** Grants a non-exclusive license to activate and use WinCare Pro v2.x (all `2.x` minor and maintenance updates) on up to the number of Windows PCs specified on your order (default: **1 PC**). You may transfer your single-PC license to a replacement computer you own by removing the local license on the old machine (`Options → Remove Local License`). Future major versions (e.g., `v3.0`) may be offered separately.

## 2. Honest System Behavior & Reversibility

WinCare Pro modifies system state **only when you explicitly confirm or trigger an action**:

- **Genuinely Reversible Actions (via History & Logs → Rollback):**
  - Windows Power Plan changes (`Balanced`, `High performance`, `Power saver`)
  - Startup program enable/disable toggles
  - Supported current-user Privacy Shield registry toggles (`Advertising ID`, `Tailored Experiences`)
  - IPv4 DNS server changes on a selected network adapter
  *(Note: if a setting was subsequently modified outside WinCare Pro, Rollback will safely refuse to overwrite the newer external state.)*

- **Actions NOT Automatically Reversible by WinCare Pro:**
  - Confirmed temporary-file cleanup (permanent deletion of unchanged previewed temp files)
  - Disk Space Analyzer removals (sent to the Windows Recycle Bin; restore manually from Recycle Bin before emptying it)
  - Confirmed graceful optional-app close (`CloseMainWindow()` only; save unsaved work beforehand)
  - Application uninstallation (delegated to the vendor's uninstaller or `Remove-AppxPackage`)
  - Microsoft Defender scans (remediation/quarantine is managed inside Windows Security → Protection history)

Always maintain regular backups of important personal files before running disk cleanup or uninstallation tools.

## 3. Standard User Privilege (`asInvoker`)

WinCare Pro runs with standard user permissions (`asInvoker`) by default and does not install kernel drivers, background services, or login tasks. Certain Windows actions (such as changing adapter IPv4 DNS or uninstalling machine-wide software) require Administrator privileges and will clearly inform you when elevation is needed.

## 4. Refunds (Direct Purchases)

Direct license purchases include a **30-day no-questions-asked refund window** from the date of purchase. Microsoft Store purchases are governed by Microsoft Store's standard refund policy. Upon refund, remove the local license key from your PC.

## 5. Disclaimer of Warranty

**WinCare Pro is provided "AS IS", without warranty of any kind, express or implied**, including without limitation warranties of merchantability, fitness for a particular purpose, non-infringement, or guaranteed performance speedups. While WinCare Pro measures real hardware metrics and avoids misleading claims, results vary by hardware, workload, and Windows configuration.

## 6. Limitation of Liability

To the maximum extent permitted by applicable law, the authors and publisher shall not be liable for any indirect, incidental, special, consequential, or punitive damages, or for loss of data, revenue, or configuration arising from use of the software, exceeding the amount you paid for your license. Nothing in this agreement limits liability that cannot lawfully be excluded.

## 7. Open-Source & Runtime Notices

WinCare Pro is built on Microsoft .NET 10 (`MIT`) and uses `CommunityToolkit.Mvvm` (`MIT`) and `System.Management` (`MIT`).

## 8. Governing Law & Contact

Governed by the laws of the publisher's principal jurisdiction.
Support & licensing contact: `support@wincarepro.example` *(replace before public sales)*.
