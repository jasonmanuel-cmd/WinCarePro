"""Local, deterministic Guided Care domain primitives."""

from __future__ import annotations

from copy import deepcopy
from dataclasses import asdict, dataclass
from datetime import datetime, timedelta, timezone
import json
import os
from pathlib import Path
import tempfile
from types import MappingProxyType
from typing import Any, Callable, Collection, Mapping, Sequence

from core.platform import get_appdata_local


SNAPSHOT_LIMIT = 30
TIMELINE_LIMIT = 1_000


def _now() -> str:
    return datetime.now(timezone.utc).replace(microsecond=0).isoformat().replace("+00:00", "Z")


_SENSITIVE_DETAIL_PARTS = ("path", "secret", "token", "password", "api_key")


def _freeze(value: Any) -> Any:
    if isinstance(value, Mapping):
        return MappingProxyType({str(key): _freeze(item) for key, item in value.items()})
    if isinstance(value, (list, tuple)):
        return tuple(_freeze(item) for item in value)
    if isinstance(value, set):
        return frozenset(_freeze(item) for item in value)
    return deepcopy(value)


def _frozen_mapping(value: Mapping[str, Any] | None) -> Mapping[str, Any]:
    return _freeze(value or {})


def _plain(value: Any) -> Any:
    if isinstance(value, Mapping):
        return {str(key): _plain(item) for key, item in value.items()}
    if isinstance(value, (tuple, frozenset)):
        return [_plain(item) for item in value]
    return value


def _redact(value: Any) -> Any:
    if isinstance(value, Mapping):
        return {
            str(key): _redact(item)
            for key, item in value.items()
            if not any(part in str(key).casefold() for part in _SENSITIVE_DETAIL_PARTS)
        }
    if isinstance(value, (list, tuple)):
        return [_redact(item) for item in value]
    return deepcopy(value)


@dataclass(frozen=True)
class CareAction:
    action_id: str
    title: str
    reason: str
    severity: str
    impact: int = 0
    confidence: int = 0
    reversible: bool = True
    requires_confirmation: bool = False

    def to_dict(self) -> dict[str, Any]:
        return asdict(self)


@dataclass(frozen=True)
class CareSnapshot:
    captured_at: str
    metrics: Mapping[str, Any]
    findings: tuple[Mapping[str, Any], ...] = ()

    def __post_init__(self) -> None:
        object.__setattr__(self, "metrics", _frozen_mapping(self.metrics))
        object.__setattr__(self, "findings", tuple(_frozen_mapping(item) for item in self.findings))

    def to_dict(self) -> dict[str, Any]:
        return {"captured_at": self.captured_at, "metrics": _plain(self.metrics), "findings": _plain(self.findings)}

    @classmethod
    def from_dict(cls, value: Mapping[str, Any]) -> "CareSnapshot":
        return cls(
            captured_at=str(value["captured_at"]),
            metrics=value["metrics"],
            findings=tuple(value.get("findings", ())),
        )


@dataclass(frozen=True)
class CareOutcome:
    action_id: str
    title: str
    status: str
    message: str
    detail: Mapping[str, Any] | None = None

    def __post_init__(self) -> None:
        object.__setattr__(self, "detail", _frozen_mapping(_redact(self.detail or {})))

    def to_dict(self) -> dict[str, Any]:
        return {
            "action_id": self.action_id,
            "title": self.title,
            "status": self.status,
            "message": self.message,
            "detail": _plain(self.detail),
        }


