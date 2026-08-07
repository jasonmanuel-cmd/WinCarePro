"""pytest fixtures for WinCarePro Desktop E2E tests."""

import os
import subprocess

import pywinauto
import pytest

from tests.e2e.config import APP_PATH, APP_TITLE, LAUNCH_TIMEOUT


@pytest.fixture(scope="session")
def app_path():
    """Return the path to the WinCarePro.Desktop executable."""
    return APP_PATH


@pytest.fixture(scope="session")
def app_launch(tmp_path_factory):
    """Launch the WPF app and return the pywinauto Application instance.

    Teardown kills the app process after all session-scoped E2E tests finish.
    """
    sandbox = tmp_path_factory.mktemp("wpf_profile")
    env = os.environ.copy()
    env["WINDIR"] = env.get("WINDIR") or env.get("SystemRoot", r"C:\Windows")
    env["WINCAREPRO_CARE_ROOT"] = str(sandbox / "care")
    proc = subprocess.Popen(
        [APP_PATH], env=env, stdout=subprocess.DEVNULL,
        stderr=subprocess.DEVNULL, creationflags=0x08000000,
    )
    try:
        app = pywinauto.Application(backend="uia").connect(
            process=proc.pid, timeout=LAUNCH_TIMEOUT)
        app.window(title=APP_TITLE).wait("visible ready", timeout=LAUNCH_TIMEOUT)
    except Exception:
        proc.kill()
        raise
    yield app, proc
    # Teardown: close app gracefully, then force kill
    app.kill()
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
    window = app.window(title=APP_TITLE)
    window.wait("visible", timeout=LAUNCH_TIMEOUT)
    yield window


@pytest.fixture(scope="session")
def artifact_dir(tmp_path_factory):
    """Create a session-scoped artifact directory for screenshots and reports."""
    base = tmp_path_factory.mktemp("e2e_artifacts")
    return str(base)
