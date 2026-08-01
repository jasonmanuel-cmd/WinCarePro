#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
================================================================================
 WinCare Pro - Advanced Security Scanner & Malware Threat Hunter
================================================================================
 Audits running processes, network activity, and startup persistence registry keys
 for advanced signs of masquerading, unsigned execution, C2 beacons, and double-extension trojans.
================================================================================
"""

import os
import json
import datetime
import subprocess
from pathlib import Path

import psutil

try:
    import winreg
except ImportError:
    winreg = None

CREATE_NO_WINDOW = 0x08000000 if os.name == "nt" else 0


class SecurityScanner:
    """
    Advanced Heuristic & Security Scanner for WinCare Pro.
    Identifies process masquerading, unauthorized network sockets,
    unsigned user-space executables, and registry persistence threats.
    """

    CORE_SYSTEM_BASELINES = {
        "svchost.exe": ["system32", "syswow64", "winsxs"],
        "lsass.exe": ["system32"],
        "csrss.exe": ["system32"],
        "wininit.exe": ["system32"],
        "services.exe": ["system32"],
        "smss.exe": ["system32"],
        "explorer.exe": ["", "system32"],
        "dwm.exe": ["system32"],
        "ctfmon.exe": ["system32"],
        "sihost.exe": ["system32"],
        "taskhostw.exe": ["system32"],
    }

    def __init__(self, logger=None):
        self.logger = logger

    def _log(self, action: str, detail: str = "", level: str = "INFO"):
        if self.logger:
            self.logger.log(action, detail, level)
        else:
            print(f"[{level}] {action}: {detail}")

    def verify_signature_offline(self, file_path: str) -> dict:
        """Queries Authenticode signature using native PowerShell."""
        if os.name != "nt" or not file_path or not os.path.exists(file_path):
            return {"status": "NotSigned", "signer": "N/A", "valid": False}

        safe_path = file_path.replace("'", "''")
        ps_script = (
            f"$s=Get-AuthenticodeSignature -LiteralPath '{safe_path}';"
            f"[pscustomobject]@{{"
            f"Status=$s.Status.ToString();"
            f"Subject=$s.SignerCertificate.Subject"
            f"}}|ConvertTo-Json -Compress"
        )
        try:
            p = subprocess.run(
                ["powershell", "-NoProfile", "-NonInteractive", "-Command", ps_script],
                capture_output=True, text=True, timeout=10, creationflags=CREATE_NO_WINDOW
            )
            if p.returncode == 0 and p.stdout.strip():
                data = json.loads(p.stdout)
                status_str = str(data.get("Status", "Unknown"))
                subject = str(data.get("Subject", "Unknown"))
                valid = status_str == "Valid"
                return {"status": status_str, "signer": subject, "valid": valid}
        except Exception:
            pass

        return {"status": "Unknown", "signer": "N/A", "valid": False}

    def scan_processes(self, callback_out=None) -> list[dict]:
        """Scans all running processes for threats."""
        def log(msg: str):
            if callback_out:
                callback_out(msg)

        log("[*] Initializing Running Process Security Scan...")
        findings = []

        win_dir = os.environ.get("WINDIR", "C:\\Windows").lower()
        user_profile = os.environ.get("USERPROFILE", "C:\\Users").lower()
        temp_dir = os.environ.get("TEMP", "").lower()
        appdata_dir = os.environ.get("APPDATA", "").lower()

        connections_by_pid = {}
        try:
            for conn in psutil.net_connections(kind="inet"):
                if conn.pid and conn.status == "ESTABLISHED":
                    r_ip, r_port = conn.raddr
                    if not r_ip.startswith(("127.", "192.168.", "10.", "172.16.")):
                        connections_by_pid.setdefault(conn.pid, []).append(f"{r_ip}:{r_port}")
        except Exception as e:
            log(f"[-] Notice: Could not map network sockets: {e}")

        for proc in psutil.process_iter(["pid", "name", "exe", "username"]):
            try:
                pid = proc.info["pid"]
                name = proc.info["name"] or ""
                exe = proc.info["exe"] or ""

                if not exe or pid in (0, 4):
                    continue

                exe_lower = exe.lower()
                name_lower = name.lower()

                if name_lower in self.CORE_SYSTEM_BASELINES:
                    valid_folders = self.CORE_SYSTEM_BASELINES[name_lower]
                    matched_location = False
                    for sub in valid_folders:
                        target_dir = os.path.join(win_dir, sub).lower().rstrip("\\")
                        if exe_lower.startswith(target_dir):
                            matched_location = True
                            break

                    if not matched_location:
                        findings.append({
                            "type": "Critical",
                            "category": "Process Masquerading",
                            "pid": pid,
                            "name": name,
                            "path": exe,
                            "details": f"CORE SYSTEM PROCESS masqueraded! Running outside legitimate Windows folder: {exe}",
                            "action": "Immediate termination and threat scan recommended."
                        })
                        continue

                name_parts = name_lower.split(".")
                if len(name_parts) >= 3 and name_parts[-1] == "exe":
                    double_ext = name_parts[-2]
                    if double_ext in ("pdf", "txt", "docx", "xlsx", "zip", "png", "jpg"):
                        findings.append({
                            "type": "Critical",
                            "category": "Double Extension Trojan",
                            "pid": pid,
                            "name": name,
                            "path": exe,
                            "details": f"Executable uses a deceptive file name: '{name}'. Matches double extension trick.",
                            "action": "Terminate process immediately and quarantine."
                        })
                        continue

                is_writable_user_path = False
                if temp_dir and temp_dir in exe_lower:
                    is_writable_user_path = True
                elif appdata_dir and appdata_dir in exe_lower:
                    is_writable_user_path = True
                elif user_profile and user_profile in exe_lower and ("\\downloads\\" in exe_lower or "\\desktop\\" in exe_lower):
                    is_writable_user_path = True
                elif "c:\\programdata\\" in exe_lower:
                    is_writable_user_path = True

                if is_writable_user_path:
                    sig_info = self.verify_signature_offline(exe)
                    if not sig_info["valid"]:
                        findings.append({
                            "type": "Warning",
                            "category": "Unsigned Writable Execution",
                            "pid": pid,
                            "name": name,
                            "path": exe,
                            "details": f"Unsigned third-party executable running from a writable directory: {exe}. Signer: {sig_info['signer']}",
                            "action": "Verify if this file belongs to a trusted tool, otherwise terminate."
                        })
                        continue

                if pid in connections_by_pid:
                    ext_conns = connections_by_pid[pid]
                    sig_info = self.verify_signature_offline(exe)
                    if not sig_info["valid"] and not exe_lower.startswith(win_dir):
                        findings.append({
                            "type": "Warning",
                            "category": "Suspicious External Beacon",
                            "pid": pid,
                            "name": name,
                            "path": exe,
                            "details": f"Unsigned non-system executable has active external network connection(s) to: {', '.join(ext_conns)}",
                            "action": "Severe risk of data exfiltration or reverse shell beacon. Terminate connection immediately."
                        })
                        continue

            except (psutil.NoSuchProcess, psutil.AccessDenied, psutil.ZombieProcess):
                continue

        log(f"[+] Process scan finished. Found {len(findings)} security findings.")
        return findings

    def scan_startup_persistence(self, callback_out=None) -> list[dict]:
        """Audits standard startup registry keys for suspicious, unsigned commands."""
        def log(msg: str):
            if callback_out:
                callback_out(msg)

        log("[*] Scanning Startup Persistence registry locations...")
        findings = []

        if os.name != "nt" or winreg is None:
            return findings

        paths = [
            (winreg.HKEY_LOCAL_MACHINE, r"Software\Microsoft\Windows\CurrentVersion\Run"),
            (winreg.HKEY_CURRENT_USER, r"Software\Microsoft\Windows\CurrentVersion\Run"),
            (winreg.HKEY_LOCAL_MACHINE, r"Software\Microsoft\Windows\CurrentVersion\RunOnce"),
            (winreg.HKEY_CURRENT_USER, r"Software\Microsoft\Windows\CurrentVersion\RunOnce")
        ]

        for hkey, subkey in paths:
            hive_str = "HKLM" if hkey == winreg.HKEY_LOCAL_MACHINE else "HKCU"
            try:
                key = winreg.OpenKey(hkey, subkey, 0, winreg.KEY_READ)
                count = 0
                while True:
                    try:
                        name, val, _ = winreg.EnumValue(key, count)
                        count += 1

                        cmd_parts = val.strip().split()
                        if not cmd_parts:
                            continue

                        potential_path = cmd_parts[0].strip('"')
                        if potential_path.endswith(".exe") and os.path.exists(potential_path):
                            sig_info = self.verify_signature_offline(potential_path)
                            if not sig_info["valid"]:
                                findings.append({
                                    "type": "Warning",
                                    "category": "Unsigned Startup Entry",
                                    "location": f"{hive_str}\\{subkey}",
                                    "name": name,
                                    "cmd": val,
                                    "details": f"Unsigned binary '{name}' scheduled at Windows startup: {val}",
                                    "action": "Disable this entry in Settings or startup config."
                                })
                    except OSError:
                        break
                winreg.CloseKey(key)
            except Exception:
                pass

        log(f"[+] Startup registry scan completed. Found {len(findings)} entry warnings.")
        return findings

    def run_security_suite(self, callback_out=None) -> dict:
        """Main runner function that combines audits into one threat report."""
        proc_threats = self.scan_processes(callback_out)
        startup_threats = self.scan_startup_persistence(callback_out)

        total_findings = proc_threats + startup_threats
        critical_count = sum(1 for f in total_findings if f.get("type") == "Critical")
        warning_count = sum(1 for f in total_findings if f.get("type") == "Warning")

        score_deductions = (critical_count * 25) + (warning_count * 10)
        security_score = max(0, 100 - score_deductions)

        return {
            "score": security_score,
            "proc_threats": proc_threats,
            "startup_threats": startup_threats,
            "total_count": len(total_findings),
            "critical_count": critical_count,
            "warning_count": warning_count,
            "timestamp": datetime.datetime.now().strftime("%Y-%m-%d %H:%M:%S")
        }