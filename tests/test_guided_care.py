import json
from datetime import datetime, timedelta, timezone

import pytest

from guided_care import (
    CareAction,
    CareExperiment,
    CareOutcome,
    CarePlanner,
    CareProfiles,
    CareSnapshot,
    CareStore,
    ChangeDetector,
    ChangeReceipt,
    ProofEngine,
    SupportSummary,
    WeeklyReport,
)


def snapshot(number, score=80, findings=()):
    return CareSnapshot(
        captured_at=f"2026-08-02T00:{number:02d}:00Z",
        metrics={"health_score": score, "disk_free_pct": 10 + number},
        findings=tuple(findings),
    )


def utc_timestamp(days_ago=0):
    return (datetime.now(timezone.utc) - timedelta(days=days_ago)).replace(microsecond=0).isoformat().replace("+00:00", "Z")


def test_store_starts_empty_and_recovers_from_corrupt_json(tmp_path):
    store = CareStore(tmp_path)

    assert store.snapshots() == []
    assert store.timeline() == []

    (tmp_path / "snapshots.json").write_text("{not json", encoding="utf-8")
    (tmp_path / "timeline.jsonl").write_text("{not json}\n", encoding="utf-8")

    assert store.snapshots() == []
    assert store.timeline() == []


def test_store_keeps_the_latest_thirty_snapshots(tmp_path):
    store = CareStore(tmp_path)

    for number in range(31):
        store.save_snapshot(snapshot(number))

    saved = store.snapshots()
    assert len(saved) == 30
    assert saved[0].metrics["disk_free_pct"] == 11
    assert saved[-1].metrics["disk_free_pct"] == 40
    assert json.loads((tmp_path / "snapshots.json").read_text(encoding="utf-8"))[0]["metrics"]["disk_free_pct"] == 11


def test_store_keeps_the_latest_thousand_timeline_events(tmp_path):
    store = CareStore(tmp_path)

    for number in range(1001):
        store.append_event("scan", {"number": number}, at=f"2026-08-02T01:{number:04d}Z")

    events = store.timeline()
    assert len(events) == 1000
    assert events[0]["detail"]["number"] == 1
    assert events[-1]["detail"]["number"] == 1000


def test_planner_ranks_safe_actions_deterministically_before_review_items():
    findings = [
        {"severity": "Warning", "category": "Memory", "title": "RAM usage 85%"},
        {"severity": "Critical", "category": "Disk", "title": "Drive almost full"},
        {"severity": "Warning", "category": "Drivers", "title": "Device needs attention"},
    ]

    plan = CarePlanner().build_plan(findings)

    assert [action.action_id for action in plan] == [
        "cleanup_temp_files",
        "reclaim_memory",
        "review_required",
    ]
    assert [action.requires_confirmation for action in plan] == [True, False, True]


def test_planner_deduplicates_safe_actions_by_action_id():
    plan = CarePlanner().build_plan([
        {"severity": "Critical", "category": "Disk", "title": "System drive full"},
        {"severity": "Warning", "category": "Disk", "title": "Cleanup recommended"},
    ])

    assert [action.action_id for action in plan] == ["cleanup_temp_files"]


def test_profiles_are_a_fixed_five_item_catalog_and_unknown_profile_fails_closed():
    profiles = CareProfiles()

    assert [profile.profile_id for profile in profiles.all()] == [
        "gaming", "work", "privacy", "battery", "restore_defaults"
    ]
    assert profiles.get("gaming").version == 1
    with pytest.raises(ValueError):
        profiles.get("unknown")


@pytest.mark.parametrize(
    ("before", "after", "expected"),
    [
        ({"disk_free_pct": 10}, {"disk_free_pct": 25}, "verified"),
        ({"disk_free_pct": 10}, {"disk_free_pct": 15}, "improved"),
        ({"disk_free_pct": 10}, {"disk_free_pct": 10}, "unchanged"),
        ({"disk_free_pct": 10}, {"disk_free_pct": 5}, "failed"),
    ],
)
def test_proof_engine_classifies_metric_comparison(before, after, expected):
    proof = ProofEngine().compare("cleanup_temp_files", before, after, "disk_free_pct")

    assert proof.status == expected


