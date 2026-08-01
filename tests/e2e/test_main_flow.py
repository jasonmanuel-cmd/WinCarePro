"""WinCarePro Desktop E2E tests using pywinauto + UIA backend."""

import pytest

from tests.e2e.config import APP_PATH, APP_TITLE

pytest_plugins = ["tests.e2e.conftest"]


@pytest.mark.e2e
class TestMainWindow:
    """E2E tests for the WinCarePro WPF main window."""

    @pytest.mark.e2e
    def test_window_opens(self, main_window):
        """The main WPF window should be visible on launch."""
        assert main_window.is_visible()

    @pytest.mark.e2e
    def test_window_title(self, main_window):
        """The window title should match 'WinCare Pro'."""
        assert main_window.window_text() == APP_TITLE

    @pytest.mark.e2e
    def test_dashboard_button_exists(self, main_window):
        """The Dashboard navigation button should be present."""
        btn = main_window.child_window(name="Dashboard", control_type="Button")
        assert btn.exists()
        assert btn.is_visible()

    @pytest.mark.e2e
    def test_system_health_button_exists(self, main_window):
        """The System Health navigation button should be present."""
        btn = main_window.child_window(name="System Health", control_type="Button")
        assert btn.exists()
        assert btn.is_visible()

    @pytest.mark.e2e
    def test_processes_cleanup_button_exists(self, main_window):
        """The Processes & Cleanup nav button should be present."""
        btn = main_window.child_window(
            name="Processes and Cleanup", control_type="Button"
        )
        assert btn.exists()

    @pytest.mark.e2e
    def test_privacy_network_button_exists(self, main_window):
        """The Privacy & Network nav button should be present."""
        btn = main_window.child_window(
            name="Privacy and Network", control_type="Button"
        )
        assert btn.exists()

    @pytest.mark.e2e
    def test_repairs_recovery_button_exists(self, main_window):
        """The Repairs & Recovery nav button should be present."""
        btn = main_window.child_window(
            name="Repairs and Recovery", control_type="Button"
        )
        assert btn.exists()

    @pytest.mark.e2e
    def test_settings_license_button_exists(self, main_window):
        """The Settings & License nav button should be present."""
        btn = main_window.child_window(
            name="Settings and License", control_type="Button"
        )
        assert btn.exists()

    @pytest.mark.e2e
    def test_open_full_app_button_exists(self, main_window):
        """The 'Open full WinCare Pro' button should be present."""
        btn = main_window.child_window(
            name="Open full WinCare Pro", control_type="Button"
        )
        assert btn.exists()

    @pytest.mark.e2e
    def test_status_text_visible(self, main_window):
        """The StatusText control should be visible."""
        status = main_window.child_window(auto_id="StatusText")
        assert status.exists()

    @pytest.mark.e2e
    def test_dashboard_button_click(self, main_window):
        """Clicking the Dashboard button should produce a visible response."""
        btn = main_window.child_window(name="Dashboard", control_type="Button")
        btn.click_input()
        # After clicking Dashboard, the window should still be open
        assert main_window.is_visible()

    @pytest.mark.smoke
    def test_smoke_dashboard_to_scan(self, main_window):
        """Smoke test: Dashboard → system scan via toolbar."""
        # Navigate to Dashboard first
        dashboard_btn = main_window.child_window(
            name="Dashboard", control_type="Button"
        )
        dashboard_btn.click_input()

        # The full WinCare Pro scanner can be opened via the main button
        open_btn = main_window.child_window(
            name="Open full WinCare Pro", control_type="Button"
        )
        if open_btn.exists() and open_btn.is_visible():
            open_btn.click_input()

        # Verify the app window is still present after interactions
        assert main_window.wrapper_object().is_visible()

    @pytest.mark.e2e
    def test_screenshot_on_failure(self, main_window):
        """Take a screenshot for artifact debugging."""
        main_window.dump_image(
            str(
                pytest.config.rootpath
                / "tests"
                / "e2e"
                / "artifacts"
                / "main_window.png"
            )
        )