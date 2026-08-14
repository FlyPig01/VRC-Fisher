"""Keep training-tool state inside the repository."""

from __future__ import annotations

import os
from pathlib import Path


TRAINING_ROOT = Path(__file__).resolve().parents[2]


def configure_ultralytics() -> Path:
    config_root = TRAINING_ROOT / ".ultralytics"
    os.environ["YOLO_CONFIG_DIR"] = str(config_root)
    return config_root
