"""TOML-backed runtime configuration."""

from __future__ import annotations

from dataclasses import dataclass, fields
from pathlib import Path
import tomllib
from typing import Any, TypeVar

from vrc_fisher.resources import resource_root


@dataclass(frozen=True, slots=True)
class WindowConfig:
    title_contains: str = "VRChat"
    activate_interval_seconds: float = 5.0


@dataclass(frozen=True, slots=True)
class CaptureConfig:
    monitor: int = 1
    target_fps: float = 30.0


@dataclass(frozen=True, slots=True)
class VisionConfig:
    locator_model: str = "models/locator.onnx"
    minigame_model: str = "models/minigame.onnx"
    device: str = "auto"
    input_size: int = 640
    intra_op_threads: int = 2
    locator_interval_frames: int = 3
    iou_threshold: float = 0.45
    crop_padding: float = 0.08
    prompt_confirm_frames: int = 2
    ui_confirm_frames: int = 4
    ui_lost_frames: int = 4
    success_confirm_frames: int = 6
    failure_confirm_frames: int = 4
    min_confidence: float = 0.35


@dataclass(frozen=True, slots=True)
class TimingConfig:
    cast_settle_seconds: float = 1.2
    bite_timeout_seconds: float = 45.0
    hook_to_ui_min_seconds: float = 0.4
    prompt_to_ui_timeout_seconds: float = 3.0
    minigame_timeout_seconds: float = 35.0
    reward_min_wait_seconds: float = 1.0
    loot_timeout_seconds: float = 7.0
    cycle_delay_seconds: float = 1.0
    recovery_delay_seconds: float = 2.0


@dataclass(frozen=True, slots=True)
class ControlConfig:
    vertical_deadband: float = 0.06
    click_duration_seconds: float = 0.04


@dataclass(frozen=True, slots=True)
class DebugConfig:
    save_failures: bool = True
    log_level: str = "INFO"


@dataclass(frozen=True, slots=True)
class AppConfig:
    window: WindowConfig = WindowConfig()
    capture: CaptureConfig = CaptureConfig()
    vision: VisionConfig = VisionConfig()
    timing: TimingConfig = TimingConfig()
    control: ControlConfig = ControlConfig()
    debug: DebugConfig = DebugConfig()


T = TypeVar("T")


def _section(cls: type[T], values: dict[str, Any]) -> T:
    allowed = {field.name for field in fields(cls)}
    unknown = set(values) - allowed
    if unknown:
        raise ValueError(f"unknown {cls.__name__} settings: {sorted(unknown)}")
    return cls(**values)


def load_config(path: str | Path | None = None) -> AppConfig:
    if path is None:
        path = resource_root() / "config" / "default.toml"
    config_path = Path(path)
    with config_path.open("rb") as stream:
        raw = tomllib.load(stream)
    known = {"window", "capture", "vision", "timing", "control", "debug"}
    unknown = set(raw) - known
    if unknown:
        raise ValueError(f"unknown config sections: {sorted(unknown)}")
    config = AppConfig(
        window=_section(WindowConfig, raw.get("window", {})),
        capture=_section(CaptureConfig, raw.get("capture", {})),
        vision=_section(VisionConfig, raw.get("vision", {})),
        timing=_section(TimingConfig, raw.get("timing", {})),
        control=_section(ControlConfig, raw.get("control", {})),
        debug=_section(DebugConfig, raw.get("debug", {})),
    )
    _validate(config)
    return config


def _validate(config: AppConfig) -> None:
    if config.capture.monitor < 1:
        raise ValueError("capture.monitor must be at least 1")
    if config.capture.target_fps <= 0:
        raise ValueError("capture.target_fps must be positive")
    if config.window.activate_interval_seconds <= 0:
        raise ValueError("window.activate_interval_seconds must be positive")
    vision = config.vision
    if vision.device not in {"auto", "cpu", "gpu"}:
        raise ValueError("vision.device must be one of: auto, cpu, gpu")
    if min(vision.input_size, vision.intra_op_threads, vision.locator_interval_frames) <= 0:
        raise ValueError(
            "vision input_size, intra_op_threads, and locator_interval_frames must be positive"
        )
    if not 0.0 <= vision.min_confidence <= 1.0:
        raise ValueError("vision.min_confidence must be between 0 and 1")
    if not 0.0 <= vision.iou_threshold <= 1.0:
        raise ValueError("vision.iou_threshold must be between 0 and 1")
    if not 0.0 <= vision.crop_padding <= 0.5:
        raise ValueError("vision.crop_padding must be between 0 and 0.5")
    confirmations = (
        vision.prompt_confirm_frames,
        vision.ui_confirm_frames,
        vision.ui_lost_frames,
        vision.success_confirm_frames,
        vision.failure_confirm_frames,
    )
    if min(confirmations) <= 0:
        raise ValueError("vision confirmation frame counts must be positive")
    timing_values = tuple(getattr(config.timing, field.name) for field in fields(TimingConfig))
    if min(timing_values) <= 0:
        raise ValueError("timing values must be positive")
    if not 0.0 <= config.control.vertical_deadband <= 0.5:
        raise ValueError("control.vertical_deadband must be between 0 and 0.5")
    if config.control.click_duration_seconds < 0:
        raise ValueError("control.click_duration_seconds cannot be negative")
