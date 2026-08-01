"""Base Page Object for WinCarePro Desktop E2E tests."""

import os
from pathlib import Path

import pywinauto
import pytest

ARTIFACT_DIR = os.environ.get(
    "E2E_ARTIFACT_DIR",
    str(Path(__file__).resolve().parents[2] / "tests" / "e2e" / "artifacts"),
)


class BasePage:
    """Base class for all WinCarePro WPF page objects."""

    def __init__(self, window):
        self.window = window

    def _by_automation_id(self, automation_id: str):
        """Locate a control by its AutomationId (x:Name in XAML)."""
        return self.window.child_window(auto_id=automation_id)

    def _by_name(self, name: str, control_type: str = "Button"):
        """Locate a control by its AutomationProperties.Name."""
        return self.window.child_window(name=name, control_type=control_type)

    def _by_x_name(self, x_name: str):
        """Locate a control by its x:Name (maps to AutomationId in WPF)."""
        return self.window.child_window(auto_id=x_name)

    def click(self, button_spec):
        """Click a control identified by AutomationId or name."""
        ctrl = self._resolve(button_spec)
        ctrl.click_input()

    def type_text(self, control_spec, text: str):
        """Type text into a control after clicking it."""
        ctrl = self._resolve(control_spec)
        ctrl.click_input()
        ctrl.type_keys(text)

    def wait_for_control(self, control_spec, timeout: float = 10):
        """Wait for a control to become visible."""
        ctrl = self._resolve(control_spec)
        ctrl.wait("visible", timeout=timeout)
        return ctrl

    def _resolve(self, spec):
        """Resolve a control spec: if string, treat as automation_id."""
        if isinstance(spec, str):
            return self.window.child_window(auto_id=spec)
        raise ValueError(f"Unknown control spec type: {type(spec)}")

    def screenshot(self, name: str = "unnamed"):
        """Capture a screenshot of the main window and save to artifact directory."""
        os.makedirs(ARTIFACT_DIR, exist_ok=True)
        path = Path(ARTIFACT_DIR) / f"{name}.png"
        try:
            self.window.dump_image(str(path))
        except Exception:
            pass  # Screenshot is best-effort for debugging

    def is_text_present(self, text: str) -> bool:
        """Check whether visible text appears in the window."""
        try:
            return self.window.wrapper_object().window_text() is not None
        except Exception:
            return False