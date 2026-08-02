# -*- coding: utf-8 -*-
"""
WinCare Pro - safe subprocess / PowerShell execution helpers.

SECURITY MODEL (CWE-78 / command injection):
Never interpolate untrusted strings into a PowerShell script body. Every
caller-supplied value is passed POSITIONALLY on the command line after the
script, and read inside the script via $args[0], $args[1], ... Combined with
-LiteralPath (so wildcard / metacharacters are treated literally), a value
containing quotes or $() can never break out into a second command.

Reference implementations of this exact pattern live in:
    disk_analyzer.py, win_baseline.py, updater.py
"""

import json
import os
import subprocess

IS_WINDOWS = (os.name == "nt")

# subprocess flag: never flash a console window behind the GUI.
CREATE_NO_WINDOW = 0x08000000 if IS_WINDOWS else 0

_POWERSHELL = ["powershell", "-NoProfile", "-NonInteractive",
               "-ExecutionPolicy", "Bypass"]


def run_cmd(cmd, timeout=120):
    """
    Run a command silently, return (returncode, merged stdout+stderr text).
    Used for short, non-interactive queries.
    """
    try:
        p = subprocess.run(
            cmd, shell=False, capture_output=True, text=True,
            encoding="utf-8", errors="ignore", timeout=timeout,
            creationflags=CREATE_NO_WINDOW,
        )
        out = (p.stdout or "") + (("\n" + p.stderr) if p.stderr else "")
        return p.returncode, out.replace("\x00", "").strip()
    except subprocess.TimeoutExpired:
        return -1, f"[timeout after {timeout}s]"
    except Exception as e:
        return -2, f"[error] {e}"


def run_ps(script, timeout=120):
    """Run a PowerShell snippet (no args) and return (returncode, output)."""
    return run_cmd(_POWERSHELL + ["-Command", script], timeout=timeout)


def safe_ps(script, *args, timeout=120):
    """
    Run a PowerShell snippet with caller-supplied values passed as
    positional arguments. Inside `script`, read them as $args[0], $args[1], ...
    Use -LiteralPath for any path arguments.

        safe_ps("(Get-AuthenticodeSignature -FilePath $args[0]).Status", path)

    Args are passed OUTSIDE the -Command string so quotes / $() / backticks
    in a value can never escape into the script body (CWE-78 mitigation).
    """
    cmd = _POWERSHELL + ["-Command", script]
    cmd.extend(str(a) for a in args)
    return run_cmd(cmd, timeout=timeout)


def stream_cmd(cmd, on_line, input_text=None):
    """
    Run a long command and push each output line to `on_line(str)` as it
    arrives (used for the live console). Returns the process return code.
    SFC emits UTF-16-ish output with NUL bytes - they are stripped here.
    """
    try:
        p = subprocess.Popen(
            cmd, shell=False,
            stdin=subprocess.PIPE if input_text else subprocess.DEVNULL,
            stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
            text=True, encoding="utf-8", errors="ignore",
            creationflags=CREATE_NO_WINDOW, bufsize=1,
        )
        if input_text:
            try:
                p.stdin.write(input_text)
                p.stdin.flush()
                p.stdin.close()
            except Exception:
                pass
        for raw in iter(p.stdout.readline, ""):
            line = raw.replace("\x00", "").rstrip("\r\n")
            if line.strip():
                on_line(line)
        p.stdout.close()
        return p.wait()
    except FileNotFoundError:
        on_line(f"[error] command not found: {cmd}")
        return -2
    except Exception as e:
        on_line(f"[error] {e}")
        return -2


def human_bytes(n) -> str:
    """1536000 -> '1.5 MB'."""
    try:
        n = float(n)
    except (TypeError, ValueError):
        return "-"
    for unit in ("B", "KB", "MB", "GB", "TB"):
        if abs(n) < 1024.0:
            return f"{n:,.1f} {unit}"
        n /= 1024.0
    return f"{n:,.1f} PB"


def ps_json(script, timeout=120):
    """Run PowerShell, parse ConvertTo-Json output; always return a list."""
    rc, out = run_ps(script, timeout=timeout)
    if rc != 0 or not out:
        return []
    try:
        data = json.loads(out)
        return data if isinstance(data, list) else [data]
    except json.JSONDecodeError:
        return []
