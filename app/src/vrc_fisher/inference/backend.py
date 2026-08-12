"""Shared object-detection contracts."""

from __future__ import annotations

from dataclasses import dataclass
from typing import Protocol

import numpy as np

from vrc_fisher.contracts import Box


@dataclass(frozen=True, slots=True)
class Detection:
    class_name: str
    confidence: float
    box: Box


class DetectionModel(Protocol):
    def detect(self, image_bgr: np.ndarray) -> list[Detection]: ...


__all__ = ["Detection", "DetectionModel"]
