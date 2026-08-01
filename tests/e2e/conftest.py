"""pytest fixtures for WinCarePro Desktop E2E tests."""

import subprocess
import time

import pywinauto
import pytest

from tests.e2e.config import APP_PATH, APP_TITLE, LAUNCH_TIMEOUT


@pytest.fixture(scope="session")
def app_path():
    """Return the path to the WinCarePro.Desktop executable."""
    return APP_PATH


@pytest.fixture(scope="session")
def app_launch():
    """Launch the WPF app and return the pywinauto Application instance.

    Teardown kills the app process after all session-scoped E2E tests finish.
    """
    proc = subprocess.Popen(
        [APP_PATH],
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
    )
    time.sleep(LAUNCH_TIMEOUT)  # Allow WPF app to initialize
    try:
        app = pywinauto.Application(backend="uia").connect(timeout=10)
    except Exception:
        # App may already be running or failed to start; try top-level connect
        app = pywinauto.Application(backend="uia").connect(title=APP_TITLE)
    yield app, proc
    # Teardown: close app gracefully, then force kill
    try:
        app.kill()
    except Exception:
        pass
    try:
        proc.terminate()
        proc.wait(timeout=5)
    except subprocess.TimeoutExpired:
        proc.kill()


@pytest.fixture(scope="function")
def main_window(app_launch):
    """Return the main WPF window for interaction in a single test.

    Yields the pywinauto WindowSpecification so tests can interact with
    the UI via AutomationIds and control names.
    """
    app, _ = app_launch
    window = app.windows(title=APP_TITLE)[0]
    window.wait("visible", timeout=LAUNCH_TIMEOUT)
    yield window


@pytest.fixture(scope="session")
def artifact_dir(tmp_path_factory):
    """Create a session-scoped artifact directory for screenshots and reports."""
    base = tmp_path_factory.mktemp("e2e_artifacts")
    return str(base)