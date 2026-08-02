"""Local, deterministic Guided Care domain primitives."""

from __future__ import annotations

from copy import deepcopy
from dataclasses import asdict, dataclass
from datetime import datetime, timezone
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


def _frozen_mapping(value: Mapping[str, Any] | None) -> Mapping[str, Any]:
    return MappingProxyType(deepcopy(dict(value or {})))


def _plain(value: Any) -> Any:
    if isinstance(value, Mapping):
        return {str(key): _plain(item) for key, item in value.items()}
    if isinstance(value, tuple):
        return [_plain(item) for item in value]
    return value


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
        object.__setattr__(self, "detail", _frozen_mapping(self.detail))

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
            return value if isinstance(value, dict) and isinstance(value.get("event"), str) else None
        except ValueError:
            return None

    @staticmethod
    def _safe_detail(detail: Mapping[str, Any]) -> dict[str, Any]:
        excluded = {"path", "paths", "secret", "token", "password", "api_key"}
        return {str(key): _plain(value) for key, value in detail.items() if str(key).casefold() not in excluded}

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

    def __init__(self, allowlisted_actions: Collection[str] | None = None) -> None:
        self.allowlisted_actions = frozenset(allowlisted_actions or ("cleanup_temp_files", "reclaim_memory"))

    def build_plan(self, findings: Sequence[Mapping[str, Any]] | None) -> list[CareAction]:
        actions: list[CareAction] = []
        seen: set[tuple[str, str]] = set()
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
                actions.append(CareAction(action_id, action_title, reason, str(finding["severity"]), impact, confidence))
            else:
                actions.append(CareAction(
                    "review_required", f"{category}: {title}",
                    "This finding needs an explicit targeted review; it is not safe to auto-change.",
                    str(finding["severity"]), 50, 50, False, True,
                ))
        return sorted(actions, key=lambda item: (item.requires_confirmation, -item.impact, -item.confidence, item.action_id, item.title))

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
            if is_cancelled and is_cancelled():
                outcome = CareOutcome(action.action_id, action.title, "cancelled", "Stopped before this action began.")
            elif not approved or (action.requires_confirmation and action.action_id not in approved_risky):
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
        snapshots = self.store.snapshots()
        scores = [item.metrics.get("health_score") for item in snapshots if isinstance(item.metrics.get("health_score"), (int, float))]
        risks = self._risks(snapshots[-1] if snapshots else None)
        completed = sum(
            1 for event in self.store.timeline()
            if event["event"] in {"executed", "verified"} and event.get("detail", {}).get("status") == "verified"
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
    def _risks(snapshot: CareSnapshot | None) -> list[str]:
        if snapshot is None:
            return []
        return [
            str(finding.get("title") or finding.get("category") or "Unknown risk")
            for finding in snapshot.findings
            if str(finding.get("severity", "")).casefold() in {"critical", "warning"}
        ]
