import json

import release_config


def test_environment_overrides_bundled_public_configuration(monkeypatch, tmp_path):
    config = tmp_path / "release_config.json"
    config.write_text(json.dumps({"SETTING": "bundled"}), encoding="utf-8")
    monkeypatch.setattr(release_config, "_config_path", lambda: config)
    monkeypatch.setenv("SETTING", "environment")

    assert release_config.release_setting("SETTING") == "environment"


def test_invalid_bundled_configuration_fails_empty(monkeypatch, tmp_path):
    config = tmp_path / "release_config.json"
    config.write_text("not json", encoding="utf-8")
    monkeypatch.setattr(release_config, "_config_path", lambda: config)

    assert release_config.release_setting("MISSING") == ""
