"""State-based UIA tests for the current Guided Care shell."""

import time

import pytest
from pywinauto.keyboard import send_keys

pytest_plugins = ["tests.e2e.conftest"]


def wait_until(predicate, timeout=30):
    deadline = time.monotonic() + timeout
    while time.monotonic() < deadline:
        if predicate():
            return
        time.sleep(0.2)
    raise TimeoutError("UI state did not reach the expected value")


@pytest.mark.e2e
def test_accessible_navigation_and_actions_exist(main_window):
    expected = {
        "Refresh Care dashboard", "Open Safety Center", "Open Undo Center",
        "Start guided scan", "Refresh Guided Care",
        "Stop current Guided Care operation",
        "Preview privacy-safe support summary", "Copy reviewed support summary",
    }
    names = {
        control.element_info.name
        for control in main_window.descendants(control_type="Button")
    }
    assert expected <= names


@pytest.mark.e2e
def test_refresh_click_reaches_ready_state(main_window):
    main_window.child_window(auto_id="RefreshButton").click_input()
    status = main_window.child_window(auto_id="StatusText")
    wait_until(lambda: "ready" in status.window_text().lower())


@pytest.mark.e2e
def test_support_preview_enables_copy_without_sending(main_window):
    main_window.child_window(auto_id="PreviewSupportButton").wrapper_object().invoke()
    copy_button = main_window.child_window(auto_id="CopySupportButton")
    preview = main_window.child_window(auto_id="SupportSummaryText")
    wait_until(lambda: copy_button.is_enabled())
    assert preview.window_text().strip()
    assert "nothing has been sent" in main_window.child_window(
        auto_id="StatusText").window_text().lower()


@pytest.mark.e2e
def test_keyboard_refresh_shortcut_preserves_window(main_window):
    main_window.set_focus()
    send_keys("^d")
    wait_until(lambda: "ready" in main_window.child_window(
        auto_id="StatusText").window_text().lower())
    assert main_window.is_visible()


@pytest.mark.e2e
def test_screenshot_artifact(main_window, artifact_dir):
    main_window.capture_as_image().save(f"{artifact_dir}\\guided-care.png")
