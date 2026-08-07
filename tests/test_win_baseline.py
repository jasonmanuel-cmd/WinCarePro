from types import SimpleNamespace
from unittest import mock

from win_baseline import WindowsBaselineAnalyzer


def test_failed_service_commands_are_not_reported_as_actions():
    failure = SimpleNamespace(returncode=5, stdout="", stderr="Access denied")
    with mock.patch("win_baseline.subprocess.run", return_value=failure):
        actions = WindowsBaselineAnalyzer().apply_optimization_preset("disable_printers")

    assert actions == 0