def test_change_detector_reports_only_shared_measured_changes():
    before = CareSnapshot(utc_timestamp(1), {"startup_count": 8, "ram_pct": 70, "fast_startup": True, "old": 1})
    after = CareSnapshot(utc_timestamp(), {"startup_count": 6, "ram_pct": 70, "fast_startup": False, "new": 1})

    assert ChangeDetector().compare(before, after) == [
        {"metric": "fast_startup", "before": True, "after": False, "delta": None},
        {"metric": "startup_count", "before": 8, "after": 6, "delta": -2},
    ]


def test_change_receipt_contains_proof_and_rollback_protection():
    action = CareAction("startup_pause", "Pause startup item", "Startup became slower.", "Warning")
    proof = ProofEngine().compare("startup_pause", {"startup_seconds": 61}, {"startup_seconds": 44}, "startup_seconds", higher_is_better=False)

    receipt = ChangeReceipt().create(action, proof, protection=("Startup entry saved", "One-click undo"))

    assert receipt["result"] == "verified"
    assert receipt["measurement"]["delta"] == 17
    assert receipt["protection"] == ["Startup entry saved", "One-click undo"]
    assert receipt["reversible"] is True
    json.dumps(receipt)


def test_experiment_keeps_only_a_verified_improvement(tmp_path):
    store = CareStore(tmp_path)
    action = CareAction("startup_pause", "Pause startup item", "Startup became slower.", "Warning")

    receipt = CareExperiment(store).run(
        action, approved=True, protection=("Startup entry saved",),
        before={"startup_seconds": 61}, metric="startup_seconds",
        execute=lambda: {"ok": True}, measure=lambda: {"startup_seconds": 44},
        revert=lambda: {"ok": True}, higher_is_better=False,
    )

    assert receipt["decision"] == "kept"
    assert [event["event"] for event in store.timeline()] == [
        "experiment_proposed", "experiment_approved", "experiment_protected",
        "experiment_started", "experiment_kept",
    ]


def test_experiment_fails_closed_without_approval_or_rollback(tmp_path):
    action = CareAction("startup_pause", "Pause startup item", "Startup became slower.", "Warning")
    called = []

    denied = CareExperiment(CareStore(tmp_path / "denied")).run(
        action, approved=False, protection=(), before={"value": 1}, metric="value",
        execute=lambda: called.append("execute") or {"ok": True}, measure=lambda: {"value": 2}, revert=None,
    )
    blocked = CareExperiment(CareStore(tmp_path / "blocked")).run(
        action, approved=True, protection=(), before={"value": 1}, metric="value",
        execute=lambda: called.append("execute") or {"ok": True}, measure=lambda: {"value": 2}, revert=None,
    )

    assert denied["decision"] == "denied"
    assert blocked["decision"] == "blocked"
    assert called == []


def test_experiment_reverts_when_proof_threshold_is_not_met(tmp_path):
    reverted = []
    receipt = CareExperiment(CareStore(tmp_path)).run(
        CareAction("startup_pause", "Pause startup item", "Slow startup", "Warning"),
        approved=True, protection=("Saved",), before={"seconds": 61}, metric="seconds",
        execute=lambda: {"ok": True}, measure=lambda: {"seconds": 58},
        revert=lambda: reverted.append(True) or {"ok": True, "message": "Restored"},
        higher_is_better=False,
    )

    assert receipt["decision"] == "reverted"
    assert reverted == [True]


def test_experiment_records_failed_rollback_after_measurement_error(tmp_path):
    def measurement_failed():
        raise RuntimeError("sensor unavailable")

    receipt = CareExperiment(CareStore(tmp_path)).run(
        CareAction("startup_pause", "Pause startup item", "Slow startup", "Warning"),
        approved=True, protection=("Saved",), before={"seconds": 61}, metric="seconds",
        execute=lambda: {"ok": True}, measure=measurement_failed,
        revert=lambda: {"ok": False, "message": "Restore verification failed"},
        higher_is_better=False,
    )

    assert receipt["decision"] == "rollback_failed"
    assert receipt["rollback_message"] == "Restore verification failed"


def test_executor_denies_unapproved_actions_and_records_the_event(tmp_path):
    store = CareStore(tmp_path)
    action = CareAction("cleanup_temp_files", "Clean temporary files", "Low disk", "Critical")

    outcomes = CarePlanner().execute([action], {}, approved=False, store=store)

    assert outcomes[0].status == "denied"
    assert store.timeline()[-1]["event"] == "approval_denied"


def test_executor_requires_trusted_separate_approval_for_cleanup_even_if_action_metadata_is_forged(tmp_path):
    store = CareStore(tmp_path)
    forged_cleanup = CareAction("cleanup_temp_files", "Clean", "Low disk", "Warning", requires_confirmation=False)
    handler = {"cleanup_temp_files": lambda: {"ok": True, "verified": True, "message": "cleaned"}}

    denied = CarePlanner().execute([forged_cleanup], handler, approved=True, store=store)
    approved = CarePlanner().execute(
        [forged_cleanup], handler, approved=True, destructive_approved={"cleanup_temp_files"}, store=store
    )

    assert denied[0].status == "denied"
    assert approved[0].status == "verified"


def test_explicit_empty_allowlist_denies_all_actions():
    action = CareAction("reclaim_memory", "Reclaim memory", "High RAM", "Warning")

    outcome = CarePlanner(()).execute(
        [action], {"reclaim_memory": lambda: {"ok": True, "verified": True}}, approved=True
    )

    assert outcome[0].status == "skipped"


def test_executor_records_cancellation_interruption_and_missing_handler(tmp_path):
    store = CareStore(tmp_path)
    planner = CarePlanner()
    actions = [
        CareAction("cleanup_temp_files", "Clean temporary files", "Low disk", "Critical"),
        CareAction("reclaim_memory", "Reclaim memory", "High RAM", "Warning"),
    ]

    cancelled = planner.execute(actions, {}, approved=True, store=store, is_cancelled=lambda: True)
    assert [outcome.status for outcome in cancelled] == ["cancelled", "cancelled"]
    assert store.timeline()[-1]["event"] == "cancelled"

    interrupted = planner.execute(
        actions[:1],
        {"cleanup_temp_files": lambda: (_ for _ in ()).throw(InterruptedError("stop"))},
        approved=True,
        destructive_approved=True,
        store=store,
    )
    assert interrupted[0].status == "interrupted"
    assert store.timeline()[-1]["event"] == "interrupted"

    missing = planner.execute(actions[:1], {}, approved=True, destructive_approved=True, store=store)
    assert missing[0].status == "skipped"
    assert store.timeline()[-1]["event"] == "skipped"


def test_weekly_report_summarizes_score_change_completed_work_and_unresolved_risks(tmp_path):
    store = CareStore(tmp_path)
    store.save_snapshot(CareSnapshot(
        utc_timestamp(1), {"health_score": 60}, ({"severity": "Critical", "title": "Disk"},)
    ))
    store.save_snapshot(CareSnapshot(
        utc_timestamp(), {"health_score": 78}, ({"severity": "Warning", "title": "Memory"},)
    ))
    store.append_event("executed", {"action_id": "cleanup_temp_files", "status": "verified"}, at=utc_timestamp())

    report = WeeklyReport(store).generate()

    assert report["score_change"] == 18
    assert report["completed_count"] == 1
    assert report["unresolved_risks"] == ["Memory"]
    assert report["next_steps"] == ["Review: Memory"]
    json.dumps(report)


