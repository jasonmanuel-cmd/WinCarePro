# End User Licence Agreement

**WinCare Pro — version 1.0, 5 October 2026**

*(Drafted to match what the app actually does. Review before publishing — see
`RELEASE.md`. This is not legal advice.)*

## 1. Licence

WinCare Pro is licensed, not sold. Subject to the terms below, you get a
non-exclusive, non-transferable, revocable licence to install and use it on any
number of computers you own or control.

## 2. What the app does when you use it

WinCare modifies your system. Specifically, and only when you ask it to:

- deletes files from your temporary folders
- empties the Recycle Bin
- deletes staged Windows Update downloads
- runs the Windows component-store cleanup
- flushes memory caches
- changes your DNS server and power plan
- changes Windows privacy registry values
- runs a program's own uninstaller, or removes a Microsoft Store app

**These actions are not reversible in most cases.** WinCare records what it did
in a local history, but that history is a record, not an undo. Where a genuine
reversal exists — a privacy toggle, a Store app re-registration — the app offers
it; where it does not, the app says so rather than offering a "Revert" button
that cannot work.

Take a backup before using the cleanup tools. Removing your Recycle Bin empties
it permanently.

## 3. No warranty

**WinCare Pro is provided "as is", without warranty of any kind, express or
implied**, including but not limited to warranties of merchantability, fitness
for a particular purpose, and non-infringement.

The app is a convenience tool operating on a complex operating system. We test
it against real system state, and it is written to be honest about what it can
and cannot do. We cannot guarantee it is correct on every hardware
configuration, Windows edition, or third-party modification.

## 4. Limitation of liability

To the maximum extent permitted by law, the authors and copyright holders are
not liable for any direct, indirect, incidental, special, consequential or
punitive damages, or for any loss of data, system instability, lost
configuration, or network changes arising from your use of the app — even if we
were advised such damages were possible.

Nothing in this agreement limits liability that cannot lawfully be limited.

## 5. What "asInvoker" means

WinCare runs without administrator privileges by default. Only certain actions
require elevation, and the app asks before elevating. You can decline, and the
app remains usable for everything that does not need it.

## 6. No warranty that a tool will do anything

Some tools in this app may not appear to help, and some are arguably
pointless. Where that is known to be the case — "free RAM", for example — the app
says so rather than implying a benefit it cannot deliver. This honesty is part
of the product's design and we ask that it be preserved in any redistribution.

## 7. Termination

This licence ends if you redistribute the app without this agreement, or if
you are not the licensed user. It also ends if you modify the app in a way that
circumvents its safety behaviour.

## 8. Third-party components

WinCare uses CommunityToolkit.Mvvm and System.Management, both MIT licensed.
The app does not bundle a browser engine or any advertising or analytics SDK.

## 9. No warranty re-export

You may not sell this software, or any derivative of it, as your own work.

## 10. Governing law

This agreement is governed by the laws of the jurisdiction in which the
publisher is established. *(Fill in before publishing.)*

## 11. Contact

*(Replace with a real contact address before publishing — see `RELEASE.md`.)*
