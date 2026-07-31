#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
================================================================================
 WinCare Pro - Performance Booster Module
================================================================================
 Module : performance_booster.py
 Scope  :
  1. RAM Standby & Process Working Set Flusher (ctypes Windows API)
  2. Fast DNS Switcher (Cloudflare, Google, Quad9, DHCP auto)
  3. TCP Gaming Latency Optimizer (Nagle's Algorithm / TcpAckFrequency toggle)
================================================================================
"""

import os
import sys
import json
import ctypes
from ctypes import wintypes
import subprocess
import winreg
import psutil
from typing import Callable, Optional, Tuple, Dict, Any, List

CREATE_NO_WINDOW = 0x08000000 if os.name == "nt" else 0

# DNS Preset Definitions
DNS_PRESETS = {
    "cloudflare": {
        "name": "Cloudflare (1.1.1.1 / 1.0.0.1)",
        "servers": ["1.1.1.1", "1.0.0.1"]
    },
    "google": {
        "name": "Google Public DNS (8.8.8.8 / 8.8.4.4)",
        "servers": ["8.8.8.8", "8.8.4.4"]
    },
    "quad9": {
        "name": "Quad9 Secure (9.9.9.9 / 149.112.112.112)",
        "servers": ["9.9.9.9", "149.112.112.112"]
    },
    "dhcp": {
        "name": "Automatic (DHCP)",
        "servers": []
    }
}


def is_admin() -> bool:
    """Check if the current process is running with Administrator privileges."""
    try:
        return ctypes.windll.shell32.IsUserAnAdmin() != 0
    except Exception:
        return False


def _log(msg: str, callback_out: Optional[Callable[[str], None]] = None) -> None:
    """Internal helper to emit log output to callback or stdout."""
    formatted = f"[PerformanceBooster] {msg}"
    if callback_out:
        try:
            callback_out(formatted)
        except Exception:
            pass
    else:
        print(formatted)


class PerformanceBooster:
    """
    Performance Booster engine for WinCare Pro.
    Provides low-level system optimizations:
      - Process working set trimming and memory standby list clearing
      - Fast DNS server switching and DNS cache flushing
      - Registry-level TCP ACK frequency and Nagle's algorithm tweaks for gaming latency
    """

    def __init__(self):
        pass

    # --------------------------------------------------------------------------
    # 1. RAM STANDBY & WORKING SET FLUSHER
    # --------------------------------------------------------------------------
    def _enable_privilege(self, privilege_name: str) -> bool:
        """Enable specific token privilege (e.g. SeProfileSingleProcessPrivilege) via ctypes."""
        if os.name != "nt":
            return False

        TOKEN_ADJUST_PRIVILEGES = 0x0020
        TOKEN_QUERY = 0x0008
        SE_PRIVILEGE_ENABLED = 0x00000002

        class LUID(ctypes.Structure):
            _fields_ = [("LowPart", wintypes.DWORD), ("HighPart", wintypes.LONG)]

        class LUID_AND_ATTRIBUTES(ctypes.Structure):
            _fields_ = [("Luid", LUID), ("Attributes", wintypes.DWORD)]

        class TOKEN_PRIVILEGES(ctypes.Structure):
            _fields_ = [("PrivilegeCount", wintypes.DWORD), ("Privileges", LUID_AND_ATTRIBUTES * 1)]

        try:
            hToken = wintypes.HANDLE()
            if not ctypes.windll.advapi32.OpenProcessToken(
                ctypes.windll.kernel32.GetCurrentProcess(),
                TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY,
                ctypes.byref(hToken)
            ):
                return False

            luid = LUID()
            if not ctypes.windll.advapi32.LookupPrivilegeValueW(None, privilege_name, ctypes.byref(luid)):
                ctypes.windll.kernel32.CloseHandle(hToken)
                return False

            tp = TOKEN_PRIVILEGES()
            tp.PrivilegeCount = 1
            tp.Privileges[0].Luid = luid
            tp.Privileges[0].Attributes = SE_PRIVILEGE_ENABLED

            res = ctypes.windll.advapi32.AdjustTokenPrivileges(
                hToken, False, ctypes.byref(tp), ctypes.sizeof(tp), None, None
            )
            ctypes.windll.kernel32.CloseHandle(hToken)
            return res != 0
        except Exception:
            return False

    def flush_ram_standby_list(self, callback_out: Optional[Callable[[str], None]] = None) -> Tuple[bool, str]:
        """
        Trims process working sets and flushes memory standby lists.
        Uses ctypes Windows API calls (EmptyWorkingSet / SetProcessWorkingSetSize / NtSetSystemInformation).

        Returns (success: bool, message: str)
        """
        if os.name != "nt":
            msg = "RAM flushing is only supported on Windows OS."
            _log(msg, callback_out)
            return False, msg

        _log("Starting RAM Standby & Working Set cleanup...", callback_out)

        mem_before = psutil.virtual_memory()
        avail_before_mb = mem_before.available / (1024 * 1024)
        _log(f"Initial Available Memory: {avail_before_mb:.2f} MB (Used: {mem_before.percent}%)", callback_out)

        PROCESS_SET_QUOTA = 0x0100
        PROCESS_QUERY_INFORMATION = 0x0400

        psapi = getattr(ctypes.windll, "psapi", None)
        kernel32 = getattr(ctypes.windll, "kernel32", None)

        trimmed_count = 0
        total_procs = 0

        # Trim Working Sets for all accessible processes
        for proc in psutil.process_iter(['pid', 'name']):
            total_procs += 1
            pid = proc.info['pid']
            if pid in (0, 4):  # System idle / System process
                continue

            try:
                if kernel32:
                    h_proc = kernel32.OpenProcess(PROCESS_SET_QUOTA | PROCESS_QUERY_INFORMATION, False, pid)
                    if h_proc:
                        success = False
                        if psapi and hasattr(psapi, "EmptyWorkingSet"):
                            success = psapi.EmptyWorkingSet(h_proc) != 0

                        if not success:
                            # Fallback to SetProcessWorkingSetSize
                            success = kernel32.SetProcessWorkingSetSize(
                                h_proc, ctypes.c_size_t(-1), ctypes.c_size_t(-1)
                            ) != 0

                        if success:
                            trimmed_count += 1

                        kernel32.CloseHandle(h_proc)
            except Exception:
                pass

        _log(f"Trimmed working set for {trimmed_count} accessible processes.", callback_out)

        # Attempt to purge Standby List via NtSetSystemInformation if elevated
        standby_flushed = False
        if is_admin():
            try:
                self._enable_privilege("SeProfileSingleProcessPrivilege")
                self._enable_privilege("SeIncreaseQuotaPrivilege")

                # SystemMemoryListInformation = 80, MemoryPurgeStandbyList = 4
                command = ctypes.c_int(4)
                ntdll = getattr(ctypes.windll, "ntdll", None)
                if ntdll and hasattr(ntdll, "NtSetSystemInformation"):
                    status = ntdll.NtSetSystemInformation(80, ctypes.byref(command), ctypes.sizeof(command))
                    if status == 0:
                        standby_flushed = True
                        _log("System memory standby list purged successfully (NtSetSystemInformation).", callback_out)
                    else:
                        _log(f"NtSetSystemInformation standby list purge returned status code: {status}", callback_out)
            except Exception as e:
                _log(f"Standby list purge notice: {e}", callback_out)

        mem_after = psutil.virtual_memory()
        avail_after_mb = mem_after.available / (1024 * 1024)
        freed_mb = avail_after_mb - avail_before_mb

        if freed_mb < 0:
            freed_mb = 0.0

        detail_msg = f"RAM Cleanup Complete. Freed ~{freed_mb:.1f} MB RAM. " \
                     f"Trimmed {trimmed_count} processes."
        if standby_flushed:
            detail_msg += " System standby list purged."

        _log(detail_msg, callback_out)
        return True, detail_msg

    # --------------------------------------------------------------------------
    # 2. FAST DNS SWITCHER & CURRENT DNS DETECTOR
    # --------------------------------------------------------------------------
    def get_current_dns(self) -> Dict[str, Any]:
        """
        Retrieves active DNS configuration across network adapters on the system.

        Returns dict structure:
        {
            "adapter": str,
            "primary": str,
            "secondary": str,
            "dns_type": str,
            "adapters": list[dict]
        }
        """
        result = {
            "adapter": "N/A",
            "primary": "",
            "secondary": "",
            "dns_type": "unknown",
            "adapters": []
        }

        if os.name != "nt":
            return result

        ps_script = (
            "Get-DnsClientServerAddress -AddressFamily IPv4 | "
            "Select-Object InterfaceAlias, InterfaceIndex, ServerAddresses | "
            "ConvertTo-Json"
        )
        try:
            res = subprocess.run(
                ["powershell", "-NoProfile", "-NonInteractive", "-Command", ps_script],
                capture_output=True,
                text=True,
                creationflags=CREATE_NO_WINDOW,
                timeout=10
            )
            if res.returncode == 0 and res.stdout.strip():
                data = json.loads(res.stdout)
                if isinstance(data, dict):
                    data = [data]

                for item in data:
                    alias = item.get("InterfaceAlias", "Unknown")
                    servers = item.get("ServerAddresses") or []

                    if isinstance(servers, str):
                        servers = [servers]

                    adapter_entry = {
                        "name": alias,
                        "servers": servers
                    }
                    result["adapters"].append(adapter_entry)

                    # Mark first adapter with servers configured as active summary
                    if servers and not result["primary"]:
                        result["adapter"] = alias
                        result["primary"] = servers[0] if len(servers) > 0 else ""
                        result["secondary"] = servers[1] if len(servers) > 1 else ""

            # Classify DNS preset type
            p_dns = result["primary"]
            s_dns = result["secondary"]

            if p_dns == "1.1.1.1" or s_dns == "1.0.0.1":
                result["dns_type"] = "cloudflare"
            elif p_dns == "8.8.8.8" or s_dns == "8.8.4.4":
                result["dns_type"] = "google"
            elif p_dns == "9.9.9.9" or s_dns == "149.112.112.112":
                result["dns_type"] = "quad9"
            elif not p_dns:
                result["dns_type"] = "dhcp"
            else:
                result["dns_type"] = "custom"

        except Exception as e:
            result["error"] = str(e)

        return result

    def set_dns_servers(self, dns_type: str, callback_out: Optional[Callable[[str], None]] = None) -> Tuple[bool, str]:
        """
        Configures system IPv4 DNS servers.

        dns_type: 'cloudflare' | 'google' | 'quad9' | 'dhcp' (case-insensitive)
        Uses PowerShell Set-DnsClientServerAddress / netsh interface ip set dns.
        Flushes DNS cache on completion.

        Returns (success: bool, message: str)
        """
        if os.name != "nt":
            msg = "DNS configuration is only supported on Windows."
            _log(msg, callback_out)
            return False, msg

        key = dns_type.lower().strip()
        if key not in DNS_PRESETS:
            valid_keys = ", ".join(DNS_PRESETS.keys())
            msg = f"Invalid dns_type '{dns_type}'. Supported choices: {valid_keys}"
            _log(msg, callback_out)
            return False, msg

        preset = DNS_PRESETS[key]
        _log(f"Applying DNS preset: {preset['name']}...", callback_out)

        # Retrieve active connected network adapters
        ps_get_adapters = (
            "Get-NetAdapter | Where-Object Status -eq 'Up' | "
            "Select-Object -ExpandProperty Name"
        )
        try:
            res = subprocess.run(
                ["powershell", "-NoProfile", "-NonInteractive", "-Command", ps_get_adapters],
                capture_output=True,
                text=True,
                creationflags=CREATE_NO_WINDOW,
                timeout=10
            )
            adapters = [line.strip() for line in res.stdout.splitlines() if line.strip()]
        except Exception as e:
            msg = f"Failed to detect active network adapters: {e}"
            _log(msg, callback_out)
            return False, msg

        if not adapters:
            msg = "No active connected network interfaces found."
            _log(msg, callback_out)
            return False, msg

        _log(f"Target network interfaces: {', '.join(adapters)}", callback_out)

        updated_adapters = []
        errors = []

        for adapter in adapters:
            success = False
            safe_adapter = adapter.replace("'", "''")

            if key == "dhcp":
                # Reset to DHCP automatic DNS
                ps_cmd = (
                    f"Set-DnsClientServerAddress -InterfaceAlias "
                    f"'{safe_adapter}' -ResetServerAddresses"
                )
                try:
                    r = subprocess.run(
                        ["powershell", "-NoProfile", "-NonInteractive", "-Command", ps_cmd],
                        capture_output=True,
                        text=True,
                        creationflags=CREATE_NO_WINDOW,
                        timeout=10
                    )
                    if r.returncode == 0:
                        success = True
                    else:
                        # Fallback to netsh
                        r2 = subprocess.run(
                            ["netsh", "interface", "ip", "set", "dns",
                             f"name={adapter}", "dhcp"],
                            shell=False, capture_output=True, text=True,
                            creationflags=CREATE_NO_WINDOW, timeout=10
                        )
                        if r2.returncode == 0:
                            success = True
                        else:
                            errors.append(f"{adapter}: {r.stderr.strip() or r2.stderr.strip()}")
                except Exception as ex:
                    errors.append(f"{adapter}: {ex}")

            else:
                # Set static DNS addresses
                servers = preset["servers"]
                formatted_servers = ', '.join([f'"{s}"' for s in servers])
                ps_cmd = (
                    f"Set-DnsClientServerAddress -InterfaceAlias "
                    f"'{safe_adapter}' -ServerAddresses ({formatted_servers})"
                )
                try:
                    r = subprocess.run(
                        ["powershell", "-NoProfile", "-NonInteractive", "-Command", ps_cmd],
                        capture_output=True,
                        text=True,
                        creationflags=CREATE_NO_WINDOW,
                        timeout=10
                    )
                    if r.returncode == 0:
                        success = True
                    else:
                        # Netsh fallback
                        fallback_ok = True
                        if len(servers) > 0:
                            r1 = subprocess.run(
                                ["netsh", "interface", "ip", "set", "dns",
                                 f"name={adapter}", "static", servers[0]],
                                shell=False, capture_output=True, text=True,
                                creationflags=CREATE_NO_WINDOW, timeout=10)
                            fallback_ok = fallback_ok and r1.returncode == 0
                        if len(servers) > 1:
                            r2 = subprocess.run(
                                ["netsh", "interface", "ip", "add", "dns",
                                 f"name={adapter}", servers[1], "index=2"],
                                shell=False, capture_output=True, text=True,
                                creationflags=CREATE_NO_WINDOW, timeout=10)
                            fallback_ok = fallback_ok and r2.returncode == 0
                        success = fallback_ok
                        if not success:
                            errors.append(
                                f"{adapter}: PowerShell and netsh both failed")
                except Exception as ex:
                    errors.append(f"{adapter}: {ex}")

            if success:
                updated_adapters.append(adapter)
                _log(f"Updated DNS for interface '{adapter}'.", callback_out)

        # Flush DNS cache
        try:
            _log("Flushing DNS Resolver Cache (ipconfig /flushdns)...", callback_out)
            subprocess.run(
                ["ipconfig", "/flushdns"],
                capture_output=True,
                creationflags=CREATE_NO_WINDOW,
                timeout=10
            )
        except Exception:
            pass

        if updated_adapters:
            msg = f"Successfully set DNS to '{preset['name']}' on {len(updated_adapters)} interface(s)."
            _log(msg, callback_out)
            return True, msg
        else:
            err_str = "; ".join(errors) if errors else "Access denied or PowerShell policy restriction."
            msg = f"Failed to update DNS settings: {err_str}"
            _log(msg, callback_out)
            return False, msg

    # --------------------------------------------------------------------------
    # 3. TCP GAMING LATENCY OPTIMIZER
    # --------------------------------------------------------------------------
    def optimize_tcp_gaming_latency(self, enable: bool, callback_out: Optional[Callable[[str], None]] = None) -> Tuple[bool, str]:
        """
        Toggles Nagle's Algorithm registry parameters for lower TCP latency in gaming.
        Target Registry Path: HKLM\\SYSTEM\\CurrentControlSet\\Services\\Tcpip\\Parameters\\Interfaces\\{GUID}

        Parameters toggled:
          - TcpAckFrequency = 1 (Disables ACK delay)
          - TCPNoDelay = 1 (Disables Nagle's buffering algorithm)
          - TcpDelAckTicks = 0 (Disables delayed ACK timer)

        When enable=False, deletes these registry values to restore Windows default behavior.

        Returns (success: bool, message: str)
        """
        if os.name != "nt":
            msg = "TCP Gaming Latency optimization is only supported on Windows."
            _log(msg, callback_out)
            return False, msg

        action_str = "Enabling" if enable else "Disabling (Restoring Defaults for)"
        _log(f"{action_str} TCP Gaming Latency Optimizations...", callback_out)

        base_path = r"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces"

        try:
            h_base = winreg.OpenKey(
                winreg.HKEY_LOCAL_MACHINE,
                base_path,
                0,
                winreg.KEY_READ
            )
        except PermissionError:
            msg = "Access Denied: Administrator rights required to modify TCP registry settings."
            _log(msg, callback_out)
            return False, msg
        except Exception as e:
            msg = f"Failed to open TCP interfaces registry key: {e}"
            _log(msg, callback_out)
            return False, msg

        modified_count = 0
        error_count = 0
        i = 0

        while True:
            try:
                guid_subkey = winreg.EnumKey(h_base, i)
                i += 1
            except OSError:
                break  # End of subkeys

            subkey_path = f"{base_path}\\{guid_subkey}"
            try:
                # Open subkey with WRITE access
                h_interface = winreg.OpenKey(
                    winreg.HKEY_LOCAL_MACHINE,
                    subkey_path,
                    0,
                    winreg.KEY_SET_VALUE | winreg.KEY_READ
                )

                if enable:
                    # Apply low-latency gaming parameters
                    winreg.SetValueEx(h_interface, "TcpAckFrequency", 0, winreg.REG_DWORD, 1)
                    winreg.SetValueEx(h_interface, "TCPNoDelay", 0, winreg.REG_DWORD, 1)
                    winreg.SetValueEx(h_interface, "TcpDelAckTicks", 0, winreg.REG_DWORD, 0)
                else:
                    # Delete custom parameters to restore default behavior
                    for val_name in ("TcpAckFrequency", "TCPNoDelay", "TcpDelAckTicks"):
                        try:
                            winreg.DeleteValue(h_interface, val_name)
                        except FileNotFoundError:
                            pass

                winreg.CloseKey(h_interface)
                modified_count += 1

            except PermissionError:
                error_count += 1
            except Exception:
                error_count += 1

        winreg.CloseKey(h_base)

        if modified_count > 0:
            state_label = "ENABLED" if enable else "DISABLED (Restored to default)"
            msg = f"TCP Gaming Latency optimization successfully {state_label} across {modified_count} network interfaces."
            _log(msg, callback_out)
            return True, msg
        elif error_count > 0:
            msg = "Failed to modify TCP registry settings: Administrator privileges required."
            _log(msg, callback_out)
            return False, msg
        else:
            msg = "No TCP network interfaces found in registry."
            _log(msg, callback_out)
            return False, msg


# ------------------------------------------------------------------------------
# STANDALONE EXECUTION VERIFICATION
# ------------------------------------------------------------------------------
if __name__ == "__main__":
    print("=========================================================================")
    print(" WinCare Pro - Performance Booster Standalone Verification")
    print("=========================================================================")

    booster = PerformanceBooster()

    # 1. Test Get Current DNS
    print("\n[1/3] Testing get_current_dns()...")
    dns_info = booster.get_current_dns()
    print(f"Current DNS Info:\n{json.dumps(dns_info, indent=2)}")

    # 2. Test RAM Flushing
    print("\n[2/3] Testing flush_ram_standby_list()...")
    ok_ram, msg_ram = booster.flush_ram_standby_list()
    print(f"Result: {ok_ram} | Message: {msg_ram}")

    # 3. Test TCP Gaming Latency Optimizer (Read / Check admin status)
    print("\n[3/3] Testing optimize_tcp_gaming_latency()...")
    if is_admin():
        ok_tcp, msg_tcp = booster.optimize_tcp_gaming_latency(enable=True)
        print(f"Result (Enable): {ok_tcp} | Message: {msg_tcp}")
    else:
        print("Note: Skipping registry write test because process is running non-elevated.")

    print("\n=========================================================================")
    print(" Standalone Verification Complete!")
    print("=========================================================================")
