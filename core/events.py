"""
WinCare Pro - Core event triage engine (v1.1).

Turns 'hundreds of Event Log errors' into a ranked, explained cause list.
Knowledge base encodes field-proven meanings for the noisy classics.
"""
from __future__ import annotations

from core.shell import ps_json, safe_ps


class EventTriage:
    # (provider substring, event id or None=any) -> (meaning, action, class)
    # class: "root" = real problem, "symptom" = downstream, "noise" = ignore
    KNOWN = [
        ("Kernel-Power", 41,
         "Unclean shutdown — the system froze, overheated, lost power, or was "
         "force-restarted. The machine died without saying goodbye.",
         "Disable Fast Startup (button below), update BIOS + chipset drivers "
         "from your PC vendor, check cooling.", "root"),
        ("Microsoft-Windows-HAL", 12,
         "Firmware corrupted memory across a power transition (sleep/resume). "
         "This is a BIOS/firmware fault, not an app problem.",
         "Update BIOS and Intel platform drivers from your PC vendor. "
         "Disable Fast Startup as mitigation.", "root"),
        ("EventLog", 6008,
         "'Previous shutdown was unexpected' — the bookkeeping twin of "
         "Kernel-Power 41. Same incident, second witness.",
         "Fix the Kernel-Power 41 cause; these disappear with it.", "symptom"),
        ("volmgr", 46,
         "Crash dump initialization failed — Windows cannot record WHY it "
         "crashes. Your flight recorder is broken.",
         "Use 'Repair crash-dump settings' below, then reboot.", "root"),
        ("Service Control Manager", 7009,
         "A service took longer than 45s to start. Chronic repeats from one "
         "service = broken vendor plumbing delaying your boot.",
         "Select the service in the lower table and demote it to Manual "
         "start.", "root"),
        ("Service Control Manager", 7000,
         "A service failed to start entirely.",
         "If it repeats for one service: reinstall that software or demote "
         "the service.", "root"),
        ("Service Control Manager", 7031,
         "A service crashed and was restarted by Windows.",
         "Repeats from one service = update or remove that software.", "root"),
        ("Service Control Manager", 7034,
         "A service crashed and was NOT restarted.",
         "Repeats from one service = update or remove that software.", "root"),
        ("Service Control Manager", None,
         "Service lifecycle errors (start failures, timeouts, crashes).",
         "Check the per-service breakdown in the lower table.", "root"),
        ("DistributedCOM", 10016,
         "DCOM permission mismatch. Famous, harmless registry noise — "
         "Microsoft's own guidance is to ignore it.",
         "No action. Do not chase registry 'fixes' for this.", "noise"),
        ("DistributedCOM", None,
         "DCOM activation errors — usually permission noise.",
         "Ignore unless an app is visibly failing.", "noise"),
        ("WindowsUpdateClient", None,
         "Windows Update install failures.",
         "Repairs tab > 'Reset Windows Update', then retry updates.", "root"),
        ("NetBT", None,
         "NetBIOS name/adapter noise — typically appears when adapters "
         "change or VPNs connect.",
         "Ignore unless file-sharing by computer name is broken.", "noise"),
        ("Volsnap", None,
         "Volume Shadow Copy (VSS) errors — System Restore and backups "
         "depend on this.",
         "If repeating: check disk health and free space; restore points "
         "may be failing silently.", "root"),
        ("TPM", None,
         "TPM attestation/maintenance noise.",
         "Ignore unless BitLocker or Windows Hello is failing.", "noise"),
        ("Kernel-EventTracing", None,
         "A diagnostic logging session failed to start — usually a leftover "
         "autologger from uninstalled software.",
         "Cosmetic. Open one event to see which session; uninstall its "
         "orphaned owner.", "noise"),
        ("DriverFrameworks", None,
         "A user-mode driver hung or failed (often USB devices).",
         "If repeating: reconnect/replace the device or update its driver.",
         "root"),
    ]

    @staticmethod
    def _lookup(provider: str, top_id):
        """Best KNOWN match: exact (substr, id) first, then (substr, None)."""
        for sub, eid, meaning, action, cls in EventTriage.KNOWN:
            if sub.lower() in provider.lower() and eid == top_id:
                return meaning, action, cls
        for sub, eid, meaning, action, cls in EventTriage.KNOWN:
            if sub.lower() in provider.lower() and eid is None:
                return meaning, action, cls
        return ("Not in the knowledge base yet.",
                "Open Event Viewer and read one instance; search "
                "'Event ID <id> <source>'.", "unknown")

    @staticmethod
    def collect(days: int = 7):
        """
        Query System log (Critical+Error, last N days) and return:
          {"providers": [{provider, count, top_id, meaning, action, cls}],
           "services":  [{name, count, ids}],
           "total": int, "days": int}
        """
        events = ps_json(
            "Get-WinEvent -FilterHashtable @{LogName='System';Level=1,2;"
            f"StartTime=(Get-Date).AddDays(-{days})}} -MaxEvents 3000 "
            "-ErrorAction SilentlyContinue | Select-Object ProviderName, Id "
            "| ConvertTo-Json -Compress", timeout=120)
        prov_counts, prov_ids = {}, {}
        for e in events:
            p = str(e.get("ProviderName") or "Unknown")
            i = e.get("Id")
            prov_counts[p] = prov_counts.get(p, 0) + 1
            prov_ids.setdefault(p, {})
            prov_ids[p][i] = prov_ids[p].get(i, 0) + 1
        providers = []
        for p, c in sorted(prov_counts.items(), key=lambda kv: -kv[1]):
            top_id = max(prov_ids[p], key=prov_ids[p].get)
            meaning, action, cls = EventTriage._lookup(p, top_id)
            providers.append({"provider": p, "count": c, "top_id": top_id,
                              "meaning": meaning, "action": action,
                              "cls": cls})
        # per-service breakdown for SCM events (name lives in the MESSAGE;
        # property slots vary by event id — parsing the message is reliable)
        services = EventTriage._scm_services(days)
        return {"providers": providers, "services": services,
                "total": len(events), "days": days}

    @staticmethod
    def _scm_services(days: int):
        import re
        msgs = ps_json(
            "Get-WinEvent -FilterHashtable @{LogName='System';"
            "ProviderName='Service Control Manager';Level=1,2;"
            f"StartTime=(Get-Date).AddDays(-{days})}} -MaxEvents 500 "
            "-ErrorAction SilentlyContinue | Select-Object Id, Message "
            "| ConvertTo-Json -Compress", timeout=90)
        agg = {}
        pat = re.compile(r"(?:waiting for the|The) (.+?) service", re.I)
        for m in msgs:
            match = pat.search(str(m.get("Message") or ""))
            if not match:
                continue
            name = match.group(1).strip()
            entry = agg.setdefault(name, {"count": 0, "ids": set()})
            entry["count"] += 1
            entry["ids"].add(m.get("Id"))
        return [{"name": n, "count": v["count"],
                 "ids": ", ".join(str(i) for i in sorted(v["ids"]))}
                for n, v in sorted(agg.items(), key=lambda kv: -kv[1]["count"])]

    @staticmethod
    def resolve_service_name(display_name: str):
        """SCM messages contain DISPLAY names; sc config needs the real
        service name. Resolve via PowerShell; fall back to the input."""
        rc, out = safe_ps(
            "(Get-Service -DisplayName $args[0] -ErrorAction "
            "SilentlyContinue | Select-Object -First 1).Name",
            display_name, timeout=30)
        name = out.strip().splitlines()[-1].strip() if out.strip() else ""
        return name or display_name
