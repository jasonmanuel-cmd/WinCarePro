"""Prevent visible terminal and dependency-install regressions at startup."""

from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]


def test_batch_launcher_delegates_immediately_to_windowless_host():
    launcher = (ROOT / "run.bat").read_text(encoding="utf-8").casefold()

    assert "wscript.exe" in launcher
    assert "pip install" not in launcher
    assert "powershell" not in launcher
    assert "python " not in launcher
    assert "cmd.exe" not in launcher


def test_vbs_launcher_uses_venv_pythonw_and_hidden_elevation():
    launcher = (ROOT / "WinCarePro.vbs").read_text(encoding="utf-8").casefold()

    assert ".venv\\scripts\\pythonw.exe" in launcher
    assert '"runas", 0' in launcher
    assert "shell.shellexecute pythonw" in launcher


def test_wpf_bridge_suppresses_child_console_windows():
    source = (ROOT / "WinCarePro.Desktop" / "MainWindow.xaml.cs").read_text(encoding="utf-8")

    assert "UseShellExecute = false" in source
    assert "CreateNoWindow = true" in source
    assert "WindowStyle = ProcessWindowStyle.Hidden" in source
