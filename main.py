#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
================================================================================
 WinCare Pro - Windows 11 Maintenance, Repair & Optimization Suite
================================================================================
 Version : 1.3.0
 Target  : Windows 11 22H2 (build 22621) and later
 Stack   : Python 3.11+, CustomTkinter, psutil

 DESIGN PRINCIPLES
 -----------------
 * SAFETY FIRST  - every destructive action requires explicit confirmation,
                   uses built-in Windows tools (SFC / DISM / chkdsk / netsh),
                   and creates a System Restore Point beforehand (if enabled).
 * NO UI FREEZE  - every long-running operation runs on a worker thread and
                   streams output back to the UI through a thread-safe queue.
 * TRANSPARENCY  - every action is logged (JSON + human-readable text log)
                   and shown live in a console panel.
 * REVERSIBILITY - startup / service changes are backed up to JSON so they
                   can be restored from inside the app.

 ARCHITECTURE (construction analogy)
 -----------------------------------
   Foundation : AppLogger, SettingsManager          (persistence layer)
   Framing    : SysInfo, HealthScore, Scanner       (read-only inspection)
   Wiring     : RepairEngine, Optimizer, Cleaner    (controlled mutation)
   Finish     : WinCareApp (CustomTkinter GUI)      (presentation layer)
================================================================================
"""

import ctypes
import json
import os
import platform
import queue
import shutil
import subprocess
import sys
import threading
import time
import traceback
from datetime import datetime, timedelta
from pathlib import Path

import psutil

# GUI toolkit ---------------------------------------------------------------
import tkinter as tk
from tkinter import ttk

import customtkinter as ctk

# Additional Modules for Baseline Knowledge, AI Engine, Privacy, Bloat, and Performance
from win_baseline import WindowsBaselineAnalyzer
from ai_engine import WinCareAIEngine
from privacy_engine import PrivacyShield, privacy_protection_switches
from bloat_remover import BloatRemover
from performance_booster import PerformanceBooster
from licensing import LicenseManager
from disk_analyzer import DiskAnalyzer
from deep_uninstaller import DeepUninstaller
from file_cleaner import FileCleaner
from rollback_engine import RollbackEngine
from wincare_tray import WinCareTrayWorker
from auto_repair import AutoRepairEngine
from commerce import open_checkout
from updater import UpdateClient
from security_scanner import SecurityScanner

# winreg only exists on Windows; keep the module importable elsewhere so the
# file can be linted / unit-tested on non-Windows CI boxes.
try:
    import winreg
except ImportError:          # pragma: no cover - non-Windows environment
    winreg = None

IS_WINDOWS = (os.name == "nt")

# ============================================================================
# CONSTANTS & PATHS
# ============================================================================
APP_NAME = "WinCare Pro"
APP_VERSION = "1.3.0"

# All app data lives in %LOCALAPPDATA%\WinCarePro (never inside C:\Windows).
APP_DIR = Path(os.environ.get("LOCALAPPDATA", str(Path.home()))) / "WinCarePro"
LOG_DIR = APP_DIR / "logs"
REPORT_DIR = APP_DIR / "reports"
SETTINGS_FILE = APP_DIR / "settings.json"
BACKUP_FILE = APP_DIR / "change_backups.json"   # rollback info for startup/services

def initialize_app_storage():
    """Create runtime storage only when the application actually starts."""
    for directory in (APP_DIR, LOG_DIR, REPORT_DIR):
        directory.mkdir(parents=True, exist_ok=True)

# subprocess flag: never flash a console window behind the GUI.
CREATE_NO_WINDOW = 0x08000000 if IS_WINDOWS else 0

SEVERITY_ORDER = {"Critical": 0, "Warning": 1, "Info": 2, "OK": 3}
SEV_COLORS = {"Critical": "#E5484D", "Warning": "#F5A524", "Info": "#4A9EFF", "OK": "#2ECC71"}

ACCENT = "#1F5FC4"
CARD_BG = ("#EBEFF5", "#1B1F27")       # (light, dark)
CARD_BG2 = ("#E1E6EE", "#232936")

DISCLAIMER_TEXT = (
    f"Welcome to {APP_NAME} v{APP_VERSION}\n\n"
    "READ BEFORE USING\n"
    "--------------------------------------------------\n"
    "* This tool runs real Windows maintenance commands (SFC, DISM, chkdsk, "
    "netsh, powercfg) and can delete temporary files, change startup entries, "
    "services and power settings.\n\n"
    "* Every destructive action asks for confirmation first, and the app "
    "offers to create a System Restore Point before repairs. Still: no "
    "utility can guarantee zero risk. Keep backups of important data.\n\n"
    "* Some operations (chkdsk /f /r, network reset, Windows Update reset) "
    "require a REBOOT to complete and may take a long time.\n\n"
    "* Run the app as Administrator for full functionality.\n\n"
    "* You use this software at your own risk. It modifies only what you "
    "explicitly approve, and logs every action to:\n"
    f"  {LOG_DIR}\n\n"
    "By clicking \"I Understand and Accept\" you acknowledge the above."
)

# ============================================================================
# LOW-LEVEL COMMAND HELPERS
# ============================================================================

def is_admin() -> bool:
    """True when the current process has administrator privileges."""
    if not IS_WINDOWS:
        return False
    try:
        return bool(ctypes.windll.shell32.IsUserAnAdmin())
    except Exception:
        return False


def relaunch_as_admin() -> bool:
    """Re-launch this script/exe elevated via UAC. Returns True on success."""
    try:
        target = sys.executable
        if getattr(sys, "frozen", False):
            args = subprocess.list2cmdline(sys.argv[1:])
        else:
            args = subprocess.list2cmdline(
                [os.path.abspath(sys.argv[0])] + sys.argv[1:]
            )
        rc = ctypes.windll.shell32.ShellExecuteW(None, "runas", target, args, None, 1)
        return rc > 32
    except Exception:
        return False


from core.shell import (  # noqa: F401  (re-exported for existing callers)
    run_cmd, run_ps, safe_ps, stream_cmd, human_bytes, ps_json,
)


from core.logger import (  # noqa: F401  (re-exported for existing callers)
    AppLogger, SettingsManager, ChangeBackup,
)
from core.sysinfo import SysInfo  # noqa: F401
from core.health import HealthScore  # noqa: F401
from core.scanner import Scanner  # noqa: F401
from core.repair import RepairEngine  # noqa: F401
from core.optimizer import Optimizer  # noqa: F401
from core.cleaner import Cleaner  # noqa: F401
from core.storage import StorageAnalyzer  # noqa: F401
from core.report import ReportExporter  # noqa: F401
from core.events import EventTriage  # noqa: F401


# ============================================================================
# SCORE HISTORY (trend line for the dashboard)
# ============================================================================
SCORE_HISTORY_FILE = APP_DIR / "score_history.json"


def score_history_append(score: int):
    """Persist each scan's score so the dashboard can show a trend."""
    hist = score_history_load()
    hist.append({"ts": datetime.now().isoformat(timespec="seconds"), "score": score})
    try:
        SCORE_HISTORY_FILE.write_text(json.dumps(hist[-50:], indent=1), encoding="utf-8")
    except OSError:
        pass


def score_history_load():
    try:
        data = json.loads(SCORE_HISTORY_FILE.read_text(encoding="utf-8"))
        return data if isinstance(data, list) else []
    except (OSError, json.JSONDecodeError):
        return []



SCHED_TASK_NAME = "WinCarePro Weekly Scan Reminder"


def scheduled_task_exists() -> bool:
    rc, _ = run_cmd(["schtasks", "/Query", "/TN", SCHED_TASK_NAME], timeout=30)
    return rc == 0


def scheduled_task_create() -> bool:
    """Weekly Sunday 10:00 launch of this app for the current user."""
    if getattr(sys, "frozen", False):
        tr = f'"{sys.executable}"'
    else:
        tr = f'"{sys.executable}" "{os.path.abspath(sys.argv[0])}"'
    rc, _ = run_cmd(["schtasks", "/Create", "/F", "/TN", SCHED_TASK_NAME,
                     "/TR", tr, "/SC", "WEEKLY", "/D", "SUN", "/ST", "10:00"],
                    timeout=30)
    return rc == 0


def scheduled_task_delete() -> bool:
    rc, _ = run_cmd(["schtasks", "/Delete", "/F", "/TN", SCHED_TASK_NAME],
                    timeout=30)
    return rc == 0


# ============================================================================
# PROCESS UTILITIES (for the Process Manager tab)
# ============================================================================
# Killing these hard-crashes or destabilizes Windows. End Task is refused.
PROTECTED_PROCESSES = {
    "system", "system idle process", "registry", "memory compression",
    "csrss.exe", "wininit.exe", "winlogon.exe", "smss.exe", "services.exe",
    "lsass.exe", "fontdrvhost.exe", "dwm.exe", "sihost.exe",
}
# Killing these is allowed but gets an extra-scary warning.
RISKY_PROCESSES = {"svchost.exe", "explorer.exe", "ctfmon.exe", "audiodg.exe",
                   "searchindexer.exe", "runtimebroker.exe"}


def snapshot_processes(cpu_cache: dict):
    """
    One pass over all processes -> list of row dicts.
    cpu_cache maps pid -> psutil.Process, kept between refreshes so
    cpu_percent() deltas are meaningful.
    """
    rows, seen = [], set()
    for p in psutil.process_iter(["pid", "name", "memory_info", "exe", "username"]):
        try:
            pid = p.info["pid"]
            seen.add(pid)
            proc = cpu_cache.get(pid)
            if proc is None:
                proc = p
                cpu_cache[pid] = p
                proc.cpu_percent(None)          # prime the counter
                cpu = 0.0
            else:
                cpu = proc.cpu_percent(None) / max(1, psutil.cpu_count())
            mem = p.info["memory_info"].rss if p.info["memory_info"] else 0
            name = p.info["name"] or "?"
            exe = p.info["exe"] or ""
            suspicious = bool(exe) and ("\\temp\\" in exe.lower()
                                        or "\\appdata\\local\\temp" in exe.lower())
            rows.append({"pid": pid, "name": name, "cpu": round(cpu, 1),
                         "mem": mem, "path": exe, "suspicious": suspicious})
        except (psutil.NoSuchProcess, psutil.AccessDenied, psutil.ZombieProcess):
            continue
    for pid in list(cpu_cache):
        if pid not in seen:
            del cpu_cache[pid]
    return rows


def inspect_process_signature(exe_path: str) -> str:
    """Authenticode signature status for a given executable."""
    if not exe_path:
        return "No path available (access denied or system process)."
    rc, out = safe_ps(
        "(Get-AuthenticodeSignature -FilePath $args[0]).Status",
        exe_path, timeout=30)
    status = out.strip().splitlines()[-1] if out.strip() else "Unknown"
    verdict = {
        "Valid": "VALID signature - publisher verified.",
        "NotSigned": "NOT SIGNED - not proof of malware, but combined with a "
                     "Temp-folder path or high resource use, investigate.",
        "HashMismatch": "HASH MISMATCH - file was modified after signing. "
                        "Scan with Windows Security NOW.",
    }.get(status, f"Signature status: {status}")
    return verdict



# ============================================================================
# FINISH WORK: GUI HELPERS
# ============================================================================
class ConfirmDialog(ctk.CTkToplevel):
    """
    Modal confirmation dialog. Destructive actions use danger=True (red
    confirm button). Read .result after wait_window(): True = confirmed.
    """

    def __init__(self, master, title, message, confirm_text="Proceed",
                 danger=True):
        super().__init__(master)
        self.title(title)
        self.result = False
        self.resizable(False, False)
        self.transient(master)
        w, h = 520, 300
        x = master.winfo_x() + (master.winfo_width() - w) // 2
        y = master.winfo_y() + (master.winfo_height() - h) // 2
        self.geometry(f"{w}x{h}+{max(0, x)}+{max(0, y)}")

        ctk.CTkLabel(self, text=("⚠  " if danger else "ℹ  ") + title,
                     font=ctk.CTkFont(size=17, weight="bold")
                     ).pack(anchor="w", padx=24, pady=(20, 6))
        box = ctk.CTkTextbox(self, wrap="word", height=150,
                             font=ctk.CTkFont(size=13))
        box.pack(fill="both", expand=True, padx=24)
        box.insert("1.0", message)
        box.configure(state="disabled")
        row = ctk.CTkFrame(self, fg_color="transparent")
        row.pack(fill="x", padx=24, pady=16)
        ctk.CTkButton(row, text="Cancel", fg_color="gray35",
                      hover_color="gray25", width=120,
                      command=self._cancel).pack(side="right", padx=(8, 0))
        ctk.CTkButton(row, text=confirm_text, width=160,
                      fg_color="#C0392B" if danger else ACCENT,
                      hover_color="#96281B" if danger else "#1F5FC4",
                      command=self._ok).pack(side="right")
        self.protocol("WM_DELETE_WINDOW", self._cancel)
        self.after(120, self._make_modal)   # grab after window is viewable

    def _make_modal(self):
        try:
            self.grab_set()
            self.focus_force()
        except tk.TclError:
            pass

    def _ok(self):
        self.result = True
        self.destroy()

    def _cancel(self):
        self.result = False
        self.destroy()


class ConsolePanel(ctk.CTkFrame):
    """
    Reusable live-output console. Worker threads call .write(line) (thread
    safe - lines go through a queue drained on the Tk main loop).
    """

    def __init__(self, master, height=200, **kw):
        super().__init__(master, fg_color=CARD_BG, corner_radius=10, **kw)
        self.queue = queue.Queue()
        self.text = ctk.CTkTextbox(self, height=height, wrap="none",
                                   font=ctk.CTkFont(family="Consolas", size=12),
                                   fg_color=("#F3F5F9", "#12151C"))
        self.text.pack(fill="both", expand=True, padx=8, pady=8)
        self.text.configure(state="disabled")
        self._poll()

    def write(self, line: str):
        self.queue.put(str(line))

    def clear(self):
        self.text.configure(state="normal")
        self.text.delete("1.0", "end")
        self.text.configure(state="disabled")

    def _poll(self):
        drained = False
        try:
            self.text.configure(state="normal")
            while True:
                line = self.queue.get_nowait()
                stamp = datetime.now().strftime("%H:%M:%S")
                self.text.insert("end", f"[{stamp}] {line}\n")
                drained = True
        except queue.Empty:
            pass
        except tk.TclError:
            return                      # widget destroyed during shutdown
        finally:
            try:
                if drained:
                    self.text.see("end")
                self.text.configure(state="disabled")
            except tk.TclError:
                pass
        self.after(150, self._poll)


def styled_treeview(parent, columns, widths, stretch_col=None):
    """ttk.Treeview themed to match the dark UI. Returns (container, tree)."""
    frame = ctk.CTkFrame(parent, fg_color=CARD_BG, corner_radius=10)
    tree = ttk.Treeview(frame, columns=columns, show="headings",
                        selectmode="browse")
    vsb = ttk.Scrollbar(frame, orient="vertical", command=tree.yview)
    tree.configure(yscrollcommand=vsb.set)
    for col, width in zip(columns, widths):
        tree.heading(col, text=col)
        tree.column(col, width=width, anchor="w",
                    stretch=(col == stretch_col))
    tree.grid(row=0, column=0, sticky="nsew", padx=(8, 0), pady=8)
    vsb.grid(row=0, column=1, sticky="ns", pady=8, padx=(0, 6))
    frame.grid_rowconfigure(0, weight=1)
    frame.grid_columnconfigure(0, weight=1)
    return frame, tree


def sort_treeview(tree, col, numeric=False):
    """Click-to-sort helper for Treeview columns (toggles asc/desc)."""
    data = [(tree.set(item, col), item) for item in tree.get_children("")]
    reverse = getattr(tree, "_sort_state", {}).get(col, False)
    if numeric:
        def keyfn(v):
            try:
                parts = str(v[0]).replace("%", "").replace(",", "").split()
                value = float(parts[0])
                if len(parts) > 1:
                    value *= {
                        "B": 1, "KB": 1024, "MB": 1024**2,
                        "GB": 1024**3, "TB": 1024**4,
                    }.get(parts[1].upper(), 1)
                return value
            except ValueError:
                return -1.0
        data.sort(key=keyfn, reverse=not reverse)
    else:
        data.sort(key=lambda v: str(v[0]).lower(), reverse=not reverse)
    for idx, (_, item) in enumerate(data):
        tree.move(item, "", idx)
    if not hasattr(tree, "_sort_state"):
        tree._sort_state = {}
    tree._sort_state[col] = not reverse


