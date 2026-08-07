import unittest
from unittest import mock

from privacy_engine import PrivacyShield, privacy_protection_switches


class PrivacyProtectionSwitchTests(unittest.TestCase):
    def test_translates_engine_enabled_states_to_disable_switches(self):
        switches = privacy_protection_switches({
            "bing_start_search": True,
            "copilot_recall": False,
            "advertising_id": True,
            "telemetry_level": 0,
            "location_tracking": True,
            "app_diagnostics": False,
        })

        self.assertEqual(switches, {
            "bing": False,
            "copilot": True,
            "advertising_id": False,
            "telemetry": True,
            "location": False,
            "app_diagnostics": True,
        })

    def test_missing_values_default_to_not_claiming_protection(self):
        self.assertEqual(
            privacy_protection_switches({}),
            {"bing": False, "copilot": False, "advertising_id": False,
             "telemetry": False, "location": False, "app_diagnostics": False},
        )

    def test_multi_value_change_reports_partial_failure(self):
        shield = PrivacyShield()
        with mock.patch.object(shield, "_write_dword", side_effect=[True, False]):
            self.assertFalse(shield.set_bing_start_search(False))


if __name__ == "__main__":
    unittest.main()