class CareStore:
    """Bounded local snapshots and timeline, with corrupt state treated as empty."""

    def __init__(self, root: str | Path | None = None) -> None:
        self.root = Path(root) if root is not None else get_appdata_local() / "WinCarePro" / "care"
        self.snapshot_path = self.root / "snapshots.json"
        self.timeline_path = self.root / "timeline.jsonl"

    def snapshots(self) -> list[CareSnapshot]:
        try:
            raw = json.loads(self.snapshot_path.read_text(encoding="utf-8"))
            if not isinstance(raw, list):
                return []
            return [CareSnapshot.from_dict(item) for item in raw if isinstance(item, Mapping)]
        except (OSError, ValueError, KeyError, TypeError):
            return []

    def save_snapshot(self, snapshot: CareSnapshot) -> None:
        records = [item.to_dict() for item in self.snapshots()[-(SNAPSHOT_LIMIT - 1):]]
        records.append(snapshot.to_dict())
        self._atomic_write(self.snapshot_path, json.dumps(records, sort_keys=True, indent=2))

    def timeline(self) -> list[dict[str, Any]]:
        try:
            with self.timeline_path.open("r", encoding="utf-8") as handle:
                return [item for line in handle if (item := self._timeline_item(line)) is not None]
        except OSError:
            return []

    def append_event(self, event: str, detail: Mapping[str, Any] | None = None, *, at: str | None = None) -> None:
        record = {"at": at or _now(), "event": str(event), "detail": self._safe_detail(detail or {})}
        self.root.mkdir(parents=True, exist_ok=True)
        with self.timeline_path.open("a", encoding="utf-8", newline="\n") as handle:
            handle.write(json.dumps(record, sort_keys=True) + "\n")
        events = self.timeline()
        if len(events) > TIMELINE_LIMIT:
            self._atomic_write(self.timeline_path, "".join(json.dumps(item, sort_keys=True) + "\n" for item in events[-TIMELINE_LIMIT:]))

    @staticmethod
    def _timeline_item(line: str) -> dict[str, Any] | None:
        try:
            value = json.loads(line)
            if not isinstance(value, dict) or not isinstance(value.get("event"), str):
                return None
            if not isinstance(value.get("at"), str) or not isinstance(value.get("detail"), Mapping):
                return None
            return value
        except ValueError:
            return None

    @staticmethod
    def _safe_detail(detail: Mapping[str, Any]) -> dict[str, Any]:
        return _plain(_redact(detail))

    def _atomic_write(self, path: Path, content: str) -> None:
        self.root.mkdir(parents=True, exist_ok=True)
        handle = tempfile.NamedTemporaryFile("w", encoding="utf-8", dir=self.root, delete=False, newline="\n")
        try:
            with handle:
                handle.write(content)
            os.replace(handle.name, path)
        finally:
            if os.path.exists(handle.name):
                os.unlink(handle.name)


class CarePlanner:
    """Build and run only the fixed low-risk action catalog."""

    _SAFE_ACTIONS = {
        "disk": ("cleanup_temp_files", "Clean temporary files", "Low disk space was detected.", 90, 90),
        "memory": ("reclaim_memory", "Reclaim process working sets", "Memory pressure was detected.", 70, 90),
    }
    _DESTRUCTIVE_ACTION_IDS = frozenset({"cleanup_temp_files"})

    def __init__(self, allowlisted_actions: Collection[str] | None = None) -> None:
        self.allowlisted_actions = frozenset(
            ("cleanup_temp_files", "reclaim_memory") if allowlisted_actions is None else allowlisted_actions
        )

    def build_plan(self, findings: Sequence[Mapping[str, Any]] | None) -> list[CareAction]:
        actions: list[CareAction] = []
        seen: set[tuple[str, str]] = set()
        seen_safe_action_ids: set[str] = set()
        for finding in findings or ():
            if not isinstance(finding, Mapping) or str(finding.get("severity", "")).casefold() not in {"critical", "warning"}:
                continue
            category = str(finding.get("category", "Unknown")).strip() or "Unknown"
            title = str(finding.get("title", category)).strip() or category
            key = (category.casefold(), title.casefold())
            if key in seen:
                continue
            seen.add(key)
            safe = self._SAFE_ACTIONS.get(category.casefold())
            if safe:
                action_id, action_title, reason, impact, confidence = safe
                if action_id not in seen_safe_action_ids:
                    actions.append(CareAction(
                        action_id, action_title, reason, str(finding["severity"]), impact, confidence,
                        requires_confirmation=action_id in self._DESTRUCTIVE_ACTION_IDS,
                    ))
                    seen_safe_action_ids.add(action_id)
            else:
                actions.append(CareAction(
                    "review_required", f"{category}: {title}",
                    "This finding needs an explicit targeted review; it is not safe to auto-change.",
                    str(finding["severity"]), 50, 50, False, True,
                ))
        return sorted(actions, key=lambda item: (-item.impact, item.requires_confirmation, -item.confidence, item.action_id, item.title))

    def execute(
        self,
        actions: Sequence[CareAction],
        handlers: Mapping[str, Callable[[], Mapping[str, Any]]],
        *,
        approved: bool,
        store: CareStore | None = None,
        destructive_approved: Collection[str] | bool = (),
        is_cancelled: Callable[[], bool] | None = None,
    ) -> list[CareOutcome]:
        outcomes: list[CareOutcome] = []
        approved_risky = frozenset(action.action_id for action in actions) if destructive_approved is True else frozenset(destructive_approved)
        for action in actions:
            requires_separate_approval = action.action_id in self._DESTRUCTIVE_ACTION_IDS or action.requires_confirmation
            if is_cancelled and is_cancelled():
                outcome = CareOutcome(action.action_id, action.title, "cancelled", "Stopped before this action began.")
            elif not approved or (requires_separate_approval and action.action_id not in approved_risky):
                outcome = CareOutcome(action.action_id, action.title, "denied", "Approval is required before this action can run.")
            elif action.action_id not in self.allowlisted_actions:
                outcome = CareOutcome(action.action_id, action.title, "skipped", "This action is not allowlisted for Guided Care.")
            elif (handler := handlers.get(action.action_id)) is None:
                outcome = CareOutcome(action.action_id, action.title, "skipped", "No verified handler is available.")
            else:
                outcome = self._run_handler(action, handler)
            outcomes.append(outcome)
            if store:
                event = "approval_denied" if outcome.status == "denied" else outcome.status
                store.append_event(event, outcome.to_dict())
        return outcomes

    @staticmethod
    def _run_handler(action: CareAction, handler: Callable[[], Mapping[str, Any]]) -> CareOutcome:
        try:
            result = dict(handler() or {})
        except InterruptedError as exc:
            return CareOutcome(action.action_id, action.title, "interrupted", str(exc) or "Action was interrupted.")
        except Exception as exc:
            return CareOutcome(action.action_id, action.title, "failed", str(exc))
        message = str(result.get("message", "No verification result returned."))
        if not result.get("ok"):
            return CareOutcome(action.action_id, action.title, "failed", message, result)
        status = "verified" if result.get("verified") else "unverified"
        return CareOutcome(action.action_id, action.title, status, message, result)


class ProofEngine:
    """Compare a pre/post metric without inferring success from execution."""

    def compare(
        self,
        action_id: str,
        before: Mapping[str, Any],
        after: Mapping[str, Any],
        metric: str,
        *,
        target_delta: float = 10,
        higher_is_better: bool = True,
    ) -> CareOutcome:
        try:
            previous = float(before[metric])
            current = float(after[metric])
        except (KeyError, TypeError, ValueError):
            return CareOutcome(action_id, action_id, "failed", f"Metric '{metric}' is unavailable for proof.")
        delta = current - previous if higher_is_better else previous - current
        if delta >= target_delta:
            status = "verified"
        elif delta > 0:
            status = "improved"
        elif delta == 0:
            status = "unchanged"
        else:
            status = "failed"
        return CareOutcome(action_id, action_id, status, f"{metric} changed by {delta:g}.", {"metric": metric, "before": previous, "after": current, "delta": delta})


class ChangeDetector:
    """Report only measured metric changes; never guess at causation."""

    def compare(self, before: CareSnapshot, after: CareSnapshot) -> list[dict[str, Any]]:
        changes = []
        for metric in sorted(before.metrics.keys() & after.metrics.keys()):
            previous, current = before.metrics[metric], after.metrics[metric]
            if isinstance(previous, bool) or isinstance(current, bool):
                if previous != current:
                    changes.append({"metric": metric, "before": previous, "after": current, "delta": None})
            elif isinstance(previous, (int, float)) and isinstance(current, (int, float)) and previous != current:
                changes.append({"metric": metric, "before": previous, "after": current, "delta": current - previous})
        return changes