# ============================================================================
# EVENT TRIAGE WINDOW (v1.1)
# ============================================================================
class TriageWindow(ctk.CTkToplevel):
    """
    One-click version of a manual Event Log investigation:
    rank error sources, decode the known ones, name the failing services,
    and offer the safe fixes (Fast Startup, crash-dump repair, service
    demotion) with the same confirm-first discipline as everything else.
    """

    def __init__(self, app):
        super().__init__(app)
        self.app = app
        self.title("Event Triage — last 7 days of System errors")
        self.geometry("1080x680")
        self.minsize(900, 560)

        head = ctk.CTkFrame(self, fg_color="transparent")
        head.pack(fill="x", padx=14, pady=(12, 4))
        ctk.CTkLabel(head, text="Event Triage",
                     font=ctk.CTkFont(size=20, weight="bold")).pack(side="left")
        self.status = ctk.CTkLabel(head, text="Analyzing last 7 days…",
                                   text_color="gray55")
        self.status.pack(side="left", padx=12)
        ctk.CTkButton(head, text="↻ Re-analyze", width=110,
                      command=self._analyze).pack(side="right")

        ctk.CTkLabel(self, text="ERROR SOURCES (ranked — fix roots, ignore noise)",
                     text_color="gray55", font=ctk.CTkFont(size=11, weight="bold")
                     ).pack(anchor="w", padx=16)
        cols = ("Count", "Source", "Top ID", "Verdict", "What it means")
        frame, self.prov_tree = styled_treeview(
            self, cols, (60, 210, 60, 80, 560), stretch_col="What it means")
        frame.pack(fill="both", expand=True, padx=14, pady=(2, 6))
        self.prov_tree.tag_configure("root", foreground="#E5484D")
        self.prov_tree.tag_configure("symptom", foreground="#F5A524")
        self.prov_tree.tag_configure("noise", foreground="gray55")
        self.prov_tree.tag_configure("unknown", foreground="#4A9EFF")

        ctk.CTkLabel(self, text="FAILING / SLOW SERVICES (from Service Control Manager events)",
                     text_color="gray55", font=ctk.CTkFont(size=11, weight="bold")
                     ).pack(anchor="w", padx=16)
        cols2 = ("Count", "Service", "Event IDs")
        frame2, self.svc_tree = styled_treeview(
            self, cols2, (60, 520, 160), stretch_col="Service")
        frame2.pack(fill="x", padx=14, pady=(2, 6))

        btns = ctk.CTkFrame(self, fg_color="transparent")
        btns.pack(fill="x", padx=14, pady=(2, 12))
        ctk.CTkButton(btns, text="⚡ Disable Fast Startup",
                      command=self._fix_fast_startup).pack(side="left")
        ctk.CTkButton(btns, text="📼 Repair crash-dump settings",
                      command=self._fix_dumps).pack(side="left", padx=6)
        ctk.CTkButton(btns, text="🕑 Demote selected service to Manual",
                      command=self._demote_service).pack(side="left")
        ctk.CTkButton(btns, text="🔍 Explain selected source",
                      fg_color="gray35", hover_color="gray25",
                      command=self._explain).pack(side="left", padx=6)
        ctk.CTkButton(btns, text="🗑 Open Installed Apps",
                      fg_color="gray35", hover_color="gray25",
                      command=lambda: self.app._open_tool("appwiz.cpl")
                      ).pack(side="left")

        self._rows, self._svc_rows = [], []
        self.after(200, self._analyze)

    # ---- data ---------------------------------------------------------
    def _analyze(self):
        self.status.configure(text="Analyzing last 7 days…")

        def work():
            return EventTriage.collect(days=7)

        def done(res):
            if isinstance(res, Exception):
                self.status.configure(text=f"Analysis failed: {res}")
                return
            self._rows = res["providers"]
            self._svc_rows = res["services"]
            for t in (self.prov_tree, self.svc_tree):
                for i in t.get_children():
                    t.delete(i)
            verdict = {"root": "FIX", "symptom": "FOLLOWS", "noise": "IGNORE",
                       "unknown": "RESEARCH"}
            for idx, r in enumerate(self._rows):
                self.prov_tree.insert(
                    "", "end", iid=str(idx),
                    values=(r["count"], r["provider"], r["top_id"],
                            verdict.get(r["cls"], "?"), r["meaning"]),
                    tags=(r["cls"],))
            for idx, s in enumerate(self._svc_rows):
                self.svc_tree.insert("", "end", iid=str(idx),
                                     values=(s["count"], s["name"], s["ids"]))
            roots = sum(1 for r in self._rows if r["cls"] == "root")
            self.status.configure(
                text=f"{res['total']} errors · {len(self._rows)} sources · "
                     f"{roots} root cause(s) — the rest is symptom/noise")
            self.app.logger.log("Event triage analyzed",
                                f"{res['total']} events, {len(self._rows)} sources")
        self.app.run_bg(work, done)

    # ---- actions --------------------------------------------------------
    def _explain(self):
        sel = self.prov_tree.selection()
        if not sel:
            self.app.notify("No selection", "Select an error source first.")
            return
        r = self._rows[int(sel[0])]
        self.app.notify(
            f"{r['provider']} (top Event ID {r['top_id']})",
            f"{r['count']} occurrences in 7 days.\n\n"
            f"MEANING\n{r['meaning']}\n\nRECOMMENDED ACTION\n{r['action']}")

    def _fix_fast_startup(self):
        if not self.app.admin:
            self.app.notify("Administrator required",
                            "This change needs admin rights.")
            return
        state = Optimizer.fast_startup_enabled()
        if state is False:
            self.app.notify("Already done", "Fast Startup is already disabled.")
            return
        if not self.app.confirm(
                "Disable Fast Startup",
                "Fast Startup (hybrid boot) is the most common software "
                "cause of Kernel-Power 41 / HAL power-transition faults.\n\n"
                "Disabling it: shutdowns become full shutdowns, boots take a "
                "few seconds longer, sleep/crash issues usually stop.\n\n"
                "Reversible anytime via Settings > Undo Center.",
                confirm_text="Disable Fast Startup", danger=False):
            return
        ok, msg = self.app.optimizer.set_fast_startup(False)
        self.app.notify("Fast Startup" if ok else "Failed", msg)

    def _fix_dumps(self):
        if not self.app.admin:
            self.app.notify("Administrator required",
                            "This change needs admin rights.")
            return
        if not self.app.confirm(
                "Repair crash-dump settings",
                "Sets crash recording to 'Automatic memory dump' and the "
                "pagefile to system-managed, so the NEXT crash leaves "
                "evidence that can actually be diagnosed.\n\n"
                "A reboot is required to fully apply.",
                confirm_text="Repair", danger=False):
            return
        lines = []

        def work():
            return self.app.repair.repair_crash_dump(lines.append)

        def done(rc):
            self.app.notify("Crash-dump repair",
                            "\n".join(lines) if lines else "Done.")
        self.app.run_bg(work, done)

    def _demote_service(self):
        sel = self.svc_tree.selection()
        if not sel:
            self.app.notify("No selection",
                            "Select a service in the lower table first.")
            return
        if not self.app.admin:
            self.app.notify("Administrator required",
                            "This change needs admin rights.")
            return
        s = self._svc_rows[int(sel[0])]
        display = s["name"]
        if not self.app.confirm(
                "Demote service to Manual start",
                f"Service: {display}\n{s['count']} error events "
                f"(IDs: {s['ids']}) in 7 days.\n\n"
                "Manual start = Windows launches it only when something "
                "asks for it, instead of blocking every boot for 45s.\n\n"
                "The original start type is backed up (Settings > Undo "
                "Center). Core Windows services will refuse this change.",
                confirm_text="Demote to Manual", danger=True):
            return

        def work():
            real = EventTriage.resolve_service_name(display)
            return self.app.optimizer.set_service_start_type(real, "demand")

        def done(res):
            if isinstance(res, Exception):
                self.app.notify("Failed", str(res))
                return
            ok, msg = res
            self.app.notify("Service demoted" if ok else "Failed", msg)
        self.app.run_bg(work, done)


# ============================================================================
# FINISH WORK: MAIN APPLICATION WINDOW
# ============================================================================
class WinCareApp(ctk.CTk):
    TABS = ["Dashboard", "AI Advisor", "Privacy Shield", "Bloatware Remover",
            "RAM & Network", "Bloat & Baseline", "Diagnostics", "Repairs",
            "Optimize", "Processes & Cleanup", "Maintenance", "Settings"]

    def __init__(self):
        super().__init__()
        # ---- backend wiring ------------------------------------------------
        initialize_app_storage()
        self.settings = SettingsManager()
        self.logger = AppLogger(self.settings.get("log_retention_days"))
        self.backup = ChangeBackup()
        self.scanner = Scanner(self.logger)
        self.repair = RepairEngine(self.logger)
        self.optimizer = Optimizer(self.logger, self.backup)
        self.cleaner = Cleaner(self.logger, self.settings)
        self.baseline = WindowsBaselineAnalyzer(self.logger)
        self.ai_engine = WinCareAIEngine()
        self.auto_repair_engine = AutoRepairEngine()
        self.privacy = PrivacyShield()
        self.bloat_remover = BloatRemover()
        self.booster = PerformanceBooster()
        self.license_mgr = LicenseManager()
        self.update_client = UpdateClient()
        self.disk_analyzer = DiskAnalyzer()
        self.deep_uninstaller = DeepUninstaller()
        self.file_cleaner = FileCleaner()
        self.rollback_engine = RollbackEngine()
        self.security_scanner = SecurityScanner(self.logger)
        self.tray_worker = WinCareTrayWorker()
        self.admin = is_admin()

        # ---- state ----------------------------------------------------------
        self._busy_op = None            # name of running heavy operation
        self._cancel_event = threading.Event()
        self._cpu_cache = {}            # pid -> Process for cpu deltas
        self.last_findings, self.last_metrics = [], {}
        self.last_score, self.last_breakdown = None, []

        # ---- window ----------------------------------------------------------
        ctk.set_appearance_mode(self.settings.get("theme", "Dark"))
        ctk.set_default_color_theme("blue")
        self.title(f"{APP_NAME} v{APP_VERSION}"
                   + ("  —  Administrator" if self.admin else "  —  LIMITED (not admin)"))
        self.geometry("1240x780")
        self.minsize(1080, 680)
        self.protocol("WM_DELETE_WINDOW", self._on_close)

        self._style_ttk()
        self._build_sidebar()
        self.container = ctk.CTkFrame(self, fg_color="transparent")
        self.container.pack(side="left", fill="both", expand=True,
                            padx=(0, 12), pady=12)

        self.frames = {}
        builders = {
            "Dashboard": self._build_dashboard,
            "AI Advisor": self._build_ai_advisor,
            "Privacy Shield": self._build_privacy_shield,
            "Bloatware Remover": self._build_bloatware_remover,
            "RAM & Network": self._build_ram_network,
            "Bloat & Baseline": self._build_bloat_baseline,
            "Diagnostics": self._build_diagnostics,
            "Repairs": self._build_repairs,
            "Optimize": self._build_optimize,
            "Processes & Cleanup": self._build_processes,
            "Maintenance": self._build_maintenance,
            "Settings": self._build_settings,
        }
        for name in self.TABS:
            frame = ctk.CTkFrame(self.container, fg_color="transparent")
            self.frames[name] = frame
            builders[name](frame)

        self.select_tab("Dashboard")
        self.logger.log("Application started",
                        f"v{APP_VERSION} admin={self.admin}")
        self._tick_gauges()
        self.after(400, self._startup_checks)

    # ------------------------------------------------------------------ shell
    def _style_ttk(self):
        """Blend ttk.Treeview into the CustomTkinter dark theme."""
        style = ttk.Style(self)
        style.theme_use("clam")
        style.configure("Treeview", background="#161A22",
                        fieldbackground="#161A22", foreground="#DDE3EC",
                        rowheight=26, borderwidth=0, font=("Segoe UI", 10))
        style.configure("Treeview.Heading", background="#232936",
                        foreground="#AAB4C4", borderwidth=0,
                        font=("Segoe UI", 10, "bold"))
        style.map("Treeview", background=[("selected", "#2E7CF6")],
                  foreground=[("selected", "#FFFFFF")])
        style.configure("Vertical.TScrollbar", background="#232936",
                        troughcolor="#161A22", borderwidth=0)

    def _build_sidebar(self):
        bar = ctk.CTkFrame(self, width=210, corner_radius=0,
                           fg_color=("#DDE3EC", "#141821"))
        bar.pack(side="left", fill="y")
        bar.pack_propagate(False)
        ctk.CTkLabel(bar, text="🛡  WinCare Pro",
                     font=ctk.CTkFont(size=20, weight="bold")
                     ).pack(pady=(24, 2), padx=16, anchor="w")
        ctk.CTkLabel(bar, text=f"v{APP_VERSION}", text_color="gray55",
                     font=ctk.CTkFont(size=11)).pack(padx=18, anchor="w")
        self.nav_buttons = {}
        icons = {"Dashboard": "⌂", "AI Advisor": "🤖", "Privacy Shield": "🔒",
                 "Bloatware Remover": "🗑", "RAM & Network": "⚡",
                 "Bloat & Baseline": "🛡", "Diagnostics": "🔍", "Repairs": "🔧",
                 "Optimize": "🚀", "Processes & Cleanup": "🧹",
                 "Maintenance": "🗓", "Settings": "⚙"}
        pad = ctk.CTkFrame(bar, fg_color="transparent")
        pad.pack(fill="x", pady=18)
        for name in self.TABS:
            b = ctk.CTkButton(pad, text=f"  {icons[name]}  {name}",
                              anchor="w", height=40, corner_radius=8,
                              fg_color="transparent",
                              text_color=("gray20", "#C9D1DE"),
                              hover_color=("#C9D2E0", "#222938"),
                              command=lambda n=name: self.select_tab(n))
            b.pack(fill="x", padx=10, pady=2)
            self.nav_buttons[name] = b
        # admin badge
        badge = ctk.CTkFrame(bar, fg_color=CARD_BG, corner_radius=8)
        badge.pack(side="bottom", fill="x", padx=12, pady=14)
        txt = ("● Administrator" if self.admin else "● Limited mode")
        col = "#2ECC71" if self.admin else "#F5A524"
        ctk.CTkLabel(badge, text=txt, text_color=col,
                     font=ctk.CTkFont(size=12, weight="bold")
                     ).pack(padx=10, pady=(8, 0), anchor="w")
        ctk.CTkLabel(badge, text="Full functionality available." if self.admin
                     else "Restart as admin for\nrepairs & deep cleaning.",
                     text_color="gray55", justify="left",
                     font=ctk.CTkFont(size=11)).pack(padx=10, pady=(0, 8), anchor="w")
        if not self.admin:
            ctk.CTkButton(badge, text="Restart as Admin", height=28,
                          command=self._elevate).pack(padx=10, pady=(0, 10), fill="x")

    def select_tab(self, name):
        for n, f in self.frames.items():
            f.pack_forget()
        self.frames[name].pack(fill="both", expand=True)
        for n, b in self.nav_buttons.items():
            b.configure(fg_color=("#C9D2E0", "#222938") if n == name
                        else "transparent")
        if name == "Processes & Cleanup":
            self._refresh_processes()

    # ------------------------------------------------------------- utilities
    def confirm(self, title, message, confirm_text="Proceed", danger=True):
        dlg = ConfirmDialog(self, title, message, confirm_text, danger)
        self.wait_window(dlg)
        return dlg.result

    def confirm_changes(self, title, changes, *, reversible=False,
                        confirm_text="Apply changes", danger=True):
        """Show and log the exact planned mutations before execution."""
        message = (
            "PLANNED SYSTEM CHANGES\n\n  • " + "\n  • ".join(changes)
            + "\n\nRollback: "
            + ("available" if reversible else "not automatically available")
        )
        approved = self.confirm(
            title, message, confirm_text=confirm_text, danger=danger)
        self.logger.log(
            "System change preview",
            f"{title}: {'approved' if approved else 'cancelled'}; "
            + " | ".join(changes))
        return approved

    def notify(self, title, message):
        """Non-blocking info dialog."""
        dlg = ConfirmDialog(self, title, message, confirm_text="OK", danger=False)
        self.wait_window(dlg)

    def begin_op(self, name) -> bool:
        """Serialize heavy operations - refuse to start two at once."""
        if self._busy_op:
            self.notify("Operation in progress",
                        f"'{self._busy_op}' is still running.\n\n"
                        "Wait for it to finish before starting another task.")
            return False
        self._busy_op = name
        self._cancel_event.clear()
        return True

    def end_op(self):
        self._busy_op = None

    def run_bg(self, fn, done=None):
        """Run fn() on a worker thread; call done(result) on the UI thread."""
        def wrap():
            try:
                res = fn()
            except Exception as e:                       # never kill the app
                self.logger.log("Background task crashed",
                                f"{e}\n{traceback.format_exc()}", "ERROR")
                res = e
            if done:
                try:
                    if self.winfo_exists():
                        self.after(0, lambda: done(res))
                except Exception:
                    pass
        threading.Thread(target=wrap, daemon=True).start()

    def _elevate(self):
        if relaunch_as_admin():
            self._on_close()
        else:
            self.notify("Elevation failed",
                        "Could not relaunch elevated. Right-click the app and "
                        "choose 'Run as administrator' instead.")

    def _on_close(self):
        self.logger.log("Application closed")
        self.destroy()

    def _startup_checks(self):
        """First-launch disclaimer + Windows version + scan reminder."""
        if not self.settings.get("accepted_disclaimer"):
            ok = self.confirm("Read before using", DISCLAIMER_TEXT,
                              confirm_text="I Understand and Accept",
                              danger=False)
            if not ok:
                self._on_close()
                return
            self.settings.set("accepted_disclaimer", True)
            self.logger.log("Disclaimer accepted")
        build = SysInfo.build_number()
        if IS_WINDOWS and 0 < build < 22621:
            self.notify("Windows version notice",
                        f"This tool targets Windows 11 22H2+ (build 22621). "
                        f"Detected build {build}. Most features will still "
                        "work, but they are untested on this version.")

    # =================================================================
    # TAB: DASHBOARD
    # =================================================================
    def _build_dashboard(self, root):
        root.grid_columnconfigure((0, 1), weight=1, uniform="dash")
        root.grid_rowconfigure(2, weight=1)

        # -- reminder banner (shown only when a scan is overdue) --------------
        self.banner = ctk.CTkFrame(root, fg_color="#7A5A16", corner_radius=8)
        self.banner_label = ctk.CTkLabel(self.banner, text="",
                                         font=ctk.CTkFont(size=12, weight="bold"))
        self.banner_label.pack(side="left", padx=12, pady=6)
        ctk.CTkButton(self.banner, text="Scan now", width=90, height=24,
                      command=lambda: (self.select_tab("Diagnostics"),
                                       self.start_scan())
                      ).pack(side="right", padx=10, pady=6)
        self._update_banner()

        # -- health score card --------------------------------------------------
        score_card = ctk.CTkFrame(root, fg_color=CARD_BG, corner_radius=12)
        score_card.grid(row=1, column=0, sticky="nsew", padx=(0, 8), pady=8)
        ctk.CTkLabel(score_card, text="SYSTEM HEALTH SCORE",
                     text_color="gray55", font=ctk.CTkFont(size=12, weight="bold")
                     ).pack(anchor="w", padx=18, pady=(14, 0))
        self.score_label = ctk.CTkLabel(score_card, text="—",
                                        font=ctk.CTkFont(size=64, weight="bold"))
        self.score_label.pack(pady=(4, 0))
        self.grade_label = ctk.CTkLabel(score_card, text="Run a scan to rate this system",
                                        text_color="gray55")
        self.grade_label.pack()
        self.trend_label = ctk.CTkLabel(score_card, text="",
                                        text_color="gray65",
                                        font=ctk.CTkFont(size=12))
        self.trend_label.pack(pady=(2, 10))
        self._update_trend_label()

        # -- system info card ------------------------------------------------------
        info_card = ctk.CTkFrame(root, fg_color=CARD_BG, corner_radius=12)
        info_card.grid(row=1, column=1, sticky="nsew", padx=(8, 0), pady=8)
        ctk.CTkLabel(info_card, text="SYSTEM INFORMATION", text_color="gray55",
                     font=ctk.CTkFont(size=12, weight="bold")
                     ).pack(anchor="w", padx=18, pady=(14, 6))
        self.info_labels = {}
        info = SysInfo.summary()
        rows = [("OS", info["os"]), ("CPU", info["cpu"]),
                ("Cores", info["cores"]),
                ("RAM", f"{info['ram_total']} ({info['ram_used_pct']}% used)"),
                ("Disk", f"{info['disk_free']} free of {info['disk_total']}"),
                ("Uptime", f"{info['uptime']} (since {info['boot_time']})")]
        for label, value in rows:
            row = ctk.CTkFrame(info_card, fg_color="transparent")
            row.pack(fill="x", padx=18, pady=1)
            ctk.CTkLabel(row, text=label, width=60, anchor="w",
                         text_color="gray55",
                         font=ctk.CTkFont(size=12)).pack(side="left")
            lab = ctk.CTkLabel(row, text=value, anchor="w",
                               font=ctk.CTkFont(size=12))
            lab.pack(side="left", fill="x", expand=True)
            self.info_labels[label] = lab

        # -- gauges + quick actions --------------------------------------------------
        bottom = ctk.CTkFrame(root, fg_color="transparent")
        bottom.grid(row=2, column=0, columnspan=2, sticky="nsew", pady=(8, 0))
        bottom.grid_columnconfigure(0, weight=3)
        bottom.grid_columnconfigure(1, weight=2)
        bottom.grid_rowconfigure(0, weight=1)

        gauge_card = ctk.CTkFrame(bottom, fg_color=CARD_BG, corner_radius=12)
        gauge_card.grid(row=0, column=0, sticky="nsew", padx=(0, 8))
        ctk.CTkLabel(gauge_card, text="LIVE RESOURCE USAGE", text_color="gray55",
                     font=ctk.CTkFont(size=12, weight="bold")
                     ).pack(anchor="w", padx=18, pady=(14, 10))
        self.gauges = {}
        for key in ("CPU", "RAM", "Disk"):
            row = ctk.CTkFrame(gauge_card, fg_color="transparent")
            row.pack(fill="x", padx=18, pady=8)
            ctk.CTkLabel(row, text=key, width=46, anchor="w",
                         font=ctk.CTkFont(size=13, weight="bold")).pack(side="left")
            bar = ctk.CTkProgressBar(row, height=14)
            bar.pack(side="left", fill="x", expand=True, padx=10)
            bar.set(0)
            val = ctk.CTkLabel(row, text="0%", width=52, anchor="e",
                               font=ctk.CTkFont(size=13))
            val.pack(side="left")
            self.gauges[key] = (bar, val)

        qa_card = ctk.CTkFrame(bottom, fg_color=CARD_BG, corner_radius=12)
        qa_card.grid(row=0, column=1, sticky="nsew", padx=(8, 0))
        ctk.CTkLabel(qa_card, text="QUICK ACTIONS", text_color="gray55",
                     font=ctk.CTkFont(size=12, weight="bold")
                     ).pack(anchor="w", padx=18, pady=(14, 10))
        ctk.CTkButton(qa_card, text="🔍  Full System Scan", height=44,
                      font=ctk.CTkFont(size=14, weight="bold"),
                      command=lambda: (self.select_tab("Diagnostics"),
                                       self.start_scan())
                      ).pack(fill="x", padx=18, pady=6)
        ctk.CTkButton(qa_card, text="⚡  One-Click Optimize", height=44,
                      fg_color="#1B6B49", hover_color="#145238",
                      font=ctk.CTkFont(size=14, weight="bold"),
                      command=self.one_click_optimize
                      ).pack(fill="x", padx=18, pady=6)
        ctk.CTkButton(qa_card, text="🛟  Create Restore Point", height=44,
                      fg_color="#6C5CE7", hover_color="#5546C8",
                      font=ctk.CTkFont(size=14, weight="bold"),
                      command=self.create_restore_point_clicked
                      ).pack(fill="x", padx=18, pady=6)

    def _update_banner(self):
        """Show the scheduled-scan reminder when the last scan is overdue."""
        last = self.settings.get("last_scan")
        interval = self.settings.get("scan_interval_days", 7)
        overdue, msg = False, ""
        if not last:
            overdue, msg = True, "No system scan on record — run your first Full System Scan."
        else:
            try:
                age = (datetime.now() - datetime.fromisoformat(last)).days
                if age >= interval:
                    overdue, msg = True, f"Last scan was {age} days ago (reminder set to every {interval} days)."
            except ValueError:
                pass
        if overdue:
            self.banner_label.configure(text="⏰  " + msg)
            self.banner.grid(row=0, column=0, columnspan=2, sticky="ew", pady=(0, 4))
        else:
            self.banner.grid_forget()

    def _update_trend_label(self):
        hist = score_history_load()
        if len(hist) >= 2:
            seq = " → ".join(str(h["score"]) for h in hist[-5:])
            self.trend_label.configure(text=f"Trend: {seq}")
        elif hist:
            self.trend_label.configure(text="Trend appears after your next scan.")

    def _tick_gauges(self):
        """2-second live gauge refresh; cheap non-blocking psutil calls."""
        try:
            cpu = psutil.cpu_percent(None)
            ram = psutil.virtual_memory().percent
            try:
                disk = psutil.disk_usage(
                    os.environ.get("SystemDrive", "C:") + "\\").percent
            except OSError:
                disk = 0
            for key, value in (("CPU", cpu), ("RAM", ram), ("Disk", disk)):
                bar, lab = self.gauges[key]
                bar.set(value / 100)
                bar.configure(progress_color=(
                    "#E5484D" if value > 90 else
                    "#F5A524" if value > 75 else ACCENT))
                lab.configure(text=f"{value:.0f}%")
        except Exception:
            pass
        self.after(2000, self._tick_gauges)

    def _apply_score_to_dashboard(self):
        if self.last_score is None:
            return
        score = self.last_score
        color = ("#2ECC71" if score >= 75 else
                 "#F5A524" if score >= 50 else "#E5484D")
        self.score_label.configure(text=str(score), text_color=color)
        self.grade_label.configure(
            text=f"{HealthScore.grade(score)} — {len(self.last_findings)} findings")
        self._update_trend_label()
        self._update_banner()

    # =================================================================
    # TAB: AI ADVISOR
    # =================================================================
    def _build_ai_advisor(self, root):
        ctk.CTkLabel(root, text="AI Advisor & Intelligence Center",
                     font=ctk.CTkFont(size=22, weight="bold")).pack(anchor="w")
        ctk.CTkLabel(root, text="AI-driven diagnostic review, process intelligence, and software keep-up-to-date assistant.",
                     text_color="gray55", font=ctk.CTkFont(size=12)).pack(anchor="w", pady=(2, 10))

        # Action bar
        bar = ctk.CTkFrame(root, fg_color=CARD_BG, corner_radius=10)
        bar.pack(fill="x", pady=(0, 10))

        row1 = ctk.CTkFrame(bar, fg_color="transparent")
        row1.pack(fill="x", padx=14, pady=10)

        ctk.CTkButton(row1, text="🤖 Run Full AI System Audit", width=180, height=32,
                      fg_color="#1F5FC4", hover_color="#174A98",
                      command=self._run_ai_audit).pack(side="left", padx=(0, 8))

        ctk.CTkButton(row1, text="🛠 1-Click AI Auto-Repair All", width=200, height=32,
                      fg_color="#C0392B", hover_color="#96281B",
                      command=self._execute_ai_auto_repair).pack(side="left", padx=8)

        ctk.CTkButton(row1, text="📦 Scan Software Updates (winget)", width=220, height=32,
                      fg_color="gray30", hover_color="gray25",
                      command=self._scan_ai_winget).pack(side="left", padx=8)

        ctk.CTkButton(row1, text="⚡ Upgrade All Outdated Software", width=220, height=32,
                      fg_color="#1B6B49", hover_color="#145238",
                      command=self.winget_upgrade_all).pack(side="left", padx=8)

        # AI Status Banner
        ollama_on = self.ai_engine.is_ollama_available()
        status_txt = "● Active Engine: Local Ollama LLM (gemma3:1b)" if ollama_on else "● Active Engine: WinCare Heuristic AI Engine (Offline)"
        status_col = "#2ECC71" if ollama_on else "#4A9EFF"
        ctk.CTkLabel(row1, text=status_txt, text_color=status_col,
                     font=ctk.CTkFont(size=12, weight="bold")).pack(side="right")

        # Split frame for AI Output & Chat
        split = ctk.CTkFrame(root, fg_color="transparent")
        split.pack(fill="both", expand=True)
        split.grid_columnconfigure((0, 1), weight=1, uniform="ai_split")
        split.grid_rowconfigure(0, weight=1)

        # Left Column: Audit Report
        left_card = ctk.CTkFrame(split, fg_color=CARD_BG, corner_radius=10)
        left_card.grid(row=0, column=0, sticky="nsew", padx=(0, 5))
        ctk.CTkLabel(left_card, text="AI DIAGNOSTIC & OPTIMIZATION REPORT", text_color="gray55",
                     font=ctk.CTkFont(size=12, weight="bold")).pack(anchor="w", padx=14, pady=(10, 4))

        self.ai_audit_text = ctk.CTkTextbox(left_card, wrap="word", font=ctk.CTkFont(size=12))
        self.ai_audit_text.pack(fill="both", expand=True, padx=12, pady=(0, 12))
        self.ai_audit_text.insert("1.0", "Click 'Run Full AI System Audit' above to generate an intelligent system review and optimization plan.")

        # Right Column: AI Assistant Chat & Software Update Center
        right_card = ctk.CTkFrame(split, fg_color=CARD_BG, corner_radius=10)
        right_card.grid(row=0, column=1, sticky="nsew", padx=(5, 0))
        ctk.CTkLabel(right_card, text="ASK AI ASSISTANT / SOFTWARE UPDATE CENTER", text_color="gray55",
                     font=ctk.CTkFont(size=12, weight="bold")).pack(anchor="w", padx=14, pady=(10, 4))

        self.ai_chat_text = ctk.CTkTextbox(right_card, wrap="word", font=ctk.CTkFont(size=12))
        self.ai_chat_text.pack(fill="both", expand=True, padx=12, pady=(0, 6))
        self.ai_chat_text.insert("1.0", "WinCare AI Assistant ready. Ask any question about Windows 11 performance, services, or software updates below.\n\n")

        chat_input_frame = ctk.CTkFrame(right_card, fg_color="transparent")
        chat_input_frame.pack(fill="x", padx=12, pady=(0, 12))
        self.ai_prompt_entry = ctk.CTkEntry(chat_input_frame, placeholder_text="Ask WinCare AI (e.g. 'How do I stop background print spooler?')...")
        self.ai_prompt_entry.pack(side="left", fill="x", expand=True, padx=(0, 6))
        self.ai_prompt_entry.bind("<Return>", lambda e: self._ask_ai_submit())
        ctk.CTkButton(chat_input_frame, text="Ask AI", width=80, command=self._ask_ai_submit).pack(side="right")

    def _run_ai_audit(self):
        if not self.begin_op("AI System Audit"):
            return
        self.ai_audit_text.delete("1.0", "end")
        self.ai_audit_text.insert("1.0", "Analyzing system metrics, event logs, and background processes...\n\n")

        def work():
            diagnostics, metrics, score, breakdown = self.scanner.run_full_scan()
            sys_data = SysInfo.summary()
            processes = self.baseline.scan_processes()
            report = self.ai_engine.analyze_system(sys_data, score, diagnostics, processes)
            return diagnostics, metrics, score, breakdown, report

        def done(result):
            self.end_op()
            if isinstance(result, Exception):
                self.ai_audit_text.insert("end", f"AI audit failed: {result}")
                return
            diagnostics, metrics, score, breakdown, report = result
            self.last_findings, self.last_metrics = diagnostics, metrics
            self.last_score, self.last_breakdown = score, breakdown
            self._apply_score_to_dashboard()
            self.ai_audit_text.delete("1.0", "end")
            self.ai_audit_text.insert("1.0", f"=== AI AUDIT REPORT ({report['ai_mode']}) ===\n\n")
            self.ai_audit_text.insert("end", f"SUMMARY & FINDINGS:\n{report['summary']}\n\n")
            self.ai_audit_text.insert("end", "RECOMMENDED OPTIMIZATIONS:\n")
            for rec in report['recommendations']:
                self.ai_audit_text.insert("end", f"  • {rec}\n")
            if report['quick_actions']:
                self.ai_audit_text.insert("end", f"\nSUGGESTED ACTIONS: {', '.join(report['quick_actions'])}\n")
            self.logger.log("AI System Audit completed")
        self.run_bg(work, done)

    def _execute_ai_auto_repair(self):
        if not self.last_findings:
            self.notify("Run an AI audit first",
                        "Auto-Repair only works from a real, current diagnostic scan. "
                        "Run 'Full AI System Audit' first so WinCare can plan fixes "
                        "from the problems it actually found.")
            return
        plan = self.auto_repair_engine.build_plan(self.last_findings)
        if not plan.safe_actions:
            self.notify("No safe automatic repairs",
                        "The current scan has no low-risk fixes that can be verified automatically. "
                        "The AI report will identify items that need targeted review instead.")
            return
        action_names = "\n".join(f"  • {item.title}" for item in plan.safe_actions)
        if not self.confirm(
                "Verified AI Auto-Repair",
                "This runs only low-risk fixes selected from your latest scan:\n\n"
                f"{action_names}\n\n"
                "It will NOT change DNS, TCP settings, privacy policies, services, "
                "startup entries, registry keys, or installed applications. Each action "
                "must pass a post-action check before WinCare reports it as fixed. Proceed?",
                confirm_text="Run verified repairs", danger=False):
            return
        if not self.begin_op("1-Click AI Auto-Repair All"):
            return
        self.ai_audit_text.delete("1.0", "end")
        self.ai_audit_text.insert("1.0", "🚀 INITIATING 1-CLICK AI AUTO-REPAIR & SYSTEM OPTIMIZATION...\n\n")

        def work():
            drive = os.environ.get("SystemDrive", "C:") + "\\"

            def cleanup_temp_files():
                before = psutil.disk_usage(drive).free
                keys = ["user_temp", "thumbs"] + (["win_temp"] if self.admin else [])
                reclaimed = self.cleaner.clean(keys, lambda _line: None)
                after = psutil.disk_usage(drive).free
                return {
                    "ok": True,
                    "verified": after >= before,
                    "message": f"Cleanup attempted; {human_bytes(reclaimed)} reported reclaimed; "
                               f"free space changed by {human_bytes(after - before)}.",
                }

            def reclaim_memory():
                before = psutil.virtual_memory().available
                ok, message = self.booster.flush_ram_standby_list()
                after = psutil.virtual_memory().available
                return {
                    "ok": ok,
                    "verified": ok and after >= before,
                    "message": f"{message} Available memory changed by {human_bytes(after - before)}.",
                }

            return self.auto_repair_engine.execute(plan, {
                "cleanup_temp_files": cleanup_temp_files,
                "reclaim_memory": reclaim_memory,
            })

        def done(result):
            self.end_op()
            self.ai_audit_text.delete("1.0", "end")
            if isinstance(result, Exception):
                self.ai_audit_text.insert("1.0", f"Auto-Repair failed: {result}")
                return
            self.ai_audit_text.insert("1.0", "=== VERIFIED AI AUTO-REPAIR RESULTS ===\n\n")
            for outcome in result.outcomes:
                label = outcome.status.upper()
                self.ai_audit_text.insert("end", f"  • [{label}] {outcome.title}: {outcome.message}\n")
            if plan.review_required:
                self.ai_audit_text.insert("end", "\nREVIEW REQUIRED — NOT AUTO-CHANGED:\n")
                for item in plan.review_required:
                    self.ai_audit_text.insert("end", f"  • {item.title}\n")
            self.ai_audit_text.insert(
                "end",
                f"\nVerified repairs: {result.verified_count}. Failed: {result.failed_count}. "
                "Run a new AI audit to measure the updated system state.\n",
            )
            self.logger.log("Verified AI Auto-Repair completed",
                            f"verified={result.verified_count} failed={result.failed_count}")

        self.run_bg(work, done)

    def _scan_ai_winget(self):
        if not self.begin_op("winget AI scan"):
            return
        self.ai_chat_text.insert("end", "\n[System] Scanning winget for software updates...\n")

        def work():
            return self.ai_engine.get_winget_updates_summary()

        def done(res):
            self.end_op()
            cnt = res.get("count", 0)
            self.ai_chat_text.insert("end", f"[AI Assistant] Scan finished. Found {cnt} software package(s) with available updates.\n")
            for pkg in res.get("packages", []):
                self.ai_chat_text.insert("end", f"  • {pkg['name']} -> New Version: {pkg['available']} (Current: {pkg['version']})\n")
            if cnt > 0:
                self.ai_chat_text.insert("end", "\nClick 'Upgrade All Outdated Software' above to automatically update all apps.\n")
            self.ai_chat_text.see("end")
        self.run_bg(work, done)

    def _ask_ai_submit(self):
        prompt = self.ai_prompt_entry.get().strip()
        if not prompt:
            return
        self.ai_prompt_entry.delete(0, "end")
        self.ai_chat_text.insert("end", f"\nUser: {prompt}\n")
        self.ai_chat_text.insert("end", "AI Thinking...\n")
        self.ai_chat_text.see("end")

        def work():
            expl = self.ai_engine.explain_item(prompt)
            if expl and expl.get("type") != "Application / Background Service":
                return f"[{expl['name']}]\nType: {expl['type']}\nOrigin: {expl['origin']}\nRisk: {expl['risk']}\nDescription: {expl['description']}\nAction: {expl['action']}"
            
            res = self.ai_engine.query_llm(prompt, system_prompt="You are WinCare AI, an expert Windows 11 performance and maintenance advisor.")
            if res:
                return res
            return f"WinCare AI Recommendation for '{prompt}':\nTo keep Windows 11 running efficiently, ensure background bloat updaters are cleaned, unnecessary services like Print Spooler (if no printer) are disabled, and software packages are kept up to date via winget."

        def done(reply):
            self.ai_chat_text.insert("end", f"WinCare AI: {reply}\n\n")
            self.ai_chat_text.see("end")
        self.run_bg(work, done)

    # =================================================================
    # TAB: PRIVACY SHIELD
    # =================================================================
    def _build_privacy_shield(self, root):
        ctk.CTkLabel(root, text="Windows 11 Privacy Shield & Anti-Spying Engine",
                     font=ctk.CTkFont(size=22, weight="bold")).pack(anchor="w")
        ctk.CTkLabel(root, text="Disable Bing Start Menu Search, Copilot/Recall telemetry, Advertising ID tracking, and telemetry collection.",
                     text_color="gray55", font=ctk.CTkFont(size=12)).pack(anchor="w", pady=(2, 10))

        # Action bar
        bar = ctk.CTkFrame(root, fg_color=CARD_BG, corner_radius=10)
        bar.pack(fill="x", pady=(0, 10))
        row = ctk.CTkFrame(bar, fg_color="transparent")
        row.pack(fill="x", padx=14, pady=10)

        ctk.CTkButton(row, text="🔒 Maximum Privacy Preset", width=180, height=32,
                      fg_color="#C0392B", hover_color="#96281B",
                      command=lambda: self._apply_privacy("maximum_privacy")).pack(side="left", padx=(0, 8))

        ctk.CTkButton(row, text="⚖ Balanced Privacy Preset", width=180, height=32,
                      fg_color="#1F5FC4", hover_color="#174A98",
                      command=lambda: self._apply_privacy("balanced_privacy")).pack(side="left", padx=8)

        ctk.CTkButton(row, text="🔄 Restore Privacy Defaults", width=180, height=32,
                      fg_color="gray30", hover_color="gray25",
                      command=lambda: self._apply_privacy("restore_defaults")).pack(side="left", padx=8)

        ctk.CTkButton(row, text="↻ Refresh Status", width=120, height=32,
                      fg_color="gray30", hover_color="gray25",
                      command=self._refresh_privacy_status).pack(side="right")

        # Toggles Card
        card = ctk.CTkFrame(root, fg_color=CARD_BG, corner_radius=10)
        card.pack(fill="both", expand=True, pady=(0, 10))

        ctk.CTkLabel(card, text="PRIVACY & TELEMETRY TOGGLES", text_color="gray55",
                     font=ctk.CTkFont(size=12, weight="bold")).pack(anchor="w", padx=14, pady=(10, 6))

        self.sw_bing = ctk.CTkSwitch(
            card, text="Disable Bing Web Search in Start Menu",
            command=lambda: self._privacy_toggle("bing", bool(self.sw_bing.get())))
        self.sw_bing.pack(anchor="w", padx=16, pady=6)

        self.sw_copilot = ctk.CTkSwitch(
            card, text="Disable Windows Copilot & Windows Recall Telemetry",
            command=lambda: self._privacy_toggle(
                "copilot", bool(self.sw_copilot.get())))
        self.sw_copilot.pack(anchor="w", padx=16, pady=6)

        self.sw_ad_id = ctk.CTkSwitch(
            card, text="Disable Windows Advertising ID Tracking",
            command=lambda: self._privacy_toggle(
                "advertising_id", bool(self.sw_ad_id.get())))
        self.sw_ad_id.pack(anchor="w", padx=16, pady=6)

        self.sw_telemetry = ctk.CTkSwitch(
            card, text="Minimize Windows Telemetry to Security Level (0)",
            command=lambda: self._privacy_toggle(
                "telemetry", bool(self.sw_telemetry.get())))
        self.sw_telemetry.pack(anchor="w", padx=16, pady=6)

        self.sw_location = ctk.CTkSwitch(
            card, text="Disable Windows Location Tracking Services",
            command=lambda: self._privacy_toggle(
                "location", bool(self.sw_location.get())))
        self.sw_location.pack(anchor="w", padx=16, pady=6)

        self.sw_app_diag = ctk.CTkSwitch(
            card, text="Disable App Diagnostic Tailored Experiences",
            command=lambda: self._privacy_toggle(
                "app_diagnostics", bool(self.sw_app_diag.get())))
        self.sw_app_diag.pack(anchor="w", padx=16, pady=6)

        self.privacy_console = ConsolePanel(card, height=120)
        self.privacy_console.pack(fill="x", padx=14, pady=(10, 10))

        self._refresh_privacy_status()

    def _refresh_privacy_status(self):
        switches = privacy_protection_switches(self.privacy.get_all_states())
        if switches["bing"]: self.sw_bing.select()
        else: self.sw_bing.deselect()

        if switches["copilot"]: self.sw_copilot.select()
        else: self.sw_copilot.deselect()

        if switches["advertising_id"]: self.sw_ad_id.select()
        else: self.sw_ad_id.deselect()

        if switches["telemetry"]: self.sw_telemetry.select()
        else: self.sw_telemetry.deselect()

        if switches["location"]: self.sw_location.select()
        else: self.sw_location.deselect()

        if switches["app_diagnostics"]: self.sw_app_diag.select()
        else: self.sw_app_diag.deselect()

    def _apply_privacy(self, preset):
        changes = {
            "maximum_privacy": [
                "Disable Bing suggestions, Copilot/Recall, Advertising ID, "
                "Location, and tailored diagnostics",
                "Set Windows telemetry policy to Security/Minimum (0)",
            ],
            "balanced_privacy": [
                "Disable Bing suggestions, Copilot/Recall, Advertising ID, "
                "and tailored diagnostics",
                "Set telemetry to Basic (1) and leave Location enabled",
            ],
            "restore_defaults": [
                "Re-enable all WinCare-managed privacy features",
                "Set Windows telemetry policy to Full/Optional (3)",
            ],
        }[preset]
        if not self.confirm_changes(
                "Privacy preset preview", changes, reversible=False,
                confirm_text="Apply privacy preset"):
            return
        out = self.privacy_console.write
        out(f"=== Applying Privacy Preset: [{preset.upper()}] ===")
        cnt = self.privacy.apply_privacy_preset(preset, out)
        self._refresh_privacy_status()
        self.notify("Privacy Shield", f"Applied [{preset.upper()}] preset. {cnt} registry settings updated.")

    def _privacy_toggle(self, key, protected):
        changes = {
            "bing": "Set Start-menu Bing search registry values to "
                    + ("disabled" if protected else "enabled"),
            "copilot": "Set Copilot and Recall policy values to "
                       + ("disabled" if protected else "enabled"),
            "advertising_id": "Set Windows Advertising ID to "
                              + ("disabled" if protected else "enabled"),
            "telemetry": "Set Windows telemetry policy to "
                         + ("Security/Minimum (0)" if protected else "Full (3)"),
            "location": "Set Windows Location policy to "
                        + ("disabled" if protected else "enabled"),
            "app_diagnostics": "Set tailored diagnostic experiences to "
                               + ("disabled" if protected else "enabled"),
        }
        if not self.confirm_changes(
                "Privacy setting preview", [changes[key]], reversible=False):
            self._refresh_privacy_status()
            return
        setters = {
            "bing": lambda: self.privacy.set_bing_start_search(not protected),
            "copilot": lambda: self.privacy.set_copilot_recall(not protected),
            "advertising_id": lambda: self.privacy.set_advertising_id(
                not protected),
            "telemetry": lambda: self.privacy.set_telemetry_level(
                0 if protected else 3),
            "location": lambda: self.privacy.set_location_tracking(
                not protected),
            "app_diagnostics": lambda: self.privacy.set_app_diagnostics(
                not protected),
        }
        ok = setters[key]()
        self._refresh_privacy_status()
        self.notify("Privacy setting", "Change applied." if ok
                    else "Windows refused the change.")

    # =================================================================
    # TAB: BLOATWARE REMOVER
    # =================================================================
    def _build_bloatware_remover(self, root):
        ctk.CTkLabel(root, text="UWP App Bloatware Uninstaller & Leftover Cleaner",
                     font=ctk.CTkFont(size=22, weight="bold")).pack(anchor="w")
        ctk.CTkLabel(root, text="Scan and uninstall pre-installed Windows 11 UWP apps (Xbox, Solitaire, News, Weather) and clean leftover files.",
                     text_color="gray55", font=ctk.CTkFont(size=12)).pack(anchor="w", pady=(2, 10))

        bar = ctk.CTkFrame(root, fg_color=CARD_BG, corner_radius=10)
        bar.pack(fill="x", pady=(0, 10))
        row = ctk.CTkFrame(bar, fg_color="transparent")
        row.pack(fill="x", padx=14, pady=10)

        ctk.CTkButton(row, text="🔍 Scan Installed UWP Bloatware", width=220, height=32,
                      fg_color="#1F5FC4", hover_color="#174A98",
                      command=self._scan_uwp_bloat).pack(side="left", padx=(0, 8))

        ctk.CTkButton(row, text="🗑 Uninstall Selected UWP App", width=220, height=32,
                      fg_color="#C0392B", hover_color="#96281B",
                      command=self._uninstall_selected_uwp).pack(side="left", padx=8)

        ctk.CTkButton(row, text="🧹 Scan & Clean Orphaned Leftovers", width=240, height=32,
                      fg_color="#8A5A12", hover_color="#68430D",
                      command=self._scan_orphaned_leftovers).pack(side="left", padx=8)

        card = ctk.CTkFrame(root, fg_color=CARD_BG, corner_radius=10)
        card.pack(fill="both", expand=True)

        cols = ("Name", "Package Full Name", "Removable")
        frame, self.uwp_tree = styled_treeview(card, cols, (220, 580, 120), stretch_col="Package Full Name")
        frame.pack(fill="both", expand=True, padx=14, pady=(10, 8))

        self.uwp_console = ConsolePanel(card, height=120)
        self.uwp_console.pack(fill="x", padx=14, pady=(0, 10))

    def _scan_uwp_bloat(self):
        if not self.begin_op("UWP Bloat Scan"):
            return
        out = self.uwp_console.write
        out("=== Scanning installed UWP bloatware packages... ===")
        for i in self.uwp_tree.get_children():
            self.uwp_tree.delete(i)

        def work():
            return self.bloat_remover.get_installed_uwp_bloat()

        def done(apps):
            self.end_op()
            for app in apps:
                self.uwp_tree.insert("", "end", values=(app["display_name"], app["package_name"], "Yes"))
            out(f"=== Scan completed. Found {len(apps)} removable UWP bloatware app(s). ===")
        self.run_bg(work, done)

    def _uninstall_selected_uwp(self):
        sel = self.uwp_tree.selection()
        if not sel:
            self.notify("No Selection", "Please select a UWP app from the list first.")
            return
        pkg_name = self.uwp_tree.item(sel[0])["values"][1]
        disp_name = self.uwp_tree.item(sel[0])["values"][0]

        if not self.confirm("Uninstall UWP App", f"Are you sure you want to uninstall '{disp_name}' ({pkg_name})?", confirm_text="Uninstall", danger=True):
            return

        out = self.uwp_console.write
        out(f"Uninstalling UWP app: {pkg_name}...")

        def work():
            return self.bloat_remover.uninstall_uwp_app(pkg_name, out)

        def done(res):
            ok, msg = res
            self.notify("Uninstall Finished" if ok else "Uninstall Failed", msg)
            self._scan_uwp_bloat()
        self.run_bg(work, done)

    def _scan_orphaned_leftovers(self):
        out = self.uwp_console.write
        out("=== Scanning orphaned AppData & Registry leftovers... ===")

        def work():
            folders = self.bloat_remover.scan_orphaned_leftovers()
            reg = self.bloat_remover.scan_orphaned_registry()
            return folders, reg

        def done(res):
            folders, reg = res
            out(f"Found {len(folders)} orphaned leftover folder(s) and {len(reg)} orphaned registry key(s).")
            if folders:
                for f in folders:
                    out(f"  • Leftover Folder: {f['path']}")
            if reg:
                for r in reg:
                    out(f"  • Leftover Reg Key: {r['name']} -> {r['command']}")
        self.run_bg(work, done)

    # =================================================================
    # TAB: RAM & NETWORK BOOSTER
    # =================================================================
    def _build_ram_network(self, root):
        ctk.CTkLabel(root, text="RAM Standby Flusher & Fast DNS Gaming Optimizer",
                     font=ctk.CTkFont(size=22, weight="bold")).pack(anchor="w")
        ctk.CTkLabel(root, text="Reclaim standby memory cache, switch to low-latency DNS servers, and disable Nagle's algorithm for gaming.",
                     text_color="gray55", font=ctk.CTkFont(size=12)).pack(anchor="w", pady=(2, 10))

        # RAM Flusher Card
        ram_card = ctk.CTkFrame(root, fg_color=CARD_BG, corner_radius=10)
        ram_card.pack(fill="x", pady=(0, 10))
        ctk.CTkLabel(ram_card, text="RAM MEMORY & STANDBY CACHE FLUSHER", text_color="gray55",
                     font=ctk.CTkFont(size=12, weight="bold")).pack(anchor="w", padx=14, pady=(10, 6))

        r_row = ctk.CTkFrame(ram_card, fg_color="transparent")
        r_row.pack(fill="x", padx=14, pady=(0, 10))

        ctk.CTkButton(r_row, text="⚡ Flush RAM & Trim Standby Cache", width=260, height=34,
                      fg_color="#1B6B49", hover_color="#145238",
                      font=ctk.CTkFont(weight="bold"),
                      command=self._flush_ram_clicked).pack(side="left", padx=(0, 8))

        # Fast DNS Card
        dns_card = ctk.CTkFrame(root, fg_color=CARD_BG, corner_radius=10)
        dns_card.pack(fill="x", pady=(0, 10))
        ctk.CTkLabel(dns_card, text="FAST DNS SWITCHER", text_color="gray55",
                     font=ctk.CTkFont(size=12, weight="bold")).pack(anchor="w", padx=14, pady=(10, 6))

        d_row = ctk.CTkFrame(dns_card, fg_color="transparent")
        d_row.pack(fill="x", padx=14, pady=(0, 10))

        ctk.CTkLabel(d_row, text="Select DNS Provider:").pack(side="left", padx=(0, 8))
        self.dns_choice = ctk.CTkOptionMenu(d_row, values=["Cloudflare (1.1.1.1)", "Google (8.8.8.8)", "Quad9 (9.9.9.9)", "Default DHCP"], width=200)
        self.dns_choice.pack(side="left", padx=8)

        ctk.CTkButton(d_row, text="🌐 Apply DNS Switch", width=160, height=32,
                      command=self._switch_dns_clicked).pack(side="left", padx=8)

        # Gaming Latency Card
        game_card = ctk.CTkFrame(root, fg_color=CARD_BG, corner_radius=10)
        game_card.pack(fill="x", pady=(0, 10))
        ctk.CTkLabel(game_card, text="GAMING TCP LATENCY OPTIMIZER", text_color="gray55",
                     font=ctk.CTkFont(size=12, weight="bold")).pack(anchor="w", padx=14, pady=(10, 6))

        g_row = ctk.CTkFrame(game_card, fg_color="transparent")
        g_row.pack(fill="x", padx=14, pady=(0, 10))

        self.sw_nagle = ctk.CTkSwitch(g_row, text="Disable Nagle's Algorithm (TcpAckFrequency = 1, TCPNoDelay = 1) for lower latency in online games",
                                       command=self._toggle_nagle_clicked)
        self.sw_nagle.pack(side="left", padx=6)

        # Console Panel
        self.booster_console = ConsolePanel(root, height=140)
        self.booster_console.pack(fill="both", expand=True)

    def _flush_ram_clicked(self):
        if not self.confirm_changes(
                "RAM cleanup preview",
                ["Trim accessible process working sets",
                 "Purge the Windows standby list when permitted"],
                reversible=False, confirm_text="Flush RAM"):
            return
        if not self.begin_op("RAM cleanup"):
            return
        out = self.booster_console.write
        out("=== Flushing RAM Working Sets & Standby Memory... ===")

        def work():
            return self.booster.flush_ram_standby_list(out)

        def done(res):
            self.end_op()
            ok, msg = res
            out(f"--> Result: {msg}")
            self.notify("RAM Flush", msg)
        self.run_bg(work, done)

    def _switch_dns_clicked(self):
        choice = self.dns_choice.get().lower()
        dns_type = "cloudflare" if "cloudflare" in choice else "google" if "google" in choice else "quad9" if "quad9" in choice else "dhcp"
        if not self.confirm_changes(
                "DNS change preview",
                [f"Set every active network adapter DNS to {choice}",
                 "Flush the Windows DNS resolver cache"],
                reversible=False, confirm_text="Apply DNS"):
            return
        if not self.begin_op("DNS change"):
            return
        out = self.booster_console.write
        out(f"=== Setting System DNS to [{dns_type.upper()}]... ===")

        def work():
            return self.booster.set_dns_servers(dns_type, out)

        def done(res):
            self.end_op()
            ok, msg = res
            out(f"--> DNS Switch Result: {msg}")
            self.notify("DNS Switch", msg)
        self.run_bg(work, done)

    def _toggle_nagle_clicked(self):
        enable = bool(self.sw_nagle.get())
        if not self.confirm_changes(
                "Gaming TCP preview",
                [("Set" if enable else "Remove")
                 + " TcpAckFrequency, TCPNoDelay, and TcpDelAckTicks "
                   "on active TCP/IP interfaces"],
                reversible=False, confirm_text="Apply TCP settings"):
            if enable:
                self.sw_nagle.deselect()
            else:
                self.sw_nagle.select()
            return
        if not self.begin_op("Gaming TCP change"):
            return
        out = self.booster_console.write
        out(f"=== Setting TCP Gaming Latency Tweaks (Disable Nagle = {enable})... ===")

        def work():
            return self.booster.optimize_tcp_gaming_latency(enable, out)

        def done(res):
            self.end_op()
            ok, msg = res
            out(f"--> TCP Tweak Result: {msg}")
            self.notify("TCP Latency Tweak", msg)
        self.run_bg(work, done)

    # =================================================================
    # TAB: BLOAT & BASELINE
    # =================================================================
    def _build_bloat_baseline(self, root):
        ctk.CTkLabel(root, text="Windows Baseline & Background Task Cleaner",
                     font=ctk.CTkFont(size=22, weight="bold")).pack(anchor="w")
        ctk.CTkLabel(root, text="Identify standard Windows core files vs background bloat, unnecessary printer/driver tasks, and apply 1-click optimization presets.",
                     text_color="gray55", font=ctk.CTkFont(size=12)).pack(anchor="w", pady=(2, 10))

        # 1-Click Presets Frame
        presets_card = ctk.CTkFrame(root, fg_color=CARD_BG, corner_radius=10)
        presets_card.pack(fill="x", pady=(0, 10))
        ctk.CTkLabel(presets_card, text="1-CLICK OPTIMIZATION PRESETS", text_color="gray55",
                     font=ctk.CTkFont(size=12, weight="bold")).pack(anchor="w", padx=14, pady=(10, 4))

        p_row = ctk.CTkFrame(presets_card, fg_color="transparent")
        p_row.pack(fill="x", padx=14, pady=(0, 10))

        ctk.CTkButton(p_row, text="🖨 Disable Printer Spooler & Drivers", height=32,
                      fg_color="#C0392B", hover_color="#96281B",
                      command=lambda: self._apply_preset("disable_printers")).pack(side="left", padx=(0, 6))

        ctk.CTkButton(p_row, text="🎮 Game & Max Performance Mode", height=32,
                      fg_color="#1F5FC4", hover_color="#174A98",
                      command=lambda: self._apply_preset("game_mode")).pack(side="left", padx=6)

        ctk.CTkButton(p_row, text="🧹 Clean Background Bloat", height=32,
                      fg_color="#8A5A12", hover_color="#68430D",
                      command=lambda: self._apply_preset("clean_background_bloat")).pack(side="left", padx=6)

        ctk.CTkButton(p_row, text="📡 Disable Telemetry", height=32,
                      fg_color="gray30", hover_color="gray25",
                      command=lambda: self._apply_preset("disable_telemetry")).pack(side="left", padx=6)

        # Baseline Process Inspector Frame
        proc_card = ctk.CTkFrame(root, fg_color=CARD_BG, corner_radius=10)
        proc_card.pack(fill="both", expand=True)

        top_bar = ctk.CTkFrame(proc_card, fg_color="transparent")
        top_bar.pack(fill="x", padx=14, pady=10)
        ctk.CTkLabel(top_bar, text="RUNNING PROCESSES VS WINDOWS BASELINE", text_color="gray55",
                     font=ctk.CTkFont(size=12, weight="bold")).pack(side="left")

        ctk.CTkButton(top_bar, text="↻ Scan Processes vs Baseline", width=180,
                      command=self._scan_baseline_processes).pack(side="right", padx=(6, 0))
        ctk.CTkButton(top_bar, text="🔏 Verify Digital Signature", width=180, fg_color="gray30", hover_color="gray25",
                      command=self._verify_selected_signature).pack(side="right")

        cols = ("Category", "PID", "Process Name", "RAM (MB)", "Status", "Path")
        frame, self.baseline_tree = styled_treeview(
            proc_card, cols, (140, 60, 180, 80, 160, 400), stretch_col="Path")
        frame.pack(fill="both", expand=True, padx=14, pady=(0, 8))

        self.baseline_console = ConsolePanel(proc_card, height=120)
        self.baseline_console.pack(fill="x", padx=14, pady=(0, 10))

    def _apply_preset(self, preset_name: str):
        if not self.admin:
            self.notify("Administrator Required", "Applying service presets requires Administrator rights. Click 'Restart as Admin' in sidebar.")
            return
        if not self.confirm(f"Apply Preset: {preset_name.upper()}",
                            f"Are you sure you want to apply the [{preset_name.upper()}] preset?\n\nThis will modify background services and terminate non-essential tasks.",
                            confirm_text="Apply Preset", danger=False):
            return
        if not self.begin_op(f"Preset {preset_name}"):
            return
        out = self.baseline_console.write
        out(f"=== Applying Preset: {preset_name} ===")

        def work():
            return self.baseline.apply_optimization_preset(preset_name, out)

        def done(count):
            self.end_op()
            self.notify("Preset Applied", f"Preset [{preset_name.upper()}] completed. {count} actions executed.")
            self._scan_baseline_processes()
        self.run_bg(work, done)

    def _scan_baseline_processes(self):
        if not self.begin_op("Baseline Process Scan"):
            return
        out = self.baseline_console.write
        out("=== Scanning running processes against Windows Baseline ===")

        for i in self.baseline_tree.get_children():
            self.baseline_tree.delete(i)

        def work():
            return self.baseline.scan_processes()

        def done(categorized):
            self.end_op()
            total = 0
            for cat_key, items in categorized.items():
                for item in items:
                    total += 1
                    self.baseline_tree.insert(
                        "", "end",
                        values=(item["category"], item["pid"], item["name"], item["mem_mb"], item["status"], item["exe"])
                    )
            out(f"=== Baseline scan finished. {total} active processes categorized. ===")
            if categorized.get("suspicious_impostors"):
                out(f"⚠️ WARNING: {len(categorized['suspicious_impostors'])} suspicious process(es) detected!")
            if categorized.get("background_bloat"):
                out(f"ℹ️ Found {len(categorized['background_bloat'])} background bloat processes.")
        self.run_bg(work, done)

    def _verify_selected_signature(self):
        sel = self.baseline_tree.selection()
        if not sel:
            self.notify("No Selection", "Please select a process in the table above first.")
            return
        item_vals = self.baseline_tree.item(sel[0])["values"]
        exe_path = item_vals[5]
        if not exe_path or exe_path == "N/A":
            self.notify("Invalid Path", "Executable path is not accessible.")
            return
        out = self.baseline_console.write
        out(f"Verifying Authenticode signature for: {exe_path}...")

        def work():
            return self.baseline.verify_authenticode(exe_path)

        def done(res):
            out(f"--> Signature Status: {res['status']} | Signer: {res['signer']}")
            self.notify("Signature Result", f"File: {exe_path}\nStatus: {res['status']}\nSigner: {res['signer']}")
        self.run_bg(work, done)

    # =================================================================
    # TAB: DIAGNOSTICS
    # =================================================================
    def _build_diagnostics(self, root):
        head = ctk.CTkFrame(root, fg_color="transparent")
        head.pack(fill="x")
        ctk.CTkLabel(head, text="Diagnostics & Scan",
                     font=ctk.CTkFont(size=22, weight="bold")).pack(side="left")
        self.export_btn = ctk.CTkButton(head, text="⭳  Export HTML Report",
                                        state="disabled", width=170,
                                        command=self.export_report)
        self.export_btn.pack(side="right", padx=(8, 0))
        ctk.CTkButton(head, text="🩺 Event Triage", width=130,
                      fg_color="#6C5CE7", hover_color="#5546C8",
                      command=self.open_triage).pack(side="right", padx=(8, 0))
        self.scan_btn = ctk.CTkButton(head, text="▶  Run Full Scan", width=150,
                                      font=ctk.CTkFont(weight="bold"),
                                      command=self.start_scan)
        self.scan_btn.pack(side="right")
        self.cancel_btn = ctk.CTkButton(head, text="Cancel", width=80,
                                        fg_color="gray35", hover_color="gray25",
                                        state="disabled",
                                        command=self._cancel_event.set)
        self.cancel_btn.pack(side="right", padx=8)

        prog_row = ctk.CTkFrame(root, fg_color="transparent")
        prog_row.pack(fill="x", pady=(10, 4))
        self.scan_progress = ctk.CTkProgressBar(prog_row, height=12)
        self.scan_progress.pack(side="left", fill="x", expand=True)
        self.scan_progress.set(0)
        self.scan_step = ctk.CTkLabel(prog_row, text="Idle", width=240,
                                      anchor="e", text_color="gray55")
        self.scan_step.pack(side="right", padx=(10, 0))

        cols = ("Severity", "Category", "Finding", "Recommended action")
        frame, self.scan_tree = styled_treeview(
            root, cols, (90, 110, 430, 420), stretch_col="Recommended action")
        frame.pack(fill="both", expand=True, pady=(8, 0))
        for sev, colr in SEV_COLORS.items():
            self.scan_tree.tag_configure(sev, foreground=colr)
        for c in cols:
            self.scan_tree.heading(
                c, text=c, command=lambda cc=c: sort_treeview(self.scan_tree, cc))
        ctk.CTkLabel(root, text="Severity legend:  Critical = act now · "
                     "Warning = should fix · Info = advisory · OK = healthy",
                     text_color="gray50", font=ctk.CTkFont(size=11)
                     ).pack(anchor="w", pady=(6, 0))

    def start_scan(self):
        if not self.begin_op("Full System Scan"):
            return
        self.scan_btn.configure(state="disabled")
        self.cancel_btn.configure(state="normal")
        self.export_btn.configure(state="disabled")
        for item in self.scan_tree.get_children():
            self.scan_tree.delete(item)

        def progress(step, pct):
            self.after(0, lambda: (self.scan_step.configure(text=step),
                                   self.scan_progress.set(pct)))

        def work():
            return self.scanner.run_full_scan(progress_cb=progress,
                                              cancel_event=self._cancel_event)

        def done(result):
            self.scan_btn.configure(state="normal")
            self.cancel_btn.configure(state="disabled")
            self.end_op()
            if isinstance(result, Exception):
                self.notify("Scan failed", f"The scan crashed:\n{result}")
                return
            findings, metrics, score, breakdown = result
            self.last_findings, self.last_metrics = findings, metrics
            self.last_score, self.last_breakdown = score, breakdown
            for f in findings:
                self.scan_tree.insert(
                    "", "end", values=(f["severity"], f["category"],
                                       f["title"], f["recommendation"]),
                    tags=(f["severity"],))
            self.scan_step.configure(
                text=f"Done — score {score}/100 ({HealthScore.grade(score)})")
            self.export_btn.configure(state="normal")
            self.settings.set("last_scan", datetime.now().isoformat())
            score_history_append(score)
            self._apply_score_to_dashboard()

        self.run_bg(work, done)

    def export_report(self):
        if self.last_score is None:
            return
        try:
            path = ReportExporter.export_html(
                SysInfo.summary(), self.last_score,
                HealthScore.grade(self.last_score),
                self.last_breakdown, self.last_findings)
            self.logger.log("Report exported", str(path))
            try:
                os.startfile(path)                     # open in browser
            except OSError:
                pass
            self.notify("Report exported", f"Saved and opened:\n{path}")
        except OSError as e:
            self.notify("Export failed", str(e))

    def open_triage(self):
        """Open (or focus) the Event Triage window."""
        if getattr(self, "_triage_win", None) is not None:
            try:
                if self._triage_win.winfo_exists():
                    self._triage_win.focus_force()
                    return
            except tk.TclError:
                pass
        self._triage_win = TriageWindow(self)

    # =================================================================
    # DASHBOARD QUICK ACTIONS
    # =================================================================
    def one_click_optimize(self):
        """Safe combo: temp cleanup + thumbnail cache + DNS flush."""
        keys = ["user_temp", "thumbs"] + (["win_temp"] if self.admin else [])
        if not self.confirm(
                "One-Click Optimize",
                "This will run a SAFE optimization pass:\n\n"
                "  • Clean user Temp files (in-use files skipped)\n"
                "  • Clear thumbnail cache (auto-rebuilt)\n"
                + ("  • Clean Windows Temp folder\n" if self.admin else
                   "  • (Windows Temp skipped — not running as admin)\n")
                + "  • Flush DNS resolver cache\n\n"
                "No system settings are changed and no restore point is "
                "needed for these actions. Proceed?",
                confirm_text="Optimize now", danger=False):
            return
        if not self.begin_op("One-Click Optimize"):
            return
        self.select_tab("Maintenance")
        out = self.maint_console.write
        out("=== One-Click Optimize started ===")

        def work():
            freed = self.cleaner.clean(keys, out)
            out(">> Flushing DNS cache ...")
            run_cmd(["ipconfig", "/flushdns"], timeout=30)
            out(">> DNS cache flushed.")
            return freed

        def done(res):
            self.end_op()
            if isinstance(res, Exception):
                self.notify("Optimize failed", str(res))
                return
            out("=== One-Click Optimize finished ===")
            self.notify("Optimize complete",
                        f"Reclaimed {human_bytes(res)} of disk space and "
                        "flushed the DNS cache.\n\nFor deeper gains: review "
                        "Startup Programs and Services in the Optimize tab.")
        self.run_bg(work, done)

    def create_restore_point_clicked(self):
        if not self.admin:
            self.notify("Administrator required",
                        "Creating a System Restore Point requires admin "
                        "rights. Use 'Restart as Admin' in the sidebar.")
            return
        if not self.confirm(
                "Create System Restore Point",
                "Creates a snapshot of system files, registry and drivers "
                "that you can roll back to via 'rstrui.exe'.\n\n"
                "Note: Windows allows one restore point per 24 hours by "
                "default — if a recent one exists this will be skipped "
                "(you are still protected).",
                confirm_text="Create", danger=False):
            return
        if not self.begin_op("Create Restore Point"):
            return
        self.select_tab("Maintenance")

        def work():
            return self.repair.create_restore_point(
                self.maint_console.write, "WinCare Pro manual checkpoint")

        def done(res):
            self.end_op()
            ok = res is True
            self.notify("Restore point",
                        "Restore point step finished — see console output."
                        if ok else "Could not create a restore point. Enable "
                        "System Protection: Start > 'Create a restore point' "
                        "> Configure > Turn on system protection.")
        self.run_bg(work, done)

    # =================================================================
    # TAB: REPAIRS
    # =================================================================
    REPAIR_DEFS = [
        ("sfc", "SFC /scannow", "Verify & repair protected system files",
         "Runs the Windows System File Checker.\n\nDuration: 5–20 minutes.\n"
         "Risk: LOW — official Microsoft tool, repairs files from the local "
         "component store.\n\nProceed?", False),
        ("dism", "DISM RestoreHealth", "Repair the Windows component store",
         "Runs DISM /Online /Cleanup-Image /RestoreHealth.\n\nDuration: "
         "10–30 minutes, needs internet (downloads clean files from Windows "
         "Update).\nRisk: LOW — official Microsoft tool.\n\nTip: run this "
         "FIRST if SFC reported unfixable errors, then run SFC again.\n\n"
         "Proceed?", False),
        ("chkdsk", "chkdsk /f /r", "Full disk check — REQUIRES REBOOT",
         "Schedules a full disk check (chkdsk C: /f /r) for the NEXT "
         "REBOOT because the system drive is in use.\n\n"
         "⚠ THE NEXT BOOT CAN TAKE SEVERAL HOURS on large/damaged drives. "
         "Do not power off during the check.\n\nRisk: LOW-MEDIUM — on a "
         "dying disk, stress can accelerate failure. BACK UP FIRST if SMART "
         "status is not Healthy.\n\nSchedule the disk check?", True),
        ("wureset", "Reset Windows Update", "Fix stuck or failing updates",
         "Stops update services, RENAMES the update caches "
         "(SoftwareDistribution, catroot2 — nothing is deleted), and "
         "restarts the services. Windows rebuilds the caches.\n\nRisk: LOW. "
         "Update history display may reset; installed updates are NOT "
         "removed.\n\nProceed?", False),
        ("netreset", "Reset Network Stack", "Fix connectivity/DNS problems",
         "Runs: winsock reset, TCP/IP reset, DNS flush, DHCP release/renew."
         "\n\n⚠ Your network will briefly DISCONNECT, VPN/adapter tweaks "
         "may need reconfiguring, and a REBOOT is required to finish.\n\n"
         "Proceed?", True),
        ("profile", "User Profile Check", "Detect corrupted-profile signals",
         "READ-ONLY check for user-profile corruption (temp profile, .bak "
         "registry entries) with step-by-step guidance.\n\nRisk: NONE — "
         "nothing is modified.\n\nRun the check?", False),
    ]

    def _build_repairs(self, root):
        head = ctk.CTkFrame(root, fg_color="transparent")
        head.pack(fill="x")
        ctk.CTkLabel(head, text="Repairs",
                     font=ctk.CTkFont(size=22, weight="bold")).pack(side="left")
        ctk.CTkLabel(head,
                     text=("Auto restore point: ON (Settings)" if
                           self.settings.get("auto_restore_point")
                           else "Auto restore point: OFF (Settings)"),
                     text_color="gray55").pack(side="right")

        grid = ctk.CTkFrame(root, fg_color="transparent")
        grid.pack(fill="x", pady=(10, 0))
        for i in range(3):
            grid.grid_columnconfigure(i, weight=1, uniform="rep")
        for idx, (key, title, subtitle, _msg, danger) in enumerate(self.REPAIR_DEFS):
            card = ctk.CTkFrame(grid, fg_color=CARD_BG, corner_radius=10)
            card.grid(row=idx // 3, column=idx % 3, sticky="nsew",
                      padx=4, pady=4)
            ctk.CTkLabel(card, text=title,
                         font=ctk.CTkFont(size=14, weight="bold")
                         ).pack(anchor="w", padx=14, pady=(10, 0))
            ctk.CTkLabel(card, text=subtitle, text_color="gray55",
                         font=ctk.CTkFont(size=11), wraplength=280,
                         justify="left").pack(anchor="w", padx=14)
            ctk.CTkButton(card, text="Run", width=90, height=30,
                          fg_color="#C0392B" if danger else ACCENT,
                          hover_color="#96281B" if danger else "#1F5FC4",
                          command=lambda k=key: self.run_repair(k)
                          ).pack(anchor="e", padx=12, pady=10)

        ctk.CTkLabel(root, text="Live output", text_color="gray55",
                     font=ctk.CTkFont(size=12, weight="bold")
                     ).pack(anchor="w", pady=(12, 2))
        self.repair_console = ConsolePanel(root, height=230)
        self.repair_console.pack(fill="both", expand=True)

    def run_repair(self, key):
        rdef = next(r for r in self.REPAIR_DEFS if r[0] == key)
        _, title, _sub, message, danger = rdef
        needs_admin = key != "profile"
        if needs_admin and not self.admin:
            self.notify("Administrator required",
                        f"'{title}' needs admin rights.\nUse 'Restart as "
                        "Admin' in the sidebar, then try again.")
            return
        if not self.confirm(title, message, confirm_text="Run " + title,
                            danger=danger):
            return
        if not self.begin_op(title):
            return
        out = self.repair_console.write
        out(f"=== {title} started ===")
        fns = {"sfc": self.repair.sfc_scan, "dism": self.repair.dism_restore,
               "chkdsk": self.repair.chkdsk,
               "wureset": self.repair.reset_windows_update,
               "netreset": self.repair.reset_network,
               "profile": self.repair.repair_profile_check}

        def work():
            # Auto restore point before anything that mutates the system.
            if (needs_admin and self.settings.get("auto_restore_point")
                    and key != "profile"):
                self.repair.create_restore_point(out, f"Before {title}")
            return fns[key](out)

        def done(res):
            self.end_op()
            out(f"=== {title} finished ===")
            if isinstance(res, Exception):
                self.notify(f"{title} failed", str(res))
            elif key in ("chkdsk", "netreset"):
                self.notify(title + " done",
                            "Action completed — a REBOOT is required to "
                            "finish this repair.")
        self.run_bg(work, done)

    # =================================================================
    # TAB: OPTIMIZE
    # =================================================================
    def _build_optimize(self, root):
        ctk.CTkLabel(root, text="Optimization & Speed Up",
                     font=ctk.CTkFont(size=22, weight="bold")).pack(anchor="w")
        tabs = ctk.CTkTabview(root, fg_color=CARD_BG)
        tabs.pack(fill="both", expand=True, pady=(8, 0))
        for t in ("Startup Programs", "Services", "Power Plan",
                  "Visuals & Memory"):
            tabs.add(t)
        self._build_opt_startup(tabs.tab("Startup Programs"))
        self._build_opt_services(tabs.tab("Services"))
        self._build_opt_power(tabs.tab("Power Plan"))
        self._build_opt_visuals(tabs.tab("Visuals & Memory"))

    # ---- startup programs ------------------------------------------------
    def _build_opt_startup(self, tab):
        bar = ctk.CTkFrame(tab, fg_color="transparent")
        bar.pack(fill="x", pady=(4, 6))
        ctk.CTkButton(bar, text="↻ Refresh", width=100,
                      command=self._refresh_startup).pack(side="left")
        ctk.CTkButton(bar, text="Disable selected", width=130,
                      fg_color="#C0392B", hover_color="#96281B",
                      command=lambda: self._toggle_startup(False)
                      ).pack(side="left", padx=6)
        ctk.CTkButton(bar, text="Enable selected", width=130,
                      fg_color="#1B6B49", hover_color="#145238",
                      command=lambda: self._toggle_startup(True)
                      ).pack(side="left")
        ctk.CTkLabel(bar, text="Disabling uses the same mechanism as Task "
                     "Manager — fully reversible.", text_color="gray55",
                     font=ctk.CTkFont(size=11)).pack(side="right")
        cols = ("Name", "Status", "Impact", "Source", "Command")
        frame, self.startup_tree = styled_treeview(
            tab, cols, (180, 80, 80, 150, 420), stretch_col="Command")
        frame.pack(fill="both", expand=True)
        self.startup_tree.tag_configure("disabled", foreground="gray55")
        self.startup_tree.tag_configure("High", foreground="#F5A524")
        self.startup_tree.tag_configure("Broken", foreground="#E5484D")
        for c in cols:
            self.startup_tree.heading(
                c, text=c, command=lambda cc=c: sort_treeview(self.startup_tree, cc))
        self._startup_items = []
        self._refresh_startup()

    def _refresh_startup(self):
        def work():
            return Optimizer.list_startup_items()

        def done(items):
            if isinstance(items, Exception):
                return
            self._startup_items = items
            for i in self.startup_tree.get_children():
                self.startup_tree.delete(i)
            for idx, it in enumerate(items):
                tags = []
                if not it["enabled"]:
                    tags.append("disabled")
                if it["impact"] in ("High", "Broken"):
                    tags.append(it["impact"])
                self.startup_tree.insert(
                    "", "end", iid=str(idx),
                    values=(it["name"],
                            "Enabled" if it["enabled"] else "Disabled",
                            it["impact"], it["source"], it["command"]),
                    tags=tuple(tags))
        self.run_bg(work, done)

    def _selected_startup_item(self):
        sel = self.startup_tree.selection()
        if not sel:
            self.notify("No selection", "Select a startup entry first.")
            return None
        try:
            return self._startup_items[int(sel[0])]
        except (ValueError, IndexError):
            return None

    def _toggle_startup(self, enable):
        item = self._selected_startup_item()
        if not item:
            return
        if item["enabled"] == enable:
            self.notify("Nothing to do",
                        f"'{item['name']}' is already "
                        f"{'enabled' if enable else 'disabled'}.")
            return
        if not enable and not self.confirm(
                "Disable startup program",
                f"Disable '{item['name']}' from starting with Windows?\n\n"
                f"Command: {item['command']}\n\nThe program itself is NOT "
                "uninstalled and you can re-enable it here anytime.",
                confirm_text="Disable", danger=True):
            return
        ok, msg = self.optimizer.set_startup_enabled(item, enable)
        self.notify("Startup change", msg)
        if ok:
            self._refresh_startup()

    # ---- services -------------------------------------------------------
    def _build_opt_services(self, tab):
        bar = ctk.CTkFrame(tab, fg_color="transparent")
        bar.pack(fill="x", pady=(4, 6))
        ctk.CTkButton(bar, text="↻ Refresh", width=100,
                      command=self._refresh_services).pack(side="left")
        ctk.CTkButton(bar, text="Stop & disable selected", width=170,
                      fg_color="#C0392B", hover_color="#96281B",
                      command=lambda: self._toggle_service(True)
                      ).pack(side="left", padx=6)
        ctk.CTkButton(bar, text="Restore selected", width=130,
                      fg_color="#1B6B49", hover_color="#145238",
                      command=lambda: self._toggle_service(False)
                      ).pack(side="left")
        ctk.CTkLabel(bar, text="Only curated, safe-to-consider services are "
                     "listed. Originals are backed up.", text_color="gray55",
                     font=ctk.CTkFont(size=11)).pack(side="right")
        cols = ("Service", "Status", "Start type", "What it does / trade-off")
        frame, self.svc_tree = styled_treeview(
            tab, cols, (200, 90, 100, 520),
            stretch_col="What it does / trade-off")
        frame.pack(fill="both", expand=True)
        self.svc_tree.tag_configure("running", foreground="#F5A524")
        self.svc_tree.tag_configure("stopped", foreground="gray55")
        self._refresh_services()

    def _refresh_services(self):
        def work():
            rows = []
            for svc in Optimizer.OPTIONAL_SERVICES:
                status, start = Optimizer.service_state(svc["name"])
                if status is None:
                    continue
                rows.append((svc, status, start))
            return rows

        def done(rows):
            if isinstance(rows, Exception):
                return
            self._svc_rows = rows
            for i in self.svc_tree.get_children():
                self.svc_tree.delete(i)
            for idx, (svc, status, start) in enumerate(rows):
                self.svc_tree.insert(
                    "", "end", iid=str(idx),
                    values=(f"{svc['display']} ({svc['name']})", status,
                            start, svc["why"]),
                    tags=("running" if status == "running" else "stopped",))
        self.run_bg(work, done)

    def _toggle_service(self, disable):
        sel = self.svc_tree.selection()
        if not sel:
            self.notify("No selection", "Select a service first.")
            return
        if not self.admin:
            self.notify("Administrator required",
                        "Changing services requires admin rights.")
            return
        try:
            svc, status, start = self._svc_rows[int(sel[0])]
        except (ValueError, IndexError):
            return
        if disable and not self.confirm(
                "Stop & disable service",
                f"Service: {svc['display']} ({svc['name']})\n"
                f"Current: {status}, start type {start}\n\n{svc['why']}\n\n"
                "The original start type is backed up and can be restored "
                "here or in Settings > Undo Center.",
                confirm_text="Stop & disable", danger=True):
            return
        ok, msg = self.optimizer.set_service(svc["name"], disable,
                                             lambda line: None)
        self.notify("Service change", msg)
        if ok:
            self._refresh_services()

    # ---- power plan -----------------------------------------------------
    def _build_opt_power(self, tab):
        ctk.CTkLabel(tab, text="Active power plan controls the "
                     "performance/battery trade-off.", text_color="gray55"
                     ).pack(anchor="w", pady=(4, 8))
        row = ctk.CTkFrame(tab, fg_color="transparent")
        row.pack(fill="x")
        self.power_var = tk.StringVar(value="")
        self.power_menu = ctk.CTkOptionMenu(row, variable=self.power_var,
                                            values=["(loading...)"], width=340)
        self.power_menu.pack(side="left")
        ctk.CTkButton(row, text="Apply plan", width=110,
                      command=self._apply_power_plan).pack(side="left", padx=8)
        ctk.CTkButton(row, text="↻", width=40,
                      command=self._refresh_power).pack(side="left")
        ctk.CTkButton(row, text="Add 'Ultimate Performance' plan", width=230,
                      fg_color="#6C5CE7", hover_color="#5546C8",
                      command=self._add_ultimate).pack(side="left", padx=8)
        self.power_status = ctk.CTkLabel(tab, text="", text_color="gray55")
        self.power_status.pack(anchor="w", pady=8)
        ctk.CTkLabel(tab, text=(
            "Guidance:\n"
            "  • Balanced — best default for desktops and laptops.\n"
            "  • High/Ultimate Performance — max responsiveness, more power "
            "draw & heat. Best for plugged-in workstations/gaming.\n"
            "  • Power saver — laptops on battery only."),
            justify="left", text_color="gray55",
            font=ctk.CTkFont(size=12)).pack(anchor="w", pady=6)
        self._power_plans = []
        self._refresh_power()

    def _refresh_power(self):
        def work():
            return Optimizer.list_power_plans()

        def done(plans):
            if isinstance(plans, Exception) or not plans:
                self.power_status.configure(text="Could not read power plans.")
                return
            self._power_plans = plans
            names = [p["name"] for p in plans]
            self.power_menu.configure(values=names)
            active = next((p["name"] for p in plans if p["active"]), names[0])
            self.power_var.set(active)
            self.power_status.configure(text=f"Active plan: {active}")
        self.run_bg(work, done)

    def _apply_power_plan(self):
        name = self.power_var.get()
        plan = next((p for p in self._power_plans if p["name"] == name), None)
        if not plan:
            return
        ok, msg = self.optimizer.set_power_plan(plan["guid"], name)
        self.power_status.configure(text=msg)

    def _add_ultimate(self):
        ok, msg = self.optimizer.enable_ultimate_performance()
        self.power_status.configure(text=msg)
        if ok:
            self._refresh_power()

    # ---- visuals & memory ------------------------------------------------
    def _build_opt_visuals(self, tab):
        left = ctk.CTkFrame(tab, fg_color="transparent")
        left.pack(side="left", fill="both", expand=True, padx=(0, 8))
        right = ctk.CTkFrame(tab, fg_color="transparent")
        right.pack(side="left", fill="both", expand=True)

        ctk.CTkLabel(left, text="Visual effects",
                     font=ctk.CTkFont(size=14, weight="bold")
                     ).pack(anchor="w", pady=(4, 4))
        ctk.CTkButton(left, text="Apply performance preset (safe)",
                      command=lambda: self._simple_action(
                          self.optimizer.apply_performance_visuals)
                      ).pack(fill="x", pady=3)
        ctk.CTkButton(left, text="Restore Windows defaults",
                      fg_color="gray35", hover_color="gray25",
                      command=lambda: self._simple_action(
                          self.optimizer.restore_default_visuals)
                      ).pack(fill="x", pady=3)
        ctk.CTkLabel(left, text="Background apps",
                     font=ctk.CTkFont(size=14, weight="bold")
                     ).pack(anchor="w", pady=(14, 4))
        ctk.CTkButton(left, text="Disable background apps (global)",
                      command=lambda: self._simple_action(
                          lambda: self.optimizer.set_background_apps(False))
                      ).pack(fill="x", pady=3)
        ctk.CTkButton(left, text="Re-allow background apps",
                      fg_color="gray35", hover_color="gray25",
                      command=lambda: self._simple_action(
                          lambda: self.optimizer.set_background_apps(True))
                      ).pack(fill="x", pady=3)

        ctk.CTkLabel(right, text="Memory & pagefile analysis",
                     font=ctk.CTkFont(size=14, weight="bold")
                     ).pack(anchor="w", pady=(4, 4))
        self.mem_box = ctk.CTkTextbox(right, height=260, wrap="word",
                                      font=ctk.CTkFont(family="Consolas", size=12))
        self.mem_box.pack(fill="both", expand=True)
        ctk.CTkButton(right, text="↻ Refresh analysis",
                      command=self._refresh_mem_advice).pack(anchor="e", pady=6)
        self._refresh_mem_advice()

    def _refresh_mem_advice(self):
        self.mem_box.configure(state="normal")
        self.mem_box.delete("1.0", "end")
        self.mem_box.insert("1.0", self.optimizer.memory_pagefile_advice())
        self.mem_box.configure(state="disabled")

    def _simple_action(self, fn):
        """Run a quick (ok, msg) optimizer action and show the result."""
        ok, msg = fn()
        self.notify("Done" if ok else "Failed", msg)

    # =================================================================
    # TAB: PROCESSES & CLEANUP
    # =================================================================
    def _build_processes(self, root):
        ctk.CTkLabel(root, text="Processes & Cleanup",
                     font=ctk.CTkFont(size=22, weight="bold")).pack(anchor="w")
        tabs = ctk.CTkTabview(root, fg_color=CARD_BG)
        tabs.pack(fill="both", expand=True, pady=(8, 0))
        tabs.add("Processes")
        tabs.add("Cleanup")
        tabs.add("Old & Duplicate Files")
        tabs.add("Security Scanner")
        self._build_proc_tab(tabs.tab("Processes"))
        self._build_clean_tab(tabs.tab("Cleanup"))
        self._build_file_finder_tab(tabs.tab("Old & Duplicate Files"))
        self._build_security_scanner_tab(tabs.tab("Security Scanner"))

    # ---- process manager ---------------------------------------------------
    def _build_proc_tab(self, tab):
        bar = ctk.CTkFrame(tab, fg_color="transparent")
        bar.pack(fill="x", pady=(4, 6))
        ctk.CTkButton(bar, text="↻ Refresh", width=100,
                      command=self._refresh_processes).pack(side="left")
        ctk.CTkButton(bar, text="■ End Task", width=110,
                      fg_color="#C0392B", hover_color="#96281B",
                      command=self._end_task).pack(side="left", padx=6)
        ctk.CTkButton(bar, text="🔎 Inspect signature", width=150,
                      command=self._inspect_process).pack(side="left")
        self.proc_count_label = ctk.CTkLabel(bar, text="", text_color="gray55")
        self.proc_count_label.pack(side="right")
        cols = ("PID", "Name", "CPU %", "RAM (MB)", "Path")
        frame, self.proc_tree = styled_treeview(
            tab, cols, (70, 200, 70, 90, 480), stretch_col="Path")
        frame.pack(fill="both", expand=True)
        self.proc_tree.tag_configure("suspicious", foreground="#E5484D")
        self.proc_tree.tag_configure("hot", foreground="#F5A524")
        numeric_cols = {"PID", "CPU %", "RAM (MB)"}
        for c in cols:
            self.proc_tree.heading(
                c, text=c, command=lambda cc=c: sort_treeview(
                    self.proc_tree, cc, numeric=cc in numeric_cols))
        ctk.CTkLabel(tab, text="Red = running from a Temp folder (inspect "
                     "it) · Orange = high resource use. System-critical "
                     "processes cannot be ended.",
                     text_color="gray55", font=ctk.CTkFont(size=11)
                     ).pack(anchor="w", pady=(4, 0))
        self._proc_rows = []

    def _refresh_processes(self):
        """Snapshot processes on a worker thread, then repaint the table."""
        def work():
            return snapshot_processes(self._cpu_cache)

        def done(rows):
            if isinstance(rows, Exception):
                return
            # keep current selection across refresh
            selected_pid = None
            sel = self.proc_tree.selection()
            if sel:
                try:
                    selected_pid = int(self.proc_tree.set(sel[0], "PID"))
                except (ValueError, tk.TclError):
                    pass
            rows.sort(key=lambda r: r["mem"], reverse=True)
            self._proc_rows = rows
            for i in self.proc_tree.get_children():
                self.proc_tree.delete(i)
            for r in rows:
                tags = ()
                if r["suspicious"]:
                    tags = ("suspicious",)
                elif r["cpu"] > 25 or r["mem"] > 1.5 * 1024**3:
                    tags = ("hot",)
                iid = self.proc_tree.insert(
                    "", "end",
                    values=(r["pid"], r["name"], r["cpu"],
                            f"{r['mem'] / 1048576:,.1f}", r["path"]),
                    tags=tags)
                if selected_pid == r["pid"]:
                    self.proc_tree.selection_set(iid)
            self.proc_count_label.configure(
                text=f"{len(rows)} processes · sorted by RAM")
        self.run_bg(work, done)

    def _selected_process(self):
        sel = self.proc_tree.selection()
        if not sel:
            self.notify("No selection", "Select a process first.")
            return None
        try:
            pid = int(self.proc_tree.set(sel[0], "PID"))
            name = self.proc_tree.set(sel[0], "Name")
            path = self.proc_tree.set(sel[0], "Path")
            return pid, name, path
        except (ValueError, tk.TclError):
            return None

    # ---- security scanner tab ----------------------------------------------
    def _build_security_scanner_tab(self, tab):
        bar = ctk.CTkFrame(tab, fg_color="transparent")
        bar.pack(fill="x", pady=(4, 6))

        self.security_scan_btn = ctk.CTkButton(
            bar, text="↻ Scan Threats", width=120,
            command=self._run_security_scan
        )
        self.security_scan_btn.pack(side="left")

        self.security_kill_btn = ctk.CTkButton(
            bar, text="■ Terminate/Remedy Threat", width=160,
            fg_color="#C0392B", hover_color="#96281B",
            command=self._remedy_selected_threat
        )
        self.security_kill_btn.pack(side="left", padx=6)

        self.security_score_label = ctk.CTkLabel(
            bar, text="Security Score: 100/100", font=ctk.CTkFont(size=14, weight="bold"),
            text_color="#2ECC71"
        )
        self.security_score_label.pack(side="right", padx=(10, 0))

        self.security_status_label = ctk.CTkLabel(
            tab, text="Click 'Scan Threats' to start auditing background threats...",
            text_color="gray55", anchor="w"
        )
        self.security_status_label.pack(fill="x", pady=(0, 6))

        cols = ("Type", "Category", "Target Name", "PID/Location", "Risk Description", "Recommended Action")
        frame, self.security_tree = styled_treeview(
            tab, cols, (80, 150, 140, 160, 240, 180), stretch_col="Risk Description"
        )
        frame.pack(fill="both", expand=True)

        self.security_tree.tag_configure("critical", foreground="#E5484D")
        self.security_tree.tag_configure("warning", foreground="#F5A524")

        for c in cols:
            self.security_tree.heading(
                c, text=c, command=lambda cc=c: sort_treeview(
                    self.security_tree, cc, numeric=cc == "PID/Location"
                )
            )

        ctk.CTkLabel(
            tab, text="Red = Critical anomalies (masquerading, deceptive naming). "
                      "Orange = Warning anomalies (unsigned user-space binaries with active connections or scheduled persistence startup keys).",
            text_color="gray55", font=ctk.CTkFont(size=11)
        ).pack(anchor="w", pady=(4, 0))

    def _run_security_scan(self):
        """Runs the security threat suite in the background."""
        self.security_scan_btn.configure(state="disabled", text="Scanning...")
        self.security_status_label.configure(text="Scrutinizing background threats...")

        def work():
            return self.security_scanner.run_security_suite()

        def done(result):
            self.security_scan_btn.configure(state="normal", text="↻ Scan Threats")
            if isinstance(result, Exception):
                self.security_status_label.configure(text=f"Scan failed: {result}")
                return

            for item in self.security_tree.get_children():
                self.security_tree.delete(item)

            score = result["score"]
            color = "#2ECC71"
            if score < 60:
                color = "#E5484D"
            elif score < 90:
                color = "#F5A524"

            self.security_score_label.configure(
                text=f"Security Score: {score}/100",
                text_color=color
            )

            all_threats = result["proc_threats"] + result["startup_threats"]
            for f in all_threats:
                tag = "warning" if f.get("type") == "Warning" else "critical"
                pid_loc = f.get("pid", "") if "pid" in f else f.get("location", "")
                self.security_tree.insert(
                    "", "end",
                    values=(
                        f.get("type", "Warning"),
                        f.get("category", "Unknown"),
                        f.get("name", "N/A"),
                        pid_loc,
                        f.get("details", ""),
                        f.get("action", "")
                    ),
                    tags=(tag,)
                )

            if len(all_threats) == 0:
                self.security_status_label.configure(text="Scan complete. No suspicious background processes detected!")
            else:
                self.security_status_label.configure(text=f"Scan complete. Found {len(all_threats)} potential threat indicators!")

        self.run_bg(work, done)

    def _remedy_selected_threat(self):
        sel = self.security_tree.selection()
        if not sel:
            self.notify("No selection", "Select a security threat from the table first.")
            return

        cols = ("Type", "Category", "Target Name", "PID/Location", "Risk Description", "Recommended Action")
        vals = [self.security_tree.set(sel[0], c) for c in cols]
        t_type, category, name, pid_loc, desc, action = vals

        try:
            pid = int(pid_loc)
            if pid in (0, 4):
                self.notify("Access Denied", "System Core Process cannot be ended.")
                return

            if not self.confirm(
                "Remedy Threat",
                f"Are you sure you want to terminate threat process '{name}' (PID {pid})?\n"
                f"Description: {desc}",
                confirm_text="Terminate Process", danger=True
            ):
                return

            p = psutil.Process(pid)
            p.terminate()
            try:
                p.wait(timeout=3)
            except psutil.TimeoutExpired:
                p.kill()

            self.logger.log("Threat terminated", f"{name} pid={pid} category={category}")
            self.notify("Threat terminated", f"Process '{name}' was successfully terminated!")
            self._run_security_scan()

        except ValueError:
            if "\\" in pid_loc and winreg is not None:
                hive_str, subkey = pid_loc.split("\\", 1)
                hkey = winreg.HKEY_LOCAL_MACHINE if hive_str == "HKLM" else winreg.HKEY_CURRENT_USER

                if not self.confirm(
                    "Delete Startup Persistence",
                    f"Remove persistent startup entry '{name}' from registry?\n"
                    f"Command: {desc}",
                    confirm_text="Delete Registry Key", danger=True
                ):
                    return

                try:
                    key = winreg.OpenKey(hkey, subkey, 0, winreg.KEY_SET_VALUE)
                    winreg.DeleteValue(key, name)
                    winreg.CloseKey(key)

                    self.logger.log("Startup threat removed", f"registry name={name}")
                    self.notify("Success", f"Startup entry '{name}' was successfully deleted from {hive_str} registry.")
                    self._run_security_scan()
                except Exception as e:
                    self.notify("Error", f"Failed to delete registry key: {e}")
            else:
                self.notify("Action Unavailable", "This item has no direct automatic termination path.")
        except psutil.NoSuchProcess:
            self.notify("Threat Gone", "The threat process has already exited.")
            self._run_security_scan()
        except psutil.AccessDenied:
            self.notify("Access Denied", "WinCare Pro does not have permission to terminate this process.")

    def _end_task(self):
        picked = self._selected_process()
        if not picked:
            return
        pid, name, path = picked
        low = name.lower()
        if low in PROTECTED_PROCESSES or pid in (0, 4):
            self.notify("Protected process",
                        f"'{name}' is critical to Windows. Ending it would "
                        "crash or destabilize the system, so WinCare Pro "
                        "refuses to end it.")
            return
        extra = ""
        if low in RISKY_PROCESSES:
            extra = ("\n\n⚠ EXTRA WARNING: this is a Windows component. "
                     "Ending it can log you out, break audio/search, or "
                     "restart the shell.")
        if not self.confirm(
                "End task",
                f"End process '{name}' (PID {pid})?\n{path or ''}"
                f"\n\nUnsaved data in this program will be lost.{extra}",
                confirm_text="End process", danger=True):
            return
        try:
            p = psutil.Process(pid)
            p.terminate()
            try:
                p.wait(timeout=3)
            except psutil.TimeoutExpired:
                p.kill()
            self.logger.log("Process ended", f"{name} pid={pid}")
            self.notify("Process ended", f"'{name}' was terminated.")
        except psutil.NoSuchProcess:
            self.notify("Already gone", "That process has already exited.")
        except psutil.AccessDenied:
            self.notify("Access denied",
                        "Windows refused — the process is protected or "
                        "requires Administrator rights.")
        self._refresh_processes()

    def _inspect_process(self):
        picked = self._selected_process()
        if not picked:
            return
        pid, name, path = picked

        def work():
            return inspect_process_signature(path)

        def done(verdict):
            self.notify(f"Inspection: {name}",
                        f"PID {pid}\nPath: {path or '(unknown)'}\n\n{verdict}")
        self.run_bg(work, done)

    # ---- cleanup ------------------------------------------------------------
    def _build_clean_tab(self, tab):
        bar = ctk.CTkFrame(tab, fg_color="transparent")
        bar.pack(fill="x", pady=(4, 6))
        ctk.CTkButton(bar, text="🧮 Analyze sizes", width=130,
                      command=self._analyze_cleanup).pack(side="left")
        ctk.CTkButton(bar, text="🧹 Clean selected", width=130,
                      fg_color="#C0392B", hover_color="#96281B",
                      command=self._run_cleanup).pack(side="left", padx=6)
        self.clean_progress = ctk.CTkProgressBar(bar, height=12, width=220)
        self.clean_progress.pack(side="right")
        self.clean_progress.set(0)

        body = ctk.CTkFrame(tab, fg_color="transparent")
        body.pack(fill="both", expand=True)
        self.clean_list = ctk.CTkScrollableFrame(body, fg_color=CARD_BG2,
                                                 corner_radius=10, width=430)
        self.clean_list.pack(side="left", fill="y", padx=(0, 8), pady=2)
        self.clean_console = ConsolePanel(body, height=200)
        self.clean_console.pack(side="left", fill="both", expand=True, pady=2)

        self.clean_vars = {}     # key -> (BooleanVar, size_label)
        self._rebuild_cleanup_list()

    def _rebuild_cleanup_list(self):
        for w in self.clean_list.winfo_children():
            w.destroy()
        self.clean_vars = {}
        default_on = {"user_temp", "win_temp", "thumbs"}
        for cat in self.cleaner.categories():
            row = ctk.CTkFrame(self.clean_list, fg_color="transparent")
            row.pack(fill="x", pady=3, padx=4)
            var = tk.BooleanVar(value=cat["key"] in default_on)
            ctk.CTkCheckBox(row, text=cat["label"], variable=var,
                            font=ctk.CTkFont(size=12)).pack(side="left")
            size_lab = ctk.CTkLabel(row, text="—", width=86, anchor="e",
                                    text_color="gray55")
            size_lab.pack(side="right")
            note = ctk.CTkLabel(self.clean_list, text="    " + cat["note"],
                                text_color="gray65",
                                font=ctk.CTkFont(size=10))
            note.pack(fill="x", anchor="w", padx=4)
            self.clean_vars[cat["key"]] = (var, size_lab)

    def _analyze_cleanup(self):
        """Dry-run: compute reclaimable sizes without deleting anything."""
        if not self.begin_op("Analyze cleanup sizes"):
            return
        self.clean_console.write(">> Analyzing reclaimable space (dry run)...")

        def work():
            sizes = {}
            for cat in self.cleaner.categories():
                sizes[cat["key"]] = self.cleaner.analyze_category(cat)
            return sizes

        def done(sizes):
            self.end_op()
            if isinstance(sizes, Exception):
                return
            total = 0
            for key, (var, lab) in self.clean_vars.items():
                sz = sizes.get(key, 0)
                total += sz
                lab.configure(text=human_bytes(sz))
            self.clean_console.write(
                f">> Analysis done. Up to {human_bytes(total)} reclaimable "
                "across all categories.")
        self.run_bg(work, done)

    def _run_cleanup(self):
        keys = [k for k, (var, _) in self.clean_vars.items() if var.get()]
        if not keys:
            self.notify("Nothing selected", "Tick at least one category.")
            return
        labels = [c["label"] for c in self.cleaner.categories()
                  if c["key"] in keys]
        warn_recycle = ("\n\n⚠ Recycle Bin contents are permanently "
                        "deleted — files in it CANNOT be recovered."
                        if "recycle" in keys else "")
        if not self.confirm(
                "Confirm cleanup",
                "The following will be cleaned:\n\n  • "
                + "\n  • ".join(labels)
                + "\n\nFiles currently in use are skipped automatically."
                + warn_recycle,
                confirm_text="Clean now", danger=True):
            return
        if not self.begin_op("Cleanup"):
            return

        def progress(label, pct):
            self.after(0, lambda: self.clean_progress.set(pct))

        def work():
            return self.cleaner.clean(keys, self.clean_console.write,
                                      progress_cb=progress)

        def done(freed):
            self.end_op()
            self.clean_progress.set(1)
            if isinstance(freed, Exception):
                self.notify("Cleanup failed", str(freed))
                return
            self.notify("Cleanup complete",
                        f"Reclaimed {human_bytes(freed)}.")
            self._analyze_cleanup_silent()
        self.run_bg(work, done)

    def _analyze_cleanup_silent(self):
        """Refresh size labels after a clean without the op guard."""
        def work():
            return {c["key"]: self.cleaner.analyze_category(c)
                    for c in self.cleaner.categories()}

        def done(sizes):
            if isinstance(sizes, Exception):
                return
            for key, (var, lab) in self.clean_vars.items():
                lab.configure(text=human_bytes(sizes.get(key, 0)))
        self.run_bg(work, done)

    # ---- old installers and duplicate files -------------------------------
    def _build_file_finder_tab(self, tab):
        bar = ctk.CTkFrame(tab, fg_color="transparent")
        bar.pack(fill="x", pady=(4, 6))
        ctk.CTkButton(bar, text="🔎 Scan Desktop + Downloads", width=190,
                      command=self._scan_old_duplicates).pack(side="left")
        ctk.CTkButton(bar, text="Cancel", width=80,
                      command=self._cancel_file_scan).pack(side="left", padx=6)
        ctk.CTkButton(bar, text="Permanently delete selected", width=190,
                      fg_color="#C0392B", hover_color="#96281B",
                      command=self._delete_file_candidates).pack(side="left")
        self.file_scan_status = ctk.CTkLabel(
            bar, text="Nothing is selected automatically.", text_color="gray55")
        self.file_scan_status.pack(side="right")

        cols = ("Type", "Age", "Size", "Duplicate", "Path")
        frame, self.file_tree = styled_treeview(
            tab, cols, (180, 70, 90, 85, 560), stretch_col="Path")
        frame.pack(fill="both", expand=True)
        for col in cols:
            self.file_tree.heading(
                col, text=col,
                command=lambda name=col: sort_treeview(
                    self.file_tree, name, numeric=name in {"Age", "Size"}))
        self.file_tree.configure(selectmode="extended")
        self._file_candidates = []
        ctk.CTkLabel(
            tab,
            text="Old = installer/archive not modified for 180+ days. "
                 "Duplicates are SHA-256 verified. Deletion is permanent.",
            text_color="gray55", font=ctk.CTkFont(size=11)
        ).pack(anchor="w", pady=(4, 0))

    def _scan_old_duplicates(self):
        if not self.begin_op("Old and duplicate file scan"):
            return
        self.file_scan_status.configure(text="Scanning…")
        for item in self.file_tree.get_children():
            self.file_tree.delete(item)
        self._file_candidates = []

        def progress(count, _path):
            self.after(0, lambda: self.file_scan_status.configure(
                text=f"Scanned {count:,} files…"))

        def work():
            app_path = Path(sys.executable if getattr(sys, "frozen", False)
                            else __file__).resolve()
            return self.file_cleaner.scan_candidates(
                old_days=180,
                exclude_paths=[str(app_path), str(app_path.parent)],
                cancel_event=self._cancel_event,
                progress_cb=progress)

        def done(records):
            self.end_op()
            if isinstance(records, Exception):
                self.notify("Scan failed", str(records))
                self.file_scan_status.configure(text="Scan failed")
                return
            if self._cancel_event.is_set():
                self.file_scan_status.configure(text="Scan cancelled")
                return
            self._file_candidates = records
            for index, record in enumerate(records):
                self.file_tree.insert(
                    "", "end", iid=str(index),
                    values=(record["category"], record["age_days"],
                            human_bytes(record["size_bytes"]),
                            record["duplicate_group"] or "—", record["path"]))
            total = sum(record["size_bytes"] for record in records)
            self.file_scan_status.configure(
                text=f"{len(records)} candidates · {human_bytes(total)} reviewable")
            self.logger.log(
                "Old/duplicate file scan",
                f"{len(records)} candidates, {human_bytes(total)}")
        self.run_bg(work, done)

    def _cancel_file_scan(self):
        if self._busy_op == "Old and duplicate file scan":
            self._cancel_event.set()
            self.file_scan_status.configure(text="Cancelling…")

    def _delete_file_candidates(self):
        indexes = [int(item) for item in self.file_tree.selection()]
        selected = [self._file_candidates[index] for index in indexes]
        if not selected:
            self.notify("Nothing selected", "Select one or more reviewed files first.")
            return
        total = sum(item["size_bytes"] for item in selected)
        summary = (f"Permanently delete {len(selected)} selected file(s), "
                   f"up to {human_bytes(total)}?\n\n"
                   "This bypasses the Recycle Bin and cannot be undone.")
        if not self.confirm("Permanent deletion", summary,
                            confirm_text="Review deletion", danger=True):
            return
        if not self.confirm("Final irreversible confirmation",
                            summary + "\n\nThis is the final confirmation.",
                            confirm_text="Permanently delete", danger=True):
            return
        if not self.begin_op("Permanent file deletion"):
            return

        def work():
            return self.file_cleaner.delete_candidates(
                selected, self._file_candidates,
                callback_out=lambda message: self.logger.log("File cleanup", message))

        def done(result):
            self.end_op()
            if isinstance(result, Exception):
                self.notify("Deletion failed", str(result))
                return
            self.logger.log(
                "Permanent file cleanup complete",
                f"deleted={result['deleted_count']} failed={result['failed_count']} "
                f"freed={human_bytes(result['freed_bytes'])}")
            self.notify(
                "Deletion complete",
                f"Deleted {result['deleted_count']} file(s); "
                f"{result['failed_count']} skipped/failed.\n"
                f"Reclaimed {human_bytes(result['freed_bytes'])}.")
            self._scan_old_duplicates()
        self.run_bg(work, done)

    # =================================================================
    # TAB: MAINTENANCE
    # =================================================================
    def _build_maintenance(self, root):
        ctk.CTkLabel(root, text="Maintenance & Health",
                     font=ctk.CTkFont(size=22, weight="bold")).pack(anchor="w")
        tabs = ctk.CTkTabview(root, fg_color=CARD_BG)
        tabs.pack(fill="both", expand=True, pady=(8, 0))
        tabs.add("Tasks")
        tabs.add("Storage Analyzer")
        self._build_maint_tasks(tabs.tab("Tasks"))
        self._build_storage_tab(tabs.tab("Storage Analyzer"))

    def _build_maint_tasks(self, tab):
        grid = ctk.CTkFrame(tab, fg_color="transparent")
        grid.pack(fill="x", pady=(4, 6))
        for i in range(3):
            grid.grid_columnconfigure(i, weight=1, uniform="mnt")
        buttons = [
            ("🩺 Full Maintenance", "Restore point → cleanup → DNS flush → "
             "drive optimize → component cleanup", self.full_maintenance,
             "#248A5E", "#1B6B49"),
            ("⬇ Windows Update", "Open Settings > Windows Update",
             lambda: self._open_tool("ms-settings:windowsupdate"),
             None, None),
            ("📦 List app updates (winget)", "Check outdated apps via "
             "Windows Package Manager", self.winget_list, None, None),
            ("📦 Upgrade all apps", "winget upgrade --all (confirm first)",
             self.winget_upgrade_all, "#6C5CE7", "#5546C8"),
            ("🌐 Network diagnostics", "Read-only connectivity triage",
             self.run_net_diag, None, None),
            ("🛟 Create Restore Point", "Manual system checkpoint",
             self.create_restore_point_clicked, "#6C5CE7", "#5546C8"),
        ]
        for idx, (title, sub, cmd, fg, hv) in enumerate(buttons):
            card = ctk.CTkFrame(grid, fg_color=CARD_BG2, corner_radius=10)
            card.grid(row=idx // 3, column=idx % 3, sticky="nsew",
                      padx=4, pady=4)
            kwargs = {}
            if fg:
                kwargs = {"fg_color": fg, "hover_color": hv}
            ctk.CTkButton(card, text=title, height=34,
                          font=ctk.CTkFont(size=13, weight="bold"),
                          command=cmd, **kwargs
                          ).pack(fill="x", padx=10, pady=(10, 2))
            ctk.CTkLabel(card, text=sub, text_color="gray55", wraplength=300,
                         font=ctk.CTkFont(size=11), justify="left"
                         ).pack(anchor="w", padx=12, pady=(0, 8))

        # quick tools launcher
        qt = ctk.CTkFrame(tab, fg_color="transparent")
        qt.pack(fill="x", pady=(2, 6))
        ctk.CTkLabel(qt, text="Quick tools:", text_color="gray55"
                     ).pack(side="left", padx=(2, 6))
        tools = [("Device Manager", "devmgmt.msc"),
                 ("Event Viewer", "eventvwr.msc"),
                 ("Disk Management", "diskmgmt.msc"),
                 ("Resource Monitor", "resmon.exe"),
                 ("Task Scheduler", "taskschd.msc"),
                 ("System Restore", "rstrui.exe"),
                 ("Disk Cleanup", "cleanmgr.exe")]
        for label, target in tools:
            ctk.CTkButton(qt, text=label, height=26, width=1,
                          fg_color="gray30", hover_color="gray25",
                          font=ctk.CTkFont(size=11),
                          command=lambda t=target: self._open_tool(t)
                          ).pack(side="left", padx=3)

        self.maint_console = ConsolePanel(tab, height=230)
        self.maint_console.pack(fill="both", expand=True, pady=(4, 0))

    def _open_tool(self, target):
        """Launch a Windows built-in tool or settings URI."""
        try:
            os.startfile(target)
            self.logger.log("Quick tool opened", target)
        except OSError as e:
            self.notify("Could not open", f"{target}\n{e}")

    def full_maintenance(self):
        steps_txt = ("1. Create System Restore Point\n"
                     "2. Clean Temp files + thumbnail cache\n"
                     "3. Flush DNS cache\n"
                     "4. Optimize/TRIM system drive (defrag /O)\n"
                     "5. DISM component-store cleanup (old update files)")
        if not self.admin:
            self.notify("Administrator required",
                        "Full Maintenance needs admin rights for restore "
                        "point, drive optimize and DISM steps.")
            return
        if not self.confirm("Full Maintenance",
                            "Runs this safe sequence:\n\n" + steps_txt +
                            "\n\nTotal time: roughly 10–40 minutes. The app "
                            "stays usable; progress streams to the console.",
                            confirm_text="Start maintenance", danger=False):
            return
        if not self.begin_op("Full Maintenance"):
            return
        out = self.maint_console.write
        out("=== FULL MAINTENANCE started ===")

        def work():
            if self.settings.get("auto_restore_point"):
                self.repair.create_restore_point(out, "Before Full Maintenance")
            freed = self.cleaner.clean(["user_temp", "win_temp", "thumbs"], out)
            out(">> Flushing DNS ...")
            run_cmd(["ipconfig", "/flushdns"], timeout=30)
            self.repair.defrag_optimize(out)
            self.repair.dism_component_cleanup(out)
            return freed

        def done(freed):
            self.end_op()
            out("=== FULL MAINTENANCE finished ===")
            if isinstance(freed, Exception):
                self.notify("Maintenance failed", str(freed))
            else:
                self.notify("Maintenance complete",
                            f"Sequence finished. {human_bytes(freed)} of "
                            "temp data reclaimed. See console for details.")
        self.run_bg(work, done)

    def winget_list(self):
        if not self.begin_op("winget list updates"):
            return
        out = self.maint_console.write
        out("=== Checking app updates via winget ===")

        def work():
            return stream_cmd(["winget", "upgrade", "--include-unknown"], out)

        def done(rc):
            self.end_op()
            if rc == -2:
                out(">> winget not found — install 'App Installer' from the "
                    "Microsoft Store.")
            out("=== winget check finished ===")
        self.run_bg(work, done)

    def winget_upgrade_all(self):
        if not self.confirm(
                "Upgrade all apps",
                "Runs: winget upgrade --all --silent\n\nThis updates every "
                "app winget manages. Apps may close/restart during upgrade. "
                "Save your work first.\n\nProceed?",
                confirm_text="Upgrade all", danger=True):
            return
        if not self.begin_op("winget upgrade all"):
            return
        out = self.maint_console.write
        out("=== Upgrading all apps via winget ===")

        def work():
            return stream_cmd(
                ["winget", "upgrade", "--all", "--silent",
                 "--accept-package-agreements", "--accept-source-agreements"],
                out)

        def done(rc):
            self.end_op()
            out(f"=== winget upgrade finished (rc={rc}) ===")
        self.run_bg(work, done)

    def run_net_diag(self):
        if not self.begin_op("Network diagnostics"):
            return
        out = self.maint_console.write

        def work():
            return self.repair.network_diagnostics(out)

        def done(_):
            self.end_op()
        self.run_bg(work, done)

    # ---- storage analyzer ------------------------------------------------
    def _build_storage_tab(self, tab):
        bar = ctk.CTkFrame(tab, fg_color="transparent")
        bar.pack(fill="x", pady=(4, 6))
        self.storage_path = ctk.CTkEntry(bar, width=380,
                                         placeholder_text="Folder to analyze")
        self.storage_path.insert(0, str(Path.home()))
        self.storage_path.pack(side="left")
        ctk.CTkButton(bar, text="Analyze", width=100,
                      command=self._run_storage).pack(side="left", padx=6)
        ctk.CTkButton(bar, text="Cancel", width=80, fg_color="gray35",
                      hover_color="gray25",
                      command=self._cancel_event.set).pack(side="left")
        self.storage_status = ctk.CTkLabel(bar, text="", text_color="gray55")
        self.storage_status.pack(side="right")
        cols = ("Size", "Type", "Path")
        frame, self.storage_tree = styled_treeview(
            tab, cols, (110, 70, 760), stretch_col="Path")
        frame.pack(fill="both", expand=True)
        self.storage_tree.heading("Size", text="Size", command=lambda:
                                  sort_treeview(self.storage_tree, "Size"))

    def _run_storage(self):
        root_dir = self.storage_path.get().strip()
        if not root_dir or not Path(root_dir).is_dir():
            self.notify("Invalid path", "Enter an existing folder path.")
            return
        if not self.begin_op("Storage analysis"):
            return
        for i in self.storage_tree.get_children():
            self.storage_tree.delete(i)
        self.storage_status.configure(text="Scanning…")

        def progress(p):
            self.after(0, lambda: self.storage_status.configure(
                text=f"Scanning {p[:60]}…"))

        def work():
            return StorageAnalyzer.scan(root_dir, self._cancel_event, progress)

        def done(res):
            self.end_op()
            if isinstance(res, Exception):
                self.storage_status.configure(text="Scan failed.")
                return
            folders, files = res
            for sz, p in folders:
                self.storage_tree.insert("", "end",
                                         values=(human_bytes(sz), "Folder", p))
            for sz, p in files:
                self.storage_tree.insert("", "end",
                                         values=(human_bytes(sz), "File", p))
            self.storage_status.configure(
                text=f"Top {len(folders)} folders + {len(files)} files ≥50 MB")
            self.logger.log("Storage analyzed", root_dir)
        self.run_bg(work, done)

    # =================================================================
    # TAB: SETTINGS (+ Undo Center + log viewer)
    # =================================================================
    def _build_settings(self, root):
        ctk.CTkLabel(root, text="Settings",
                     font=ctk.CTkFont(size=22, weight="bold")).pack(anchor="w")
        wrap = ctk.CTkScrollableFrame(root, fg_color="transparent")
        wrap.pack(fill="both", expand=True, pady=(8, 0))

        # ---- appearance & behaviour ------------------------------------
        card = self._settings_card(wrap, "Appearance & behaviour")
        row = ctk.CTkFrame(card, fg_color="transparent")
        row.pack(fill="x", padx=14, pady=4)
        ctk.CTkLabel(row, text="Theme").pack(side="left")
        ctk.CTkOptionMenu(row, values=["Dark", "Light", "System"], width=140,
                          command=self._set_theme,
                          variable=tk.StringVar(value=self.settings.get("theme"))
                          ).pack(side="right")

        self.sw_restore = ctk.CTkSwitch(
            card, text="Auto-create a System Restore Point before repairs "
                       "(recommended)",
            command=lambda: self.settings.set(
                "auto_restore_point", bool(self.sw_restore.get())))
        self.sw_restore.pack(anchor="w", padx=14, pady=4)
        if self.settings.get("auto_restore_point"):
            self.sw_restore.select()

        self.sw_browser = ctk.CTkSwitch(
            card, text="Include browser caches (Chrome/Edge) in Cleanup",
            command=self._toggle_browser_cache)
        self.sw_browser.pack(anchor="w", padx=14, pady=4)
        if self.settings.get("clean_browser_cache"):
            self.sw_browser.select()

        self.sw_sched = ctk.CTkSwitch(
            card, text="Windows Task Scheduler: launch WinCare Pro weekly "
                       "(Sunday 10:00) as a scan reminder",
            command=self._toggle_sched_task)
        self.sw_sched.pack(anchor="w", padx=14, pady=4)
        self.run_bg(scheduled_task_exists,
                    lambda ex: self.sw_sched.select() if ex is True else None)

        # ---- numbers -----------------------------------------------------
        card = self._settings_card(wrap, "Retention & reminders")
        self.entry_retention = self._settings_number(
            card, "Log retention (days)", self.settings.get("log_retention_days"))
        self.entry_interval = self._settings_number(
            card, "Scan reminder interval (days)",
            self.settings.get("scan_interval_days"))
        ctk.CTkButton(card, text="Save numbers", width=130,
                      command=self._save_numbers).pack(anchor="e", padx=14, pady=8)

        # ---- custom cleanup paths ------------------------------------------
        card = self._settings_card(wrap, "Custom cleanup folders")
        ctk.CTkLabel(card, text="Extra folders emptied by Cleanup. System "
                     "locations are refused automatically.",
                     text_color="gray55", font=ctk.CTkFont(size=11)
                     ).pack(anchor="w", padx=14)
        row = ctk.CTkFrame(card, fg_color="transparent")
        row.pack(fill="x", padx=14, pady=4)
        self.path_entry = ctk.CTkEntry(row, placeholder_text=r"D:\SomeCacheFolder")
        self.path_entry.pack(side="left", fill="x", expand=True)
        ctk.CTkButton(row, text="Add", width=70,
                      command=self._add_custom_path).pack(side="left", padx=4)
        ctk.CTkButton(row, text="Remove selected", width=130,
                      fg_color="gray35", hover_color="gray25",
                      command=self._remove_custom_path).pack(side="left")
        frame, self.paths_tree = styled_treeview(card, ("Folder",), (700,),
                                                 stretch_col="Folder")
        frame.pack(fill="x", padx=14, pady=(4, 10))
        self._refresh_paths_tree()

        # ---- license, checkout & updates -----------------------------------
        card = self._settings_card(wrap, "License & secure updates")
        self.license_status = ctk.CTkLabel(
            card, text=self.license_mgr.get_tier_display(),
            text_color="#2ECC71" if self.license_mgr.is_pro() else "gray65")
        self.license_status.pack(anchor="w", padx=14, pady=(0, 6))
        row = ctk.CTkFrame(card, fg_color="transparent")
        row.pack(fill="x", padx=14, pady=4)
        self.license_email = ctk.CTkEntry(
            row, width=220, placeholder_text="Purchase email")
        self.license_email.pack(side="left")
        self.license_key = ctk.CTkEntry(
            row, width=300, placeholder_text="License key")
        self.license_key.pack(side="left", padx=6)
        ctk.CTkButton(
            row, text="Activate", width=100,
            command=self._activate_license).pack(side="left")
        row = ctk.CTkFrame(card, fg_color="transparent")
        row.pack(fill="x", padx=14, pady=(2, 10))
        ctk.CTkButton(
            row, text="Buy WinCare Pro", width=150,
            fg_color="#1B6B49", hover_color="#145238",
            command=self._open_checkout).pack(side="left")
        ctk.CTkButton(
            row, text="Check for secure updates", width=190,
            command=self._check_product_update).pack(side="left", padx=6)
        self.update_status = ctk.CTkLabel(
            row,
            text=("Ready" if self.update_client.configured
                  else "Publisher update service not configured"),
            text_color="gray65")
        self.update_status.pack(side="left", padx=8)

        # ---- undo center ------------------------------------------------------
        card = self._settings_card(wrap, "Undo Center")
        ctk.CTkLabel(card, text="Every startup/service change is backed up. "
                     "Restore any of them here.",
                     text_color="gray55", font=ctk.CTkFont(size=11)
                     ).pack(anchor="w", padx=14)
        row = ctk.CTkFrame(card, fg_color="transparent")
        row.pack(fill="x", padx=14, pady=4)
        ctk.CTkButton(row, text="↻ Refresh list", width=110,
                      command=self._refresh_undo).pack(side="left")
        ctk.CTkButton(row, text="Restore selected", width=140,
                      fg_color="#1B6B49", hover_color="#145238",
                      command=self._undo_selected).pack(side="left", padx=6)
        frame, self.undo_tree = styled_treeview(
            card, ("Type", "Item", "Original state"), (100, 260, 340),
            stretch_col="Original state")
        frame.pack(fill="x", padx=14, pady=(4, 10))
        self._refresh_undo()

        # ---- logs & about -------------------------------------------------------
        card = self._settings_card(wrap, "Logs & about")
        row = ctk.CTkFrame(card, fg_color="transparent")
        row.pack(fill="x", padx=14, pady=6)
        ctk.CTkButton(row, text="Open logs folder", width=140,
                      command=lambda: self._open_tool(str(LOG_DIR))
                      ).pack(side="left")
        ctk.CTkButton(row, text="View today's log", width=140,
                      fg_color="gray35", hover_color="gray25",
                      command=self._view_log).pack(side="left", padx=6)
        ctk.CTkButton(row, text="Open reports folder", width=150,
                      fg_color="gray35", hover_color="gray25",
                      command=lambda: self._open_tool(str(REPORT_DIR))
                      ).pack(side="left")
        ctk.CTkLabel(card, text=f"{APP_NAME} v{APP_VERSION} — data folder: "
                     f"{APP_DIR}\nAll actions are logged. Destructive actions "
                     "always require confirmation.",
                     text_color="gray65", justify="left",
                     font=ctk.CTkFont(size=11)).pack(anchor="w", padx=14,
                                                     pady=(2, 10))

    # ---- settings helpers ------------------------------------------------
    def _open_checkout(self):
        try:
            if not open_checkout():
                raise OSError("Windows could not open the checkout page.")
        except (ValueError, OSError) as exc:
            self.notify("Checkout unavailable", str(exc))

    def _activate_license(self):
        key = self.license_key.get().strip()
        email = self.license_email.get().strip()
        if not key:
            self.notify("License key required", "Enter your purchase key.")
            return
        if not self.begin_op("License activation"):
            return
        self.update_status.configure(text="Verifying purchase…")

        def work():
            return self.license_mgr.activate_online(key, email)

        def done(result):
            self.end_op()
            ok, message = result
            self.license_key.delete(0, "end")
            self.license_status.configure(
                text=self.license_mgr.get_tier_display(),
                text_color="#2ECC71" if ok else "#F5A524")
            self.update_status.configure(text="Ready")
            self.notify("License activated" if ok else "Activation failed",
                        message)
        self.run_bg(work, done)

    def _check_product_update(self):
        if not self.update_client.configured:
            self.notify(
                "Updates not configured",
                "Set WINCAREPRO_UPDATE_MANIFEST_URL and "
                "WINCAREPRO_SIGNER_SUBJECT in the release environment.")
            return
        if not self.begin_op("Update check"):
            return
        self.update_status.configure(text="Checking securely…")

        def done(result):
            if isinstance(result, Exception):
                self.end_op()
                self.update_status.configure(text="Update check failed")
                self.notify("Update check failed", str(result))
                return
            if not result.get("available"):
                self.end_op()
                self.update_status.configure(text="Up to date")
                self.notify("Up to date", f"{APP_NAME} v{APP_VERSION} is current.")
                return
            version = result["version"]
            if not self.confirm_changes(
                    "Update available",
                    [f"Download WinCare Pro v{version} over HTTPS",
                     "Verify SHA-256 and trusted Authenticode publisher",
                     "Launch the verified installer"],
                    confirm_text="Download update", danger=False):
                self.end_op()
                self.update_status.configure(text="Update postponed")
                return
            self.update_status.configure(text=f"Downloading v{version}…")

            def downloaded(path):
                self.end_op()
                if isinstance(path, Exception):
                    self.update_status.configure(text="Verification failed")
                    self.notify("Update rejected", str(path))
                    return
                self.update_status.configure(text=f"v{version} verified")
                if self.confirm(
                        "Install verified update",
                        f"Close WinCare Pro and launch the signed v{version} "
                        "installer now?", confirm_text="Install update",
                        danger=False):
                    os.startfile(path)
                    self._on_close()
            self.run_bg(
                lambda: self.update_client.download_and_verify(result),
                downloaded)
        self.run_bg(lambda: self.update_client.check(APP_VERSION), done)

    @staticmethod
    def _settings_card(parent, title):
        card = ctk.CTkFrame(parent, fg_color=CARD_BG, corner_radius=10)
        card.pack(fill="x", pady=6)
        ctk.CTkLabel(card, text=title.upper(), text_color="gray55",
                     font=ctk.CTkFont(size=12, weight="bold")
                     ).pack(anchor="w", padx=14, pady=(10, 4))
        return card

    @staticmethod
    def _settings_number(card, label, value):
        row = ctk.CTkFrame(card, fg_color="transparent")
        row.pack(fill="x", padx=14, pady=4)
        ctk.CTkLabel(row, text=label).pack(side="left")
        entry = ctk.CTkEntry(row, width=80, justify="center")
        entry.insert(0, str(value))
        entry.pack(side="right")
        return entry

    def _set_theme(self, choice):
        self.settings.set("theme", choice)
        ctk.set_appearance_mode(choice)
        self.logger.log("Theme changed", choice)

    def _toggle_browser_cache(self):
        self.settings.set("clean_browser_cache", bool(self.sw_browser.get()))
        self._rebuild_cleanup_list()

    def _toggle_sched_task(self):
        want = bool(self.sw_sched.get())

        def work():
            return scheduled_task_create() if want else scheduled_task_delete()

        def done(ok):
            if ok is not True:
                self.notify("Task Scheduler",
                            "Could not change the scheduled task (it may "
                            "require admin rights).")
                if want:
                    self.sw_sched.deselect()
                else:
                    self.sw_sched.select()
            else:
                self.logger.log("Scheduled task "
                                + ("created" if want else "removed"))
        self.run_bg(work, done)

    def _save_numbers(self):
        try:
            ret = max(1, min(365, int(self.entry_retention.get())))
            inter = max(1, min(90, int(self.entry_interval.get())))
        except ValueError:
            self.notify("Invalid value", "Enter whole numbers of days.")
            return
        self.settings.set("log_retention_days", ret)
        self.settings.set("scan_interval_days", inter)
        self.logger.retention_days = ret
        self._update_banner()
        self.notify("Saved", f"Log retention: {ret} days\n"
                             f"Scan reminder: every {inter} days")

    def _refresh_paths_tree(self):
        for i in self.paths_tree.get_children():
            self.paths_tree.delete(i)
        for p in self.settings.get("custom_clean_paths", []):
            self.paths_tree.insert("", "end", values=(p,))

    def _add_custom_path(self):
        p = self.path_entry.get().strip().strip('"')
        if not p:
            return
        ok, reason = SettingsManager.validate_custom_path(p)
        if not ok:
            self.notify("Path rejected", reason)
            return
        paths = list(self.settings.get("custom_clean_paths", []))
        if p not in paths:
            paths.append(p)
            self.settings.set("custom_clean_paths", paths)
            self.logger.log("Custom cleanup path added", p)
        self._refresh_paths_tree()
        self._rebuild_cleanup_list()
        self.path_entry.delete(0, "end")

    def _remove_custom_path(self):
        sel = self.paths_tree.selection()
        if not sel:
            return
        p = self.paths_tree.set(sel[0], "Folder")
        paths = [x for x in self.settings.get("custom_clean_paths", [])
                 if x != p]
        self.settings.set("custom_clean_paths", paths)
        self._refresh_paths_tree()
        self._rebuild_cleanup_list()

    def _view_log(self):
        self.notify("Today's log (tail)", self.logger.tail(200) or "(empty)")

    # ---- undo center ------------------------------------------------------
    def _refresh_undo(self):
        for i in self.undo_tree.get_children():
            self.undo_tree.delete(i)
        self._undo_rows = []
        for name, state in self.backup.data.get("services", {}).items():
            self._undo_rows.append(("services", name, state))
            self.undo_tree.insert("", "end", iid=str(len(self._undo_rows) - 1),
                                  values=("Service", name,
                                          f"start type: {state.get('start_type')}"))
        for key, state in self.backup.data.get("startup", {}).items():
            self._undo_rows.append(("startup", key, state))
            self.undo_tree.insert("", "end", iid=str(len(self._undo_rows) - 1),
                                  values=("Startup", key,
                                          state.get("command", "")[:80]))

    def _undo_selected(self):
        sel = self.undo_tree.selection()
        if not sel:
            self.notify("No selection", "Select a backed-up change first.")
            return
        try:
            category, key, state = self._undo_rows[int(sel[0])]
        except (ValueError, IndexError):
            return
        if category == "services":
            ok, msg = self.optimizer.set_service(key, False, lambda l: None)
        else:
            ok, msg = self._undo_startup(key, state)
        if ok:
            self.backup.data.get(category, {}).pop(key, None)
            self.backup.save()
            self._refresh_undo()
            self._refresh_startup()
        self.notify("Undo " + ("succeeded" if ok else "failed"), msg)

    def _undo_startup(self, key, state):
        """Re-enable a startup entry from its backup record."""
        name = key.split("\\", 1)[1] if "\\" in key else key
        source = state.get("source", "HKCU")
        command = state.get("command", "")
        if source.startswith("Folder"):
            parked = Optimizer.DISABLED_FOLDER / Path(command).name
            try:
                if parked.exists():
                    shutil.move(str(parked), command)
                    self.logger.log("Startup folder item restored", command)
                    return True, f"Restored: {command}"
                return False, "Parked file no longer exists."
            except (OSError, shutil.Error) as e:
                return False, f"Move failed: {e}"
        item = {"name": name, "command": command, "source": source,
                "enabled": False, "impact": "Normal"}
        return self.optimizer.set_startup_enabled(item, True)


