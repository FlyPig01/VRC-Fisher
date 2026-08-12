from pathlib import Path

import pytest

from vrc_fisher.config import load_config


def test_default_config_loads() -> None:
    config = load_config()
    assert config.window.title_contains == "VRChat"
    assert config.capture.target_fps == 30.0
    assert config.vision.ui_lost_frames >= 2


def test_unknown_setting_is_rejected(tmp_path: Path) -> None:
    path = tmp_path / "invalid.toml"
    path.write_text("[capture]\nunknown = 1\n", encoding="utf-8")
    with pytest.raises(ValueError, match="unknown CaptureConfig settings"):
        load_config(path)


def test_invalid_runtime_value_is_rejected(tmp_path: Path) -> None:
    path = tmp_path / "invalid.toml"
    path.write_text("[capture]\ntarget_fps = 0\n", encoding="ascii")

    with pytest.raises(ValueError, match="target_fps"):
        load_config(path)
