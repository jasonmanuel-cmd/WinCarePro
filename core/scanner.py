"""
WinCare Pro - Core diagnostics scanner (read-only, never mutates).
"""
from __future__ import annotations

import os
import time
from datetime import datetime
from pathlib import Path

try:
    import psutil
except ImportError:
    psutil = None

try:
    import winreg
except ImportError:
    winreg = None

from core.shell import safe_ps, ps_json, run_ps, human_bytes
from core.platform import IS_WINDOWS
from core.health import HealthScore
from core.optimizer import Optimizer  # circular import handled at runtime


# Severity sort order for findings display
SEVERITY_ORDER = {"Critical": 0, "Warning": 1, "Info": 2, "OK": 3}


class Scanner:
    """
    Runs the comprehensive scan. Each check returns findings:
      {"severity": Critical|Warning|Info|OK, "category": str,
       "title": str, "recommendation": str}
    and contributes to the metrics dict used by HealthScore.
    """

    def __init__(self, logger):
        self.log = logger

    # ---- individual checks ------------------------------------------------
    def check_disk_health(self, findings, metrics):
        """SMART / HealthStatus via Get-PhysicalDisk."""
        disks = ps_json(
            "Get-PhysicalDisk | Select-Object FriendlyName, MediaType, "
            "HealthStatus, @{n='SizeGB';e={[math]::Round($_.Size/1GB,1)}} "
            "| ConvertTo-Json", timeout=60)
        if not disks:
            findings.append(dict(severity="Info", category="Disk",
                title="Could not query physical disk health (needs admin?)",
                recommendation="Re-run as Administrator for SMART status."))
            return
        unhealthy = False
        for d in disks:
            status = str(d.get("HealthStatus", "Unknown"))
            name = f"{d.get('FriendlyName','disk')} ({d.get('SizeGB','?')} GB, {d.get('MediaType','')})"
            if status.lower() == "healthy":
                findings.append(dict(severity="OK", category="Disk",
                    title=f"{name}: SMART status Healthy",
                    recommendation="No action needed."))
            else:
                unhealthy = True
                findings.append(dict(severity="Critical", category="Disk",
                    title=f"{name}: SMART status '{status}'",
                    recommendation="BACK UP YOUR DATA NOW, then plan disk replacement. "
                                   "Run 'chkdsk /f /r' from the Repairs tab."))
        metrics["disk_unhealthy"] = unhealthy

    def check_disk_space(self, findings, metrics):
        drive = os.environ.get("SystemDrive", "C:") + "\\"
        try:
            du = psutil.disk_usage(drive)
        except OSError:
            return
        free_pct = round(100 - du.percent, 1)
        metrics["disk_free_pct"] = free_pct
        sev = ("Critical" if free_pct < 5 else
               "Warning" if free_pct < 15 else "OK")
        findings.append(dict(severity=sev, category="Disk",
            title=f"System drive: {human_bytes(du.free)} free ({free_pct}%)",
            recommendation="Run Cleanup + Storage Analyzer to reclaim space."
                           if sev != "OK" else "Healthy free-space margin."))

    def check_memory(self, findings, metrics):
        """RAM pressure + top consumers (leak candidates)."""
        vm = psutil.virtual_memory()
        sw = psutil.swap_memory()
        metrics["ram_pct"] = vm.percent
        sev = "Critical" if vm.percent > 92 else "Warning" if vm.percent > 80 else "OK"
        findings.append(dict(severity=sev, category="Memory",
            title=f"RAM usage {vm.percent}% of {human_bytes(vm.total)} "
                  f"(pagefile {sw.percent}%)",
            recommendation="Close heavy apps or review top consumers below."
                           if sev != "OK" else "Memory pressure is normal."))
        # top-3 consumers - one scan can't prove a leak; it flags candidates.
        procs = []
        for p in psutil.process_iter(["name", "memory_info"]):
            try:
                procs.append((p.info["memory_info"].rss, p.info["name"]))
            except (psutil.NoSuchProcess, psutil.AccessDenied, TypeError):
                continue
        for rss, name in sorted(procs, reverse=True)[:3]:
            if rss > 1.5 * 1024**3:  # only surface >1.5 GB consumers
                findings.append(dict(severity="Info", category="Memory",
                    title=f"High memory consumer: {name} ({human_bytes(rss)})",
                    recommendation="If this grows over time, restart the app - "
                                   "possible memory leak."))

    def check_event_log(self, findings, metrics):
        """Count Critical+Error events in System log, last 7 days."""
        script = (
            "$c=(Get-WinEvent -FilterHashtable @{LogName='System';Level=1,2;"
            "StartTime=(Get-Date).AddDays(-7)} -ErrorAction SilentlyContinue "
            "| Measure-Object).Count; $c")
        rc, out = run_ps(script, timeout=90)
        try:
            count = int(out.strip().splitlines()[-1])
        except (ValueError, IndexError):
            count = 0
        metrics["event_errors"] = count
        sev = "Warning" if count > 50 else "Info" if count > 0 else "OK"
        findings.append(dict(severity=sev, category="Event Log",
            title=f"{count} error/critical events in System log (7 days)",
            recommendation="Open Event Viewer (eventvwr.msc) and review the "
                           "most frequent sources." if count else "Log is clean."))
        # Show the 3 most recent error sources for quick triage
        if count:
            recent = ps_json(
                "Get-WinEvent -FilterHashtable @{LogName='System';Level=1,2;"
                "StartTime=(Get-Date).AddDays(-7)} -MaxEvents 3 -ErrorAction "
                "SilentlyContinue | Select-Object ProviderName, Id | ConvertTo-Json",
                timeout=60)
            for ev in recent:
                findings.append(dict(severity="Info", category="Event Log",
                    title=f"Recent error source: {ev.get('ProviderName','?')} "
                          f"(Event ID {ev.get('Id','?')})",
                    recommendation="Search this Event ID if it repeats frequently."))

    def check_updates(self, findings, metrics):
        """Pending Windows updates via the Update Agent COM API."""
        script = (
            "try { $s=New-Object -ComObject Microsoft.Update.Session; "
            "$r=$s.CreateUpdateSearcher().Search(\"IsInstalled=0 and Type='Software' and IsHidden=0\"); "
            "$r.Updates.Count } catch { 'ERR' }")
        rc, out = run_ps(script, timeout=180)
        last = out.strip().splitlines()[-1] if out.strip() else "ERR"
        if last == "ERR" or rc != 0:
            findings.append(dict(severity="Info", category="Updates",
                title="Could not query Windows Update (service busy or offline)",
                recommendation="Open Settings > Windows Update manually."))
            return
        try:
            count = int(last)
        except ValueError:
            count = 0
        metrics["pending_updates"] = count
        sev = "Warning" if count >= 5 else "Info" if count > 0 else "OK"
        findings.append(dict(severity=sev, category="Updates",
            title=f"{count} pending Windows update(s)",
            recommendation="Install from Maintenance tab > Windows Update."
                           if count else "System is up to date."))

    def check_drivers(self, findings, metrics):
        """Devices with ConfigManagerErrorCode != 0 (driver problems)."""
        devs = ps_json(
            "Get-CimInstance Win32_PnPEntity -Filter 'ConfigManagerErrorCode <> 0' "
            "| Select-Object Name, ConfigManagerErrorCode -First 10 | ConvertTo-Json",
            timeout=90)
        metrics["driver_issues"] = len(devs)
        if not devs:
            findings.append(dict(severity="OK", category="Drivers",
                title="No devices reporting driver errors",
                recommendation="No action needed."))
        for d in devs:
            findings.append(dict(severity="Warning", category="Drivers",
                title=f"Device problem: {d.get('Name','Unknown device')} "
                      f"(code {d.get('ConfigManagerErrorCode','?')})",
                recommendation="Open Device Manager (devmgmt.msc) and update or "
                               "reinstall this driver."))

    def check_startup(self, findings, metrics):
        items = Optimizer.list_startup_items()
        enabled = [i for i in items if i["enabled"]]
        metrics["startup_count"] = len(enabled)
        heavy = [i for i in enabled if i["impact"] == "High"]
        sev = "Warning" if len(enabled) > 10 or heavy else "Info" if enabled else "OK"
        findings.append(dict(severity=sev, category="Startup",
            title=f"{len(enabled)} startup program(s) enabled"
                  + (f", {len(heavy)} known high-impact" if heavy else ""),
            recommendation="Disable non-essential entries in Optimize > Startup "
                           "Programs." if sev == "Warning" else "Startup load is reasonable."))
        for i in heavy[:5]:
            findings.append(dict(severity="Info", category="Startup",
                title=f"High-impact startup item: {i['name']}",
                recommendation="Disable it if you don't need it at every boot."))

    def check_integrity_hint(self, findings, metrics):
        """
        Lightweight integrity signals (a full SFC scan belongs to Repairs).
        Flags orphaned startup registry entries whose target file is missing -
        a common sign of sloppy uninstalls / registry rot.
        """
        orphans = 0
        for item in Optimizer.list_startup_items():
            exe = Optimizer.extract_exe_path(item["command"])
            if exe and not Path(exe).exists():
                orphans += 1
                findings.append(dict(severity="Info", category="Registry",
                    title=f"Orphaned startup entry: {item['name']} -> missing file",
                    recommendation="Safe to disable/remove via Optimize tab."))
        findings.append(dict(severity="Info", category="System Files",
            title="System file integrity: run SFC for a definitive check",
            recommendation="Repairs tab > 'SFC /scannow' verifies and repairs "
                           "protected system files."))
        metrics["orphaned_startup"] = orphans

    def check_services(self, findings, metrics):
        """Flag known-optional services that are currently running."""
        running_optional = []
        for svc in Optimizer.OPTIONAL_SERVICES:
            try:
                s = psutil.win_service_get(svc["name"])
                if s.status() == "running":
                    running_optional.append(svc)
            except Exception:
                continue
        for svc in running_optional:
            findings.append(dict(severity="Info", category="Services",
                title=f"Optional service running: {svc['display']}",
                recommendation=f"{svc['why']} Manage in Optimize > Services."))
        if not running_optional:
            findings.append(dict(severity="OK", category="Services",
                title="No unnecessary optional services detected running",
                recommendation="No action needed."))

    def check_uptime(self, findings, metrics):
        days = (time.time() - psutil.boot_time()) / 86400
        metrics["uptime_days"] = round(days, 1)
        if days > 7:
            findings.append(dict(severity="Warning" if days > 14 else "Info",
                category="System",
                title=f"System has not rebooted in {days:.0f} days",
                recommendation="Reboot to apply updates and clear leaked resources."))

    # ---- v1.1 checks: power transitions, crash dumps, tool conflicts, BIOS --
    def check_power_transition(self, findings, metrics):
        """Fast Startup is the #1 software cause of 'unclean shutdown' storms."""
        state = Optimizer.fast_startup_enabled()
        if state is None:
            return
        metrics["fast_startup"] = state
        if state:
            findings.append(dict(severity="Info", category="Power",
                title="Fast Startup is ENABLED",
                recommendation="If Event Triage shows Kernel-Power 41 / HAL "
                               "errors, disable it there — hybrid boot is a "
                               "common cause of power-transition faults."))
        else:
            findings.append(dict(severity="OK", category="Power",
                title="Fast Startup is disabled (full shutdowns)",
                recommendation="No action needed."))

    def check_crash_dump(self, findings, metrics):
        """Verify Windows can actually record a crash (the 'black box')."""
        if not winreg:
            return
        try:
            key = winreg.OpenKey(winreg.HKEY_LOCAL_MACHINE,
                                 r"SYSTEM\CurrentControlSet\Control\CrashControl")
            val, _ = winreg.QueryValueEx(key, "CrashDumpEnabled")
            winreg.CloseKey(key)
        except OSError:
            return
        metrics["crash_dump"] = val
        if val == 0:
            findings.append(dict(severity="Warning", category="Reliability",
                title="Crash dumps are DISABLED — crashes leave no evidence",
                recommendation="Diagnostics > Event Triage > 'Repair "
                               "crash-dump settings'."))
        else:
            findings.append(dict(severity="OK", category="Reliability",
                title="Crash-dump recording is enabled",
                recommendation="No action needed."))

    CONFLICT_TOOLS = ("spybot", "superantispyware", "ccleaner",
                      "microsoft pc manager", "iobit", "driver booster",
                      "advanced systemcare", "restoro", "reimage",
                      "wise care", "glary")

    def check_conflicting_tools(self, findings, metrics):
        """
        Multiple overlapping cleaner/AV utilities are a CAUSE of instability
        (competing filter drivers, orphaned services). Enumerate installed
        apps from the Uninstall registry keys and flag known offenders.
        """
        if not winreg:
            return
        found = set()
        roots = [
            (winreg.HKEY_LOCAL_MACHINE,
             r"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"),
            (winreg.HKEY_LOCAL_MACHINE,
             r"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"),
            (winreg.HKEY_CURRENT_USER,
             r"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"),
        ]
        for hive, path in roots:
            try:
                key = winreg.OpenKey(hive, path)
            except OSError:
                continue
            i = 0
            while True:
                try:
                    sub = winreg.EnumKey(key, i); i += 1
                except OSError:
                    break
                try:
                    sk = winreg.OpenKey(key, sub)
                    name, _ = winreg.QueryValueEx(sk, "DisplayName")
                    winreg.CloseKey(sk)
                except OSError:
                    continue
                low = str(name).lower()
                for tool in self.CONFLICT_TOOLS:
                    if tool in low:
                        found.add(str(name))
            winreg.CloseKey(key)
        metrics["conflicting_tools"] = len(found)
        for name in sorted(found):
            findings.append(dict(severity="Warning", category="Conflicts",
                title=f"Competing maintenance/AV utility installed: {name}",
                recommendation="Overlapping cleaners and their filter "
                               "drivers cause service failures and boot "
                               "issues. Uninstall it — Windows Defender + "
                               "WinCare Pro cover this ground."))
        if not found:
            findings.append(dict(severity="OK", category="Conflicts",
                title="No known conflicting utilities installed",
                recommendation="No action needed."))

    def check_bios_age(self, findings, metrics):
        """Old firmware = unfixed power/stability bugs. Detect + point only —
        flashing BIOS belongs to the vendor's own tool, never to us."""
        info = ps_json(
            "Get-CimInstance Win32_BIOS | Select-Object SMBIOSBIOSVersion, "
            "@{n='Date';e={$_.ReleaseDate.ToString('yyyy-MM-dd')}} "
            "| ConvertTo-Json", timeout=60)
        if not info:
            return
        ver = info[0].get("SMBIOSBIOSVersion", "?")
        date = info[0].get("Date", "")
        try:
            age_days = (datetime.now() - datetime.fromisoformat(date)).days
        except (ValueError, TypeError):
            return
        metrics["bios_age_days"] = age_days
        if age_days > 540:
            findings.append(dict(severity="Info", category="Firmware",
                title=f"BIOS {ver} is {age_days // 30} months old "
                      f"(released {date})",
                recommendation="Check your PC vendor's support page for a "
                               "BIOS update — firmware fixes power/stability "
                               "bugs no software tweak can. WinCare never "
                               "flashes firmware itself."))
        else:
            findings.append(dict(severity="OK", category="Firmware",
                title=f"BIOS {ver} is recent (released {date})",
                recommendation="No action needed."))

    # ---- orchestrator ------------------------------------------------------
    def run_full_scan(self, progress_cb=None, cancel_event=None):
        """
        Execute all checks sequentially. progress_cb(step_name, pct) keeps the
        UI informed; cancel_event allows a graceful abort between checks.
        Returns (findings, metrics, score, breakdown).
        """
        findings, metrics = [], {}
        metrics["cpu_pct"] = psutil.cpu_percent(interval=1)
        metrics["ram_pct"] = psutil.virtual_memory().percent

        steps = [
            ("Disk space", self.check_disk_space),
            ("Disk health (SMART)", self.check_disk_health),
            ("Memory", self.check_memory),
            ("Event Log errors", self.check_event_log),
            ("Startup programs", self.check_startup),
            ("Optional services", self.check_services),
            ("Driver issues", self.check_drivers),
            ("Registry / integrity hints", self.check_integrity_hint),
            ("Power transition config", self.check_power_transition),
            ("Crash-dump readiness", self.check_crash_dump),
            ("Conflicting utilities", self.check_conflicting_tools),
            ("Firmware age", self.check_bios_age),
            ("Uptime", self.check_uptime),
            ("Windows Update", self.check_updates),   # slowest last
        ]
        total = len(steps)
        for i, (name, fn) in enumerate(steps, 1):
            if cancel_event is not None and cancel_event.is_set():
                self.log.log("Scan cancelled", name, "WARN")
                break
            if progress_cb:
                progress_cb(name, (i - 1) / total)
            try:
                fn(findings, metrics)
            except Exception as e:            # one broken check never kills the scan
                self.log.log("Scan check failed", f"{name}: {e}", "ERROR")
                findings.append(dict(severity="Info", category=name,
                    title=f"Check '{name}' could not complete",
                    recommendation=f"Error: {e}"))
        if progress_cb:
            progress_cb("Finalizing", 1.0)

        findings.sort(key=lambda f: SEVERITY_ORDER.get(f["severity"], 9))
        score, breakdown = HealthScore.compute(metrics)
        self.log.log("Full scan completed",
                     f"score={score}, findings={len(findings)}")
        return findings, metrics, score, breakdown