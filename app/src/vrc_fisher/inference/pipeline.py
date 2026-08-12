"""Two-stage detector: full-screen locator followed by a mini-game crop model."""

from __future__ import annotations

from time import perf_counter_ns

import numpy as np

from vrc_fisher.config import VisionConfig
from vrc_fisher.contracts import Box, Frame, Observation
from vrc_fisher.models import resolve_model_path

from .backend import Detection, DetectionModel
from .locator import LOCATOR_CLASSES
from .minigame import MINIGAME_CLASSES
from .onnx_backend import OnnxYoloModel


class TwoStageOnnxDetector:
    def __init__(
        self,
        config: VisionConfig,
        app_root=None,
        locator: DetectionModel | None = None,
        minigame: DetectionModel | None = None,
    ) -> None:
        if not 0.0 <= config.crop_padding <= 0.5:
            raise ValueError("crop_padding must be between 0 and 0.5")
        self._config = config
        self._locator = locator or OnnxYoloModel(
            resolve_model_path(config.locator_model),
            LOCATOR_CLASSES,
            config.min_confidence,
            config.iou_threshold,
            config.input_size,
            config.intra_op_threads,
            config.device,
        )
        self._minigame = minigame or OnnxYoloModel(
            resolve_model_path(config.minigame_model),
            MINIGAME_CLASSES,
            config.min_confidence,
            config.iou_threshold,
            config.input_size,
            config.intra_op_threads,
            config.device,
        )
        self._last_locator_sequence: int | None = None
        self._locator_cache: list[Detection] = []

    def observe(self, frame: Frame) -> Observation:
        if (
            self._last_locator_sequence is None
            or frame.sequence < self._last_locator_sequence
            or frame.sequence - self._last_locator_sequence >= self._config.locator_interval_frames
        ):
            self._locator_cache = self._locator.detect(frame.image_bgr)
            self._last_locator_sequence = frame.sequence
        detections = self._locator_cache
        prompt = _best_box(detections, "prompt")
        fishing_ui = _best_box(detections, "fishing_ui_group")
        success = _best_box(detections, "success")
        failure = _best_box(detections, "failure")
        locator_confidence = max((item.confidence for item in detections), default=0.0)

        if fishing_ui is None:
            return Observation(
                frame_sequence=frame.sequence,
                observed_at_ns=perf_counter_ns(),
                prompt=prompt,
                success=success,
                failure=failure,
                confidence=locator_confidence,
            )

        crop_box = _padded_box(
            fishing_ui,
            frame.image_bgr.shape[1],
            frame.image_bgr.shape[0],
            self._config.crop_padding,
        )
        x1, y1, x2, y2 = crop_box
        local = self._minigame.detect(frame.image_bgr[y1:y2, x1:x2])
        rail = _best(local, "rail")
        control = _best(local, "control_bar")
        target = _best(local, "target")
        progress = _best(local, "progress_bar")
        target_y, control_top, control_bottom = _relative_positions(rail, control, target)
        confidence = max(
            [locator_confidence, *(item.confidence for item in local)],
            default=locator_confidence,
        )
        progress_norm = None
        if rail is not None and progress is not None:
            rail_height = max(1, rail.box[3] - rail.box[1])
            progress_norm = max(0.0, min(1.0, (progress.box[3] - progress.box[1]) / rail_height))

        return Observation(
            frame_sequence=frame.sequence,
            observed_at_ns=perf_counter_ns(),
            fishing_ui=fishing_ui,
            prompt=prompt,
            success=success,
            failure=failure,
            target_y_norm=target_y,
            control_top_norm=control_top,
            control_bottom_norm=control_bottom,
            progress_norm=progress_norm,
            confidence=confidence,
        )


def _best(detections: list[Detection], class_name: str) -> Detection | None:
    return next((item for item in detections if item.class_name == class_name), None)


def _best_box(detections: list[Detection], class_name: str) -> Box | None:
    detection = _best(detections, class_name)
    return detection.box if detection is not None else None


def _padded_box(box: Box, width: int, height: int, padding: float) -> Box:
    x1, y1, x2, y2 = box
    pad_x = int(round((x2 - x1) * padding))
    pad_y = int(round((y2 - y1) * padding))
    return max(0, x1 - pad_x), max(0, y1 - pad_y), min(width, x2 + pad_x), min(height, y2 + pad_y)


def _relative_positions(
    rail: Detection | None,
    control: Detection | None,
    target: Detection | None,
) -> tuple[float | None, float | None, float | None]:
    if rail is None:
        return None, None, None
    rail_top, rail_bottom = rail.box[1], rail.box[3]
    rail_height = max(1, rail_bottom - rail_top)

    def normalize(value: float) -> float:
        return max(0.0, min(1.0, (value - rail_top) / rail_height))

    target_y = None
    if target is not None:
        target_y = normalize((target.box[1] + target.box[3]) / 2)
    if control is None:
        return target_y, None, None
    return target_y, normalize(control.box[1]), normalize(control.box[3])
