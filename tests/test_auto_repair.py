import unittest

from auto_repair import AutoRepairEngine


class AutoRepairEngineTests(unittest.TestCase):
    def test_plan_only_selects_safe_fixes_for_matching_findings(self):
        findings = [
            {
                "severity": "Critical",
                "category": "Disk",
                "title": "System drive: 2 GB free (3%)",
                "recommendation": "Run Cleanup + Storage Analyzer to reclaim space.",
            },
            {
                "severity": "Warning",
                "category": "Memory",
                "title": "RAM usage 93% of 16 GB",
                "recommendation": "Close heavy apps or review top consumers below.",
            },
            {
                "severity": "Warning",
                "category": "Drivers",
                "title": "Device problem: Unknown device (code 28)",
                "recommendation": "Open Device Manager.",
            },
        ]

        plan = AutoRepairEngine().build_plan(findings)

        self.assertEqual([item.action_id for item in plan.safe_actions], [
            "cleanup_temp_files",
            "reclaim_memory",
        ])
        self.assertEqual(len(plan.review_required), 1)
        self.assertIn("Drivers", plan.review_required[0].title)

    def test_execute_marks_action_verified_only_when_handler_verifies(self):
        engine = AutoRepairEngine()
        plan = engine.build_plan([
            {"severity": "Warning", "category": "Disk", "title": "System drive: 10% free"},
        ])

        result = engine.execute(
            plan,
            {"cleanup_temp_files": lambda: {"ok": True, "verified": True, "message": "Freed 1.0 GB"}},
        )

        self.assertEqual(result.completed_count, 1)
        self.assertEqual(result.verified_count, 1)
        self.assertEqual(result.failed_count, 0)
        self.assertEqual(result.outcomes[0].status, "verified")

    def test_execute_never_claims_success_when_handler_is_unverified_or_missing(self):
        engine = AutoRepairEngine()
        plan = engine.build_plan([
            {"severity": "Warning", "category": "Memory", "title": "RAM usage 85%"},
            {"severity": "Warning", "category": "Disk", "title": "System drive: 10% free"},
        ])

        result = engine.execute(
            plan,
            {"reclaim_memory": lambda: {"ok": True, "verified": False, "message": "Metric unchanged"}},
        )

        statuses = {item.action_id: item.status for item in result.outcomes}
        self.assertEqual(statuses["reclaim_memory"], "unverified")
        self.assertEqual(statuses["cleanup_temp_files"], "skipped")
        self.assertEqual(result.verified_count, 0)
        self.assertEqual(result.completed_count, 0)


if __name__ == "__main__":
    unittest.main()
