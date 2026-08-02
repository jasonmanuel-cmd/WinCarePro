"""Subprocess contract tests for the Guided Care JSON bridge."""

from __future__ import annotations

import json
from pathlib import Path
import subprocess
import sys

import pytest


ROOT = Path(__file__).resolve().parents[1]
CLI = ROOT / "guided_care_cli.py"


def run_cli(state_dir: Path, *args: str) -> subprocess.CompletedProcess[str]:
    return subprocess.run(
        [sys.executable, str(CLI), "--state-dir", str(state_dir), *args],
        cwd=ROOT,
        capture_output=True,
        text=True,
        check=False,
    )


def json_stdout(result: subprocess.CompletedProcess[str]) -> dict:
    decoder = json.JSONDecoder()
    value, end = decoder.raw_decode(result.stdout)
    assert result.stdout[end:].strip() == ""
    assert isinstance(value, dict)
    return value


@pytest.mark.parametrize(
    "args",
    [
        ("dashboard",),
        ("scan", "--cancel"),
        ("profiles",),
        ("timeline",),
        ("weekly-report",),
    ],
)
def test_every_valid_command_returns_the_v1_json_envelope(tmp_path, args):
    result = run_cli(tmp_path, *args)

    assert result.returncode == 0, result.stderr
    payload = json_stdout(result)
    assert payload["schema_version"] == 1
    assert payload["command"] == args[0]
    assert isinstance(payload["data"], dict)


@pytest.mark.parametrize(
    "args",
    [("unknown",), ("dashboard", "--command", "Get-Process"), ("dashboard", "--cancel")],
)
def test_malformed_or_unknown_commands_fail_closed_with_one_json_error(tmp_path, args):
    result = run_cli(tmp_path, *args)

    assert result.returncode != 0
    payload = json_stdout(result)
    assert payload["schema_version"] == 1
    assert payload["error"]["code"] == "invalid_input"
    assert "Get-Process" not in result.stdout


def test_scan_cancellation_records_a_local_timeline_event(tmp_path):
    result = run_cli(tmp_path, "scan", "--cancel")

    assert result.returncode == 0, result.stderr
    assert json_stdout(result)["data"]["status"] == "cancelled"
    timeline = json_stdout(run_cli(tmp_path, "timeline"))["data"]["events"]
    assert timeline[-1]["event"] == "scan_cancelled"


def test_dashboard_works_with_an_empty_store(tmp_path):
    result = run_cli(tmp_path, "dashboard")

    assert result.returncode == 0, result.stderr
    dashboard = json_stdout(result)["data"]
    assert dashboard["health_score"] == 100
    assert dashboard["findings"] == []
    assert dashboard["timeline_count"] == 0
