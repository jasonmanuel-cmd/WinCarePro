"""
WinCare Pro - Core health scoring.
"""
from __future__ import annotations


class HealthScore:
    """
    Weighted 0-100 score. Starts at 100 and deducts per metric.
    Weights (max deduction): disk free 20, disk health 20, event errors 15,
    RAM 15, CPU 10, startup bloat 10, uptime 5, pending updates 5.
    """

    @staticmethod
    def compute(m: dict):
        """m = metrics dict from Scanner. Returns (score:int, breakdown:list[str])."""
        score, notes = 100.0, []

        def ding(points, why):
            nonlocal score
            if points > 0:
                score -= points
                notes.append(f"-{points:.0f}  {why}")

        free = m.get("disk_free_pct", 50)
        if free < 5:
            ding(20, f"System drive almost full ({free}% free)")
        elif free < 10:
            ding(14, f"System drive very low ({free}% free)")
        elif free < 20:
            ding(7, f"System drive low ({free}% free)")

        if m.get("disk_unhealthy"):
            ding(20, "A physical disk reports non-healthy SMART status")

        errs = m.get("event_errors", 0)
        if errs > 200:
            ding(15, f"{errs} system errors in Event Log (7 days)")
        elif errs > 50:
            ding(10, f"{errs} system errors in Event Log (7 days)")
        elif errs > 10:
            ding(5, f"{errs} system errors in Event Log (7 days)")

        ram = m.get("ram_pct", 0)
        if ram > 92:
            ding(15, f"RAM critically high ({ram}%)")
        elif ram > 80:
            ding(8, f"RAM elevated ({ram}%)")

        cpu = m.get("cpu_pct", 0)
        if cpu > 90:
            ding(10, f"CPU sustained very high ({cpu}%)")
        elif cpu > 70:
            ding(5, f"CPU elevated ({cpu}%)")

        # Threshold aligned with Scanner.check_startup (warns above 10) so the
        # score never deducts for a count the findings call "reasonable".
        sc = m.get("startup_count", 0)
        if sc > 15:
            ding(10, f"{sc} enabled startup programs")
        elif sc > 10:
            ding(5, f"{sc} enabled startup programs")

        up_days = m.get("uptime_days", 0)
        if up_days > 14:
            ding(5, f"No reboot for {up_days} days")
        elif up_days > 7:
            ding(2, f"No reboot for {up_days} days")

        pu = m.get("pending_updates", 0)
        if pu >= 5:
            ding(5, f"{pu} pending Windows updates")
        elif pu > 0:
            ding(2, f"{pu} pending Windows updates")

        if m.get("driver_issues", 0) > 0:
            ding(5, f"{m['driver_issues']} device(s) reporting driver problems")

        return max(0, min(100, round(score))), notes

    @staticmethod
    def grade(score: int) -> str:
        if score >= 90:
            return "Excellent"
        if score >= 75:
            return "Good"
        if score >= 55:
            return "Fair"
        if score >= 35:
            return "Poor"
        return "Critical"