# ============================================================================
# ENTRY POINT
# ============================================================================
def _single_instance_or_die():
    """Refuse to run twice — two instances deleting files concurrently is
    exactly the kind of race a maintenance tool must never allow."""
    try:
        kernel32 = ctypes.windll.kernel32
        # CreateMutexW returns a handle; GetLastError must be checked
        # immediately — before any other Win32 call (including Python's
        # internal SetLastError resets) — otherwise the error code is stale
        # and we get false "already running" detections.
        mutex_name = "Local\\" + "WinCareProSingletonMutex_v1"
        handle = kernel32.CreateMutexW(None, False, mutex_name)
        error = kernel32.GetLastError()
        # Keep the handle alive for the lifetime of the process so the
        # mutex is not released when the handle is garbage collected.
        _mutex_handle = handle
        if error == 183:   # ERROR_ALREADY_EXISTS
            try:
                user32 = ctypes.windll.user32
                hwnd_holder = {"hwnd": None}

                @ctypes.WINFUNCTYPE(ctypes.c_bool, ctypes.c_void_p, ctypes.c_void_p)
                def enum_windows_proc(hwnd, _lparam):
                    length = user32.GetWindowTextLengthW(hwnd)
                    if length <= 0:
                        return True
                    buffer = ctypes.create_unicode_buffer(length + 1)
                    user32.GetWindowTextW(hwnd, buffer, length + 1)
                    title = buffer.value
                    if APP_NAME in title:
                        hwnd_holder["hwnd"] = hwnd
                        return False
                    return True

                user32.EnumWindows(enum_windows_proc, 0)
                hwnd = hwnd_holder["hwnd"]
                if hwnd:
                    user32.ShowWindow(hwnd, 9)
                    user32.SetForegroundWindow(hwnd)
            except Exception:
                from tkinter import messagebox
                r = tk.Tk(); r.withdraw()
                messagebox.showwarning(APP_NAME,
                                       "WinCare Pro is already running.")
                r.destroy()
            sys.exit(0)
    except AttributeError:
        pass  # non-Windows lint runs


def _report_crash(logger: AppLogger, text: str, fatal: bool):
    """Crash reporting must be LOUD: log file + console + dialog.
    A silent exit teaches the user nothing and hides defects."""
    logger.log("UNCAUGHT EXCEPTION", text, "ERROR")
    print(text, file=sys.stderr)
    if fatal:
        try:
            from tkinter import messagebox
            r = tk.Tk(); r.withdraw()
            messagebox.showerror(
                APP_NAME,
                "WinCare Pro hit an unexpected error and must close.\n\n"
                + text.strip().splitlines()[-1]
                + f"\n\nFull details were saved to:\n{LOG_DIR}")
            r.destroy()
        except Exception:
            pass


def _install_crash_handlers(logger: AppLogger):
    """Log-print-and-survive for uncaught exceptions on any thread."""
    def hook(exc_type, exc, tb):
        _report_crash(logger, "".join(
            traceback.format_exception(exc_type, exc, tb)), fatal=True)
    sys.excepthook = hook

    def thread_hook(args):
        _report_crash(logger, "".join(traceback.format_exception(
            args.exc_type, args.exc_value, args.exc_traceback)), fatal=False)
    threading.excepthook = thread_hook


def main():
    if not IS_WINDOWS:
        print("WinCare Pro is a Windows 11 application. This OS is not "
              "supported.")
        sys.exit(1)
    _single_instance_or_die()
    # Per-monitor DPI awareness for crisp text on modern displays.
    try:
        ctypes.windll.shcore.SetProcessDpiAwareness(1)
    except (AttributeError, OSError):
        pass
    crash_logger = AppLogger()
    _install_crash_handlers(crash_logger)
    try:
        app = WinCareApp()
        app.mainloop()
    except Exception:                          # startup/runtime failure
        _report_crash(crash_logger, traceback.format_exc(), fatal=True)
        sys.exit(1)


if __name__ == "__main__":
    main()
# EOF
