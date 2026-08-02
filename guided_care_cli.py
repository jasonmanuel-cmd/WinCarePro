"""Fixed-command JSON bridge for local Guided Care data."""

from __future__ import annotations

import argparse
from datetime import datetime, timezone
import json
import sys
import threading
from typing import Any, Sequence

from core.health import HealthScore
from core.logger import AppLogger
from core.scanner import Scanner
from guided_care import CareProfiles, CareSnapshot, CareStore, ChangeDetector, WeeklyReport


SCHEMA_VERSION = 1
COMMANDS = ("dashboard", "scan", "profiles", "timeline", "weekly-report")


class InputError(ValueError):
    pass


class JsonArgumentParser(argparse.ArgumentParser):
    def error(self, message: str) -> None:
        raise InputError("Invalid command input.")


def build_parser() -> argparse.ArgumentParser:
    parser = JsonArgumentParser(add_help=False, allow_abbrev=False)
    parser.add_argument("command", choices=COMMANDS)
    parser.add_argument("--cancel", action="store_true")
    return parser


def _now() -> str:
    return datetime.now(timezone.utc).replace(microsecond=0).isoformat().replace("+00:00", "Z")


def dashboard(store: CareStore) -> dict[str, Any]:
    snapshots = store.snapshots()
    snapshot = snapshots[-1].to_dict() if snapshots else None
    metrics = snapshot["metrics"] if snapshot else {}
    score = metrics.get("health_score") if isinstance(metrics.get("health_score"), (int, float)) else HealthScore.compute(metrics)[0]
    return {
        "health_score": score,
        "grade": HealthScore.grade(int(score)),
        "metrics": metrics,
        "findings": snapshot["findings"] if snapshot else [],
        "snapshot_captured_at": snapshot["captured_at"] if snapshot else None,
        "timeline_count": len(store.timeline()),
        "latest_changes": ChangeDetector().compare(snapshots[-2], snapshots[-1]) if len(snapshots) > 1 else [],
    }


def scan(store: CareStore, cancelled: bool) -> dict[str, Any]:
    if cancelled:
        store.append_event("scan_cancelled", {"status": "cancelled", "findings_count": 0})
        return {"status": "cancelled", "findings": [], "metrics": {}, "health_score": 100, "breakdown": []}
    findings, metrics, score, breakdown = Scanner(AppLogger()).run_full_scan(cancel_event=threading.Event())
    snapshot = CareSnapshot(_now(), {**metrics, "health_score": score}, tuple(findings))
    store.save_snapshot(snapshot)
    store.append_event("scan_completed", {"status": "completed", "findings_count": len(findings), "health_score": score})
    return {"status": "completed", "findings": findings, "metrics": snapshot.to_dict()["metrics"], "health_score": score, "breakdown": breakdown}


def dispatch(args: argparse.Namespace) -> dict[str, Any]:
    if args.cancel and args.command != "scan":
        raise InputError("Invalid command input.")
    store = CareStore()
    if args.command == "dashboard":
        return dashboard(store)
    if args.command == "scan":
        return scan(store, args.cancel)
    if args.command == "profiles":
        return {"profiles": [profile.to_dict() for profile in CareProfiles().all()]}
    if args.command == "timeline":
        return {"events": store.timeline()}
    if args.command == "weekly-report":
        return WeeklyReport(store).generate()
    raise InputError("Invalid command input.")


def write_json(value: dict[str, Any]) -> None:
    sys.stdout.write(json.dumps(value, sort_keys=True) + "\n")


def main(argv: Sequence[str] | None = None) -> int:
    try:
        args = build_parser().parse_args(argv)
        write_json({"schema_version": SCHEMA_VERSION, "command": args.command, "data": dispatch(args)})
        return 0
    except InputError:
        print("guided-care-cli: invalid input", file=sys.stderr)
        write_json({"schema_version": SCHEMA_VERSION, "error": {"code": "invalid_input", "message": "Invalid command input."}})
        return 2
    except Exception as exc:
        print(f"guided-care-cli: {type(exc).__name__}", file=sys.stderr)
        write_json({"schema_version": SCHEMA_VERSION, "error": {"code": "runtime_error", "message": "The command could not complete."}})
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
