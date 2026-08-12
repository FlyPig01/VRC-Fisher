from pathlib import Path

import pytest

from vrc_training.config import load_train_config


def test_default_training_config_loads() -> None:
    path = Path(__file__).resolve().parents[1] / "configs/default.toml"
    config = load_train_config(path)

    assert config.device == "cpu"
    assert config.locator.data.endswith("locator/data.yaml")
    assert config.minigame.base_model == "yolo11n.pt"


def test_unknown_training_setting_is_rejected(tmp_path: Path) -> None:
    path = tmp_path / "invalid.toml"
    path.write_text("[runtime]\nunknown = 1\n", encoding="ascii")

    with pytest.raises(ValueError, match="unknown runtime settings"):
        load_train_config(path)
