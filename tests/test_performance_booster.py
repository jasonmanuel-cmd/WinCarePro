import types
import unittest
from unittest import mock

from performance_booster import PerformanceBooster


class PerformanceBoosterSecurityTests(unittest.TestCase):
    @mock.patch("performance_booster.os.name", "nt")
    @mock.patch("performance_booster.subprocess.run")
    def test_dns_adapter_name_is_escaped_and_failed_fallback_is_reported(self, run):
        adapter = "bad'; Write-Output injected; '"
        run.side_effect = [
            types.SimpleNamespace(returncode=0, stdout=adapter + "\n", stderr=""),
            types.SimpleNamespace(returncode=1, stdout="", stderr="ps failed"),
            types.SimpleNamespace(returncode=1, stdout="", stderr="netsh failed"),
            types.SimpleNamespace(returncode=1, stdout="", stderr="netsh failed"),
            types.SimpleNamespace(returncode=0, stdout="", stderr=""),
        ]

        ok, message = PerformanceBooster().set_dns_servers("google")

        self.assertFalse(ok)
        self.assertIn("Failed to update DNS", message)
        # Verify positional args pattern: script uses $args[0], adapter passed separately
        powershell_script = run.call_args_list[1].args[0][4]  # script is 5th element (index 4)
        self.assertIn("$args[0]", powershell_script)
        # Adapter passed as separate argument (not interpolated into script)
        adapter_arg = run.call_args_list[1].args[0][5]  # 6th element
        self.assertEqual(adapter_arg, adapter)
        for call in run.call_args_list:
            self.assertIsNot(call.kwargs.get("shell"), True)


if __name__ == "__main__":
    unittest.main()
