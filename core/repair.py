"""
WinCare Pro - Core repair engine (mutating operations with live streaming).
"""
from __future__ import annotations

import os
from datetime import datetime
from pathlib import Path

from core.shell import safe_ps, run_cmd, stream_cmd, run_ps
from core.platform import IS_WINDOWS

try:
    import psutil
except ImportError:
    psutil = None

try:
    import winreg
except ImportError:
    winreg = None


class RepairEngine:
    def __init__(self, logger):
        self.log = logger

    # ---- restore point ------------------------------------------------
    def create_restore_point(self, out, label="WinCare Pro checkpoint") -> bool:
        """
        Create a System Restore Point via PowerShell Checkpoint-Computer.
        Windows enforces 1 restore point / 24h by default - we treat that
        case as success (a recent point already protects the user).
        """
        label = label.replace("'", "").replace('"', "")[:60]
        out(f">> Creating System Restore Point: '{label}' ...")
        script = (
            "Enable-ComputerRestore -Drive \"$env:SystemDrive\\\" "
            "-ErrorAction SilentlyContinue; "
            "Checkpoint-Computer -Description $args[0] "
            "-RestorePointType MODIFY_SETTINGS"
        )
        rc, output = safe_ps(script, label, timeout=240)
        for ln in output.splitlines():
            out("   " + ln)
        low = output.lower()
        if "1440" in output or "already been created" in low:
            out(">> A restore point already exists from the last 24h - protected, continuing.")
            self.log.log("Restore point skipped (24h limit)", label, "WARN")
            return True
        ok = (rc == 0 and "exception" not in low and "error" not in low[:200])
        out(">> Restore point created." if ok else
            ">> FAILED to create restore point (System Restore may be disabled).")
        self.log.log("Create restore point", f"{label} ok={ok}",
                      "INFO" if ok else "ERROR")
        return ok

    # ---- built-in Windows repair tools ---------------------------------
    def sfc_scan(self, out):
        """System File Checker - verifies & repairs protected system files."""
        out(">> Running: sfc /scannow  (this takes 5-20 minutes)")
        rc = stream_cmd(["sfc", "/scannow"], out)
        out(f">> SFC finished with exit code {rc}.")
        self.log.log("SFC /scannow", f"rc={rc}")
        return rc

    def dism_restore(self, out):
        """DISM RestoreHealth - repairs the component store SFC relies on."""
        out(">> Running: DISM /Online /Cleanup-Image /RestoreHealth")
        out("   (needs internet for Windows Update source; 10-30 minutes)")
        rc = stream_cmd(["DISM", "/Online", "/Cleanup-Image", "/RestoreHealth"], out)
        out(f">> DISM finished with exit code {rc}.")
        self.log.log("DISM RestoreHealth", f"rc={rc}")
        return rc

    def dism_component_cleanup(self, out):
        """Removes superseded component-store files (old update payloads)."""
        out(">> Running: DISM /Online /Cleanup-Image /StartComponentCleanup")
        rc = stream_cmd(["DISM", "/Online", "/Cleanup-Image",
                         "/StartComponentCleanup"], out)
        out(f">> Component cleanup finished (rc={rc}).")
        self.log.log("DISM StartComponentCleanup", f"rc={rc}")
        return rc

    def chkdsk(self, out):
        """
        chkdsk C: /f /r - the system volume is in use, so Windows asks to
        schedule the check at next reboot; we answer 'Y' automatically.
        """
        drive = os.environ.get("SystemDrive", "C:")
        out(f">> Running: chkdsk {drive} /f /r")
        out("   The system drive is in use -> Windows will schedule the scan")
        out("   for the NEXT REBOOT. The reboot itself may take a long time.")
        rc = stream_cmd(
            ["chkdsk", drive, "/f", "/r"], out, input_text="Y\n")
        out(f">> chkdsk finished (rc={rc}). If scheduled, reboot to run it.")
        self.log.log("chkdsk /f /r", f"drive={drive} rc={rc}")
        return rc

    def defrag_optimize(self, out):
        """'defrag /O' = TRIM on SSDs, defragment on HDDs - safe either way."""
        drive = os.environ.get("SystemDrive", "C:")
        out(f">> Running: defrag {drive} /O  (optimize / retrim)")
        rc = stream_cmd(["defrag", drive, "/O"], out)
        out(f">> Drive optimize finished (rc={rc}).")
        self.log.log("defrag /O", f"rc={rc}")
        return rc

    # ---- Windows Update stack reset -------------------------------------
    def reset_windows_update(self, out):
        """
        Standard Microsoft procedure: stop WU services, rename the two cache
        folders (rename = reversible, nothing is deleted), restart services.
        """
        out(">> Resetting Windows Update components ...")
        services = ["wuauserv", "cryptSvc", "bits", "msiserver"]
        for s in services:
            out(f"   stopping {s} ...")
            run_cmd(["net", "stop", s], timeout=90)
        stamp = datetime.now().strftime("%Y%m%d_%H%M%S")
        renames = [
            (r"%systemroot%\SoftwareDistribution", f"SoftwareDistribution.old_{stamp}"),
            (r"%systemroot%\System32\catroot2", f"catroot2.old_{stamp}"),
        ]
        for src, dst in renames:
            try:
                source = Path(os.path.expandvars(src))
                source.rename(source.parent / dst)
                rc, o = 0, ""
            except OSError as exc:
                rc, o = -1, str(exc)
            out(f"   rename {src} -> {dst}: "
                + ("OK" if rc == 0 else f"skipped ({o.strip() or 'in use'})"))
        for s in services:
            out(f"   starting {s} ...")
            run_cmd(["net", "start", s], timeout=90)
        out(">> Windows Update reset complete. Old caches were RENAMED, not")
        out("   deleted - Windows rebuilds them on next update check.")
        self.log.log("Reset Windows Update components", f"stamp={stamp}")
        return 0

    # ---- network stack reset ---------------------------------------------
    def reset_network(self, out):
        """netsh winsock/ip reset + DHCP renew + DNS flush. Reboot advised."""
        out(">> Resetting network stack ...")
        steps = [
            ("Winsock reset", ["netsh", "winsock", "reset"]),
            ("TCP/IP reset", ["netsh", "int", "ip", "reset"]),
            ("Flush DNS cache", ["ipconfig", "/flushdns"]),
            ("Release DHCP lease", ["ipconfig", "/release"]),
            ("Renew DHCP lease", ["ipconfig", "/renew"]),
        ]
        for name, cmd in steps:
            out(f"   {name} ...")
            rc, o = run_cmd(cmd, timeout=120)
            for ln in o.splitlines()[:6]:
                out("      " + ln)
        out(">> Network reset done. REBOOT to fully apply winsock/IP reset.")
        self.log.log("Reset network stack")
        return 0

    def network_diagnostics(self, out):
        """Read-only connectivity triage: adapters, gateway, ping, DNS."""
        out(">> Network diagnostics")
        try:
            stats = psutil.net_if_stats()
            for nic, st in stats.items():
                if st.isup:
                    out(f"   adapter UP : {nic} ({st.speed} Mbps)")
        except Exception as e:
            out(f"   adapter query failed: {e}")
        rc, o = run_cmd(["ipconfig"], timeout=30)
        gateway = ""
        for ln in o.splitlines():
            if "Default Gateway" in ln and ln.split(":")[-1].strip():
                gateway = ln.split(":")[-1].strip()
        out(f"   default gateway: {gateway or 'NOT FOUND (check router/cable)'}")
        for label, target in (("gateway", gateway or "192.168.1.1"),
                              ("internet (8.8.8.8)", "8.8.8.8")):
            rc, o = run_cmd(["ping", "-n", "2", "-w", "1500", target], timeout=20)
            out(f"   ping {label}: {'OK' if rc == 0 else 'FAILED'}")
        rc, o = run_cmd(["nslookup", "microsoft.com"], timeout=20)
        out(f"   DNS resolution: {'OK' if rc == 0 else 'FAILED'}")
        out(">> Diagnostics complete. Use 'Reset network stack' if issues persist.")
        self.log.log("Network diagnostics")
        return 0

    # ---- user profile (basic, non-destructive) ----------------------------
    def repair_profile_check(self, out):
        """
        Basic profile repair: DETECT corruption signals (.bak SIDs, temp
        profile) and report exact guidance. Deliberately non-destructive -
        automated ProfileList surgery bricks accounts.
        """
        out(">> Checking user profile health ...")
        problems = 0
        if winreg:
            try:
                base = r"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList"
                key = winreg.OpenKey(winreg.HKEY_LOCAL_MACHINE, base)
                i = 0
                while True:
                    try:
                        sid = winreg.EnumKey(key, i); i += 1
                    except OSError:
                        break
                    if sid.endswith(".bak"):
                        problems += 1
                        out(f"   [!] Backup profile key found: {sid}")
                        out("       -> Windows loaded a TEMP profile at least once.")
                winreg.CloseKey(key)
            except OSError as e:
                out(f"   registry check failed: {e}")
        prof = os.environ.get("USERPROFILE", "")
        if "\\TEMP" in prof.upper():
            problems += 1
            out(f"   [!] You are running on a TEMPORARY profile: {prof}")
        if problems == 0:
            out("   No corruption signals found. Profile looks healthy.")
        else:
            out("   RECOMMENDED SEQUENCE:")
            out("   1) Run SFC + DISM from this tab (fixes system-side causes).")
            out("   2) Reboot twice.")
            out("   3) If the temp profile persists: create a NEW local account")
            out("      (Settings > Accounts), sign in, copy your files over.")
            out("      That is the only safe fix Microsoft supports.")
        self.log.log("Profile health check", f"problems={problems}")
        return problems


    # ---- crash-dump readiness ("repair the black box") ---------------------
    def repair_crash_dump(self, out):
        """
        Restore Windows' ability to record WHY it crashes:
          * CrashDumpEnabled = 7  (Automatic memory dump)
          * pagefile back to system-managed (dumps are written through it)
        Without this, Kernel-Power 41 events leave no evidence.
        """
        out(">> Setting crash dump type to 'Automatic memory dump' ...")
        rc, o = run_cmd(["reg", "add",
                         r"HKLM\SYSTEM\CurrentControlSet\Control\CrashControl",
                         "/v", "CrashDumpEnabled", "/t", "REG_DWORD",
                         "/d", "7", "/f"], timeout=30)
        out("   " + ("OK" if rc == 0 else f"failed: {o[:100]}"))
        out(">> Ensuring pagefile is system-managed ...")
        rc2, o2 = run_ps(
            "$cs = Get-CimInstance Win32_ComputerSystem; "
            "if (-not $cs.AutomaticManagedPagefile) { "
            "Set-CimInstance -InputObject $cs -Property "
            "@{AutomaticManagedPagefile=$true}; "
            "'pagefile switched to system-managed' } "
            "else { 'pagefile already system-managed' }", timeout=60)
        out("   " + (o2.strip().splitlines()[-1] if o2.strip() else "done"))
        out(">> Crash-dump repair complete. REBOOT to apply. The next crash")
        out("   will leave a dump in C:\\Windows for diagnosis.")
        self.log.log("Crash-dump settings repaired",
                     f"reg rc={rc}, pagefile rc={rc2}")
        return 0 if rc == 0 else 1