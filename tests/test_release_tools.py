import sys
from pathlib import Path

import pytest

import release_tools


def test_release_tools_refuses_unsigned_manifest(monkeypatch, tmp_path: Path):
    artifact = tmp_path / "app.exe"
    artifact.write_bytes(b"not-a-real-executable")
    monkeypatch.delenv("WINCAREPRO_SIGN_CERT_THUMBPRINT", raising=False)
    monkeypatch.setattr(
        sys,
        "argv",
        ["release_tools.py", str(artifact), "--version", "1.3.0", "--url", "https://example.test/app.exe"],
    )

    with pytest.raises(SystemExit) as error:
        release_tools.main()

    assert error.value.code == 2
    assert not (tmp_path / "dist" / "update.json").exists()
