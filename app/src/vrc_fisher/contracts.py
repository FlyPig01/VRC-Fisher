"""Data contracts shared by capture, vision, state, and automation layers."""

from __future__ import annotations

from dataclasses import dataclass
from enum import Enum
from typing import Protocol

import numpy as np


Box = tuple[int, int, int, int]


class Phase(str, Enum):
    IDLE = "idle"
    CASTING = "casting"
    WAITING_BITE = "waiting_bite"
    HOOKING = "hooking"
    MINIGAME = "minigame"
    REELING = "reeling"
    LOOT = "loot"
    RECOVERY = "recovery"
    STOPPED = "stopped"


@dataclass(frozen=True, slots=True)
class Frame:
    sequence: int
    captured_at_ns: int
    image_bgr: np.ndarray


@dataclass(frozen=True, slots=True)
class Observation:
    frame_sequence: int
    observed_at_ns: int
    fishing_ui: Box | None = None
    prompt: Box | None = None
    success: Box | None = None
    failure: Box | None = None
    cast_complete: bool = False
    target_y_norm: float | None = None
    control_top_norm: float | None = None
    control_bottom_norm: float | None = None
    progress_norm: float | None = None
    confidence: float = 0.0

    @property
    def has_fishing_ui(self) -> bool:
        return self.fishing_ui is not None

    @property
    def has_prompt(self) -> bool:
        return self.prompt is not None

    @property
    def has_success(self) -> bool:
        return self.success is not None

    @property
    def has_failure(self) -> bool:
        return self.failure is not None


class InputSink(Protocol):
    def click(self) -> None: ...

    def press(self) -> None: ...

    def release(self) -> None: ...


class Detector(Protocol):
    def observe(self, frame: Frame) -> Observation: ...


class Action(str, Enum):
    NONE = "none"
    CLICK = "click"
    PRESS = "press"
    RELEASE = "release"


@dataclass(frozen=True, slots=True)
class Decision:
    phase: Phase
    action: Action = Action.NONE
    reason: str = ""
    cycle: int = 0
