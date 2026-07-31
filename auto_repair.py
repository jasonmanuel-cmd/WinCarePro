"""Finding-driven, verification-first repair planning for WinCare Pro.

The planner intentionally auto-selects only low-risk, reversible maintenance
steps. Network, privacy, driver, service, registry, firmware, and application
changes are returned as review-required items; they must never be silently
applied just because an AI report mentioned them.
"""

from __future__ import annotations

from dataclasses import dataclass, field
from typing import Any, Callable, Mapping, Sequence


@dataclass(frozen=True)
class RepairAction:
    action_id: str
    title: str
    reason: str


@dataclass
class RepairPlan:
    safe_actions: list[RepairAction] = field(default_factory=list)
    review_required: list[RepairAction] = field(default_factory=list)


@dataclass(frozen=True)
class RepairOutcome:
    action_id: str
    title: str
    status: str
    message: str


@dataclass
class RepairRun:
    outcomes: list[RepairOutcome] = field(default_factory=list)

    @property
    def verified_count(self) -> int:
        return sum(item.status == "verified" for item in self.outcomes)

    @property
    def completed_count(self) -> int:
        return self.verified_count

    @property
    def failed_count(self) -> int:
        return sum(item.status == "failed" for item in self.outcomes)


class AutoRepairEngine:
    """Build and execute a conservative repair plan from actual scan findings."""

    _SAFE_BY_CATEGORY = {
        "disk": ("cleanup_temp_files", "Clean temporary files", "Low free disk space was detected."),
        "memory": ("reclaim_memory", "Reclaim process working sets", "Memory pressure was detected."),
    }

    def build_plan(self, findings: Sequence[Mapping[str, Any]] | None) -> RepairPlan:
        plan = RepairPlan()
        seen_safe: set[str] = set()
        seen_review: set[tuple[str, str]] = set()

        for finding in findings or ():
            severity = str(finding.get("severity", "")).casefold()
            if severity not in {"critical", "warning"}:
                continue
            category = str(finding.get("category", "Unknown")).strip()
            title = str(finding.get("title", category)).strip() or category
            safe = self._SAFE_BY_CATEGORY.get(category.casefold())
            if safe:
                action_id, action_title, reason = safe
                if action_id not in seen_safe:
                    plan.safe_actions.append(RepairAction(action_id, action_title, reason))
                    seen_safe.add(action_id)
                continue

            key = (category, title)
            if key not in seen_review:
                plan.review_required.append(RepairAction(
                    "review_required",
                    f"{category}: {title}",
                    "This finding requires an explicit, targeted review; it is not safe to auto-change.",
                ))
                seen_review.add(key)
        return plan

    def execute(
        self,
        plan: RepairPlan,
        handlers: Mapping[str, Callable[[], Mapping[str, Any]]],
    ) -> RepairRun:
        run = RepairRun()
        for action in plan.safe_actions:
            handler = handlers.get(action.action_id)
            if handler is None:
                run.outcomes.append(RepairOutcome(
                    action.action_id, action.title, "skipped", "No verified handler is available."
                ))
                continue
            try:
                result = dict(handler() or {})
            except Exception as exc:
                run.outcomes.append(RepairOutcome(action.action_id, action.title, "failed", str(exc)))
                continue
            message = str(result.get("message", "No verification result returned."))
            if not result.get("ok"):
                run.outcomes.append(RepairOutcome(action.action_id, action.title, "failed", message))
            elif result.get("verified"):
                run.outcomes.append(RepairOutcome(action.action_id, action.title, "verified", message))
            else:
                run.outcomes.append(RepairOutcome(action.action_id, action.title, "unverified", message))
        return run
