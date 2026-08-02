"""Subprocess contract tests for the Guided Care JSON bridge."""

from __future__ import annotations

import json
import os
from pathlib import Path
import subprocess
import sys

import pytest


ROOT = Path(__file__).resolve().parents[1]
CLI = ROOT / "guided_care_cli.py"
SUCCESSFUL_SCAN_HARNESS = """
import guided_care_cli as cli

class FixedScanner:
    def __init__(self, logger):
        pass

    def run_full_scan(self, cancel_event=None):
        return ([{"severity": "OK", "category": "Guided Care", "title": "Harness scan completed", "recommendation": "No action needed."}], {"cpu_pct": 10, "ram_pct": 20, "disk_free_pct": 50}, 100, [])

cli.Scanner = FixedScanner
raise SystemExit(cli.main(["scan"]))
"""
ENV_GATE_HARNESS = """
import guided_care_cli as cli

class FailingScanner:
    def __init__(self, logger):
        pass

    def run_full_scan(self, cancel_event=None):
        raise RuntimeError("harness scanner called")

cli.Scanner = FailingScanner
raise SystemExit(cli.main(["scan"]))
"""


def run_cli(tmp_path: Path, *args: str) -> subprocess.CompletedProcess[str]:
    env = os.environ | {"LOCALAPPDATA": str(tmp_path)}
    return subprocess.run(
        [sys.executable, str(CLI), *args],
        cwd=ROOT,
        capture_output=True,
        text=True,
        env=env,
        check=False,
    )


def run_harness(tmp_path: Path, source: str, **env_vars: str) -> subprocess.CompletedProcess[str]:
    return subprocess.run(
        [sys.executable, "-c", source],
        cwd=ROOT,
        capture_output=True,
        text=True,
        env=os.environ | {"LOCALAPPDATA": str(tmp_path)} | env_vars,
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
    [("dashboard",), ("scan", "--cancel"), ("profiles",), ("timeline",), ("weekly-report",), ("support-preview",)],
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
    [
        ("unknown",),
        ("dashboard", "--command", "Get-Process"),
        ("dashboard", "--cancel"),
        ("scan", "--c"),
        ("scan", "--can"),
        ("--state-dir", "SECRET_STATE_PATH", "dashboard"),
    ],
)
def test_malformed_or_unknown_commands_fail_closed_with_one_json_error(tmp_path, args):
    result = run_cli(tmp_path, *args)

    assert result.returncode != 0
    payload = json_stdout(result)
    assert payload["schema_version"] == 1
    assert payload["error"]["code"] == "invalid_input"
    assert "Get-Process" not in result.stdout + result.stderr
    assert "SECRET_STATE_PATH" not in result.stdout + result.stderr


def test_scan_cancellation_records_a_local_timeline_event(tmp_path):
    result = run_cli(tmp_path, "scan", "--cancel")

    assert result.returncode == 0, result.stderr
    assert json_stdout(result)["data"]["status"] == "cancelled"
    timeline = json_stdout(run_cli(tmp_path, "timeline"))["data"]["events"]
    assert timeline == [timeline[-1]]
    assert timeline[-1]["event"] == "scan_cancelled"
    assert not (tmp_path / "WinCarePro" / "care" / "snapshots.json").exists()


def test_plain_scan_persists_the_dashboard_snapshot_and_completion_event(tmp_path):
    result = run_harness(tmp_path, SUCCESSFUL_SCAN_HARNESS)

    assert result.returncode == 0, result.stderr
    payload = json_stdout(result)
    assert payload["schema_version"] == 1
    assert payload["command"] == "scan"
    assert payload["data"]["status"] == "completed"
    dashboard = json_stdout(run_cli(tmp_path, "dashboard"))["data"]
    assert dashboard["snapshot_captured_at"]
    assert dashboard["health_score"] == payload["data"]["health_score"]
    timeline = json_stdout(run_cli(tmp_path, "timeline"))["data"]["events"]
    assert timeline[-1]["event"] == "scan_completed"


def test_environment_cannot_fabricate_a_successful_scan(tmp_path):
    result = run_harness(tmp_path, ENV_GATE_HARNESS, WINCAREPRO_GUIDED_CARE_TEST_SCAN="1")

    assert result.returncode == 1
    assert json_stdout(result)["error"]["code"] == "runtime_error"


def test_dashboard_works_with_an_empty_store(tmp_path):
    result = run_cli(tmp_path, "dashboard")

    assert result.returncode == 0, result.stderr
    dashboard = json_stdout(result)["data"]
    assert dashboard["health_score"] == 100
    assert dashboard["findings"] == []
    assert dashboard["timeline_count"] == 0
    assert dashboard["latest_changes"] == []