class ChangeReceipt:
    """Create a local, serializable receipt from measurements and rollback evidence."""

    def create(
        self,
        action: CareAction,
        proof: CareOutcome,
        *,
        protection: Sequence[str] = (),
    ) -> dict[str, Any]:
        return {
            "action_id": action.action_id,
            "action": action.title,
            "reason": action.reason,
            "result": proof.status,
            "message": proof.message,
            "measurement": _plain(proof.detail),
            "reversible": action.reversible,
            "protection": [str(item) for item in protection if str(item).strip()],
            "generated_at": _now(),
        }


@dataclass(frozen=True)
class CareProfile:
    profile_id: str
    title: str
    version: int
    recommendations: tuple[str, ...]

    def to_dict(self) -> dict[str, Any]:
        return asdict(self)


class CareProfiles:
    _CATALOG = (
        CareProfile("gaming", "Gaming", 1, ("Prioritize game performance while reviewing background apps.",)),
        CareProfile("work", "Work", 1, ("Keep startup and update risks visible.",)),
        CareProfile("privacy", "Privacy", 1, ("Review privacy changes before applying them.",)),
        CareProfile("battery", "Battery", 1, ("Review power usage and startup pressure.",)),
        CareProfile("restore_defaults", "Restore Defaults", 1, ("Return Guided Care recommendations to their defaults.",)),
    )

    def all(self) -> tuple[CareProfile, ...]:
        return self._CATALOG

    def get(self, profile_id: str) -> CareProfile:
        for profile in self._CATALOG:
            if profile.profile_id == str(profile_id).casefold():
                return profile
        raise ValueError("Unknown care profile.")


class WeeklyReport:
    def __init__(self, store: CareStore) -> None:
        self.store = store

    def generate(self) -> dict[str, Any]:
        now = datetime.now(timezone.utc)
        cutoff = now - timedelta(days=7)
        snapshots = [
            (at, snapshot)
            for snapshot in self.store.snapshots()
            if (at := self._parsed_time(snapshot.captured_at)) is not None and cutoff <= at <= now
        ]
        snapshots.sort(key=lambda item: item[0])
        recent_snapshots = [snapshot for _, snapshot in snapshots]
        scores = [item.metrics.get("health_score") for item in recent_snapshots if isinstance(item.metrics.get("health_score"), (int, float))]
        risks = self._risks(recent_snapshots[-1] if recent_snapshots else None)
        completed = sum(
            1 for event in self.store.timeline()
            if (at := self._parsed_time(event.get("at"))) is not None
            and cutoff <= at <= now
            and event["event"] in {"executed", "verified"}
            and isinstance(event.get("detail"), Mapping)
            and event["detail"].get("status") == "verified"
        )
        return {
            "score_start": scores[0] if scores else None,
            "score_end": scores[-1] if scores else None,
            "score_change": scores[-1] - scores[0] if scores else 0,
            "completed_count": completed,
            "unresolved_risks": risks,
            "next_steps": [f"Review: {risk}" for risk in risks] or ["Continue regular scans."],
        }

    @staticmethod
    def _parsed_time(value: object) -> datetime | None:
        if not isinstance(value, str):
            return None
        try:
            parsed = datetime.fromisoformat(value.replace("Z", "+00:00"))
        except ValueError:
            return None
        return parsed.astimezone(timezone.utc) if parsed.tzinfo is not None else None

    @staticmethod
    def _risks(snapshot: CareSnapshot | None) -> list[str]:
        if snapshot is None:
            return []
        return [
            str(finding.get("title") or finding.get("category") or "Unknown risk")
            for finding in snapshot.findings
            if str(finding.get("severity", "")).casefold() in {"critical", "warning"}
        ]