def test_records_copy_and_freeze_nested_values():
    metrics = {"nested": {"value": 1}, "items": [{"value": 2}]}
    detail = {"nested": {"value": 3}, "items": [{"value": 4}]}
    saved = CareSnapshot(utc_timestamp(), metrics)
    outcome = CareOutcome("reclaim_memory", "Reclaim memory", "verified", "done", detail)

    metrics["nested"]["value"] = 99
    metrics["items"][0]["value"] = 99

    assert saved.metrics["nested"]["value"] == 1
    assert saved.metrics["items"][0]["value"] == 2
    with pytest.raises(TypeError):
        saved.metrics["nested"]["value"] = 0
    with pytest.raises(TypeError):
        saved.metrics["items"][0]["value"] = 0
    assert outcome.detail["nested"]["value"] == 3
    assert outcome.detail["items"][0]["value"] == 4
    with pytest.raises(TypeError):
        outcome.detail["nested"]["value"] = 0
    with pytest.raises(TypeError):
        outcome.detail["items"][0]["value"] = 0


def test_executor_recursively_redacts_sensitive_handler_detail_fields(tmp_path):
    action = CareAction("cleanup_temp_files", "Clean", "Low disk", "Warning")
    result = {
        "ok": False,
        "message": "failed",
        "nested": {"safe": "keep", "file_path": "C:/private", "access_token": "secret"},
        "items": [{"password_hint": "private", "safe": "keep"}],
    }
    store = CareStore(tmp_path)

    outcome = CarePlanner().execute(
        [action], {"cleanup_temp_files": lambda: result}, approved=True,
        destructive_approved={"cleanup_temp_files"}, store=store,
    )[0]

    assert outcome.detail["nested"] == {"safe": "keep"}
    assert outcome.detail["items"] == ({"safe": "keep"},)
    assert store.timeline()[-1]["detail"]["detail"] == {"nested": {"safe": "keep"}, "items": [{"safe": "keep"}], "ok": False, "message": "failed"}


def test_weekly_report_ignores_old_state_and_malformed_timeline_detail(tmp_path):
    store = CareStore(tmp_path)
    store.save_snapshot(CareSnapshot(utc_timestamp(8), {"health_score": 10}, ({"severity": "Critical", "title": "Old risk"},)))
    store.save_snapshot(CareSnapshot(utc_timestamp(2), {"health_score": 60}, ({"severity": "Warning", "title": "Recent risk"},)))
    store.save_snapshot(CareSnapshot(utc_timestamp(), {"health_score": 72}, ({"severity": "Warning", "title": "Current risk"},)))
    store.append_event("executed", {"status": "verified"}, at=utc_timestamp(8))
    store.append_event("executed", {"status": "verified"}, at=utc_timestamp())
    with store.timeline_path.open("a", encoding="utf-8") as handle:
        handle.write(json.dumps({"at": utc_timestamp(), "event": "executed", "detail": None}) + "\n")

    report = WeeklyReport(store).generate()

    assert report["score_start"] == 60
    assert report["score_end"] == 72
    assert report["score_change"] == 12
    assert report["completed_count"] == 1
    assert report["unresolved_risks"] == ["Current risk"]


def test_support_summary_omits_raw_findings_paths_and_event_details(tmp_path):
    store = CareStore(tmp_path)
    store.save_snapshot(CareSnapshot(
        utc_timestamp(), {"health_score": 81, "disk_free_pct": 33, "private_metric": "secret"},
        ({"severity": "Warning", "title": "SECRET_FINDING", "file_path": "C:/private"},),
    ))
    store.append_event("experiment_failed", {
        "status": "failed", "decision": "reverted", "message": "SECRET_MESSAGE", "file_path": "C:/private",
    })

    summary = SupportSummary(store).generate()
    encoded = json.dumps(summary)

    assert summary["latest_metrics"] == {"health_score": 81, "disk_free_pct": 33}
    assert summary["recent_events"][0]["decision"] == "reverted"
    assert "SECRET" not in encoded
    assert "C:/private" not in encoded
