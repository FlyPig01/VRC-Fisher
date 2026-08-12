"""Replay extracted full-screen frames through production vision and state logic."""

from __future__ import annotations

from dataclasses import asdict
import json
from pathlib import Path
from time import perf_counter, perf_counter_ns

import numpy as np
from PIL import Image

from vrc_fisher.config import AppConfig
from vrc_fisher.contracts import Action, Detector, Frame
from vrc_fisher.inference import TwoStageOnnxDetector
from vrc_fisher.resources import resource_root
from vrc_fisher.state.machine import FishingStateMachine
from vrc_fisher.telemetry.metrics import RuntimeMetrics


class ReplayError(RuntimeError):
    pass


def replay_frames(
    manifest: Path,
    frames_root: Path,
    config: AppConfig,
    events_path: Path,
    detector: Detector | None = None,
) -> dict[str, object]:
    rows = _read_manifest(manifest)
    if not rows:
        raise ReplayError(f"manifest contains no frames: {manifest}")
    vision = detector or TwoStageOnnxDetector(config.vision, resource_root())
    state = FishingStateMachine(config)
    metrics = RuntimeMetrics()
    events_path.parent.mkdir(parents=True, exist_ok=True)
    events: list[dict[str, object]] = []
    previous_phase = state.phase

    with events_path.open("w", encoding="utf-8") as stream:
        for sequence, row in enumerate(rows):
            relative_image = row.get("image")
            timestamp = row.get("timestamp_seconds")
            if not isinstance(relative_image, str) or not isinstance(timestamp, (int, float)):
                raise ReplayError(f"invalid manifest row {sequence + 1}")
            image_path = frames_root / Path(relative_image)
            if not image_path.is_file():
                raise ReplayError(f"frame does not exist: {image_path}")
            with Image.open(image_path) as image:
                rgb = np.asarray(image.convert("RGB"), dtype=np.uint8)
            frame = Frame(sequence, perf_counter_ns(), rgb[:, :, ::-1].copy())
            started = perf_counter()
            observation = vision.observe(frame)
            inference_ms = (perf_counter() - started) * 1000
            decision = state.step(observation, float(timestamp))
            metrics.record(frame, observation, inference_ms)
            if decision.phase is not previous_phase or decision.action is not Action.NONE:
                event = {
                    "timestamp_seconds": round(float(timestamp), 6),
                    "frame": sequence,
                    "phase": decision.phase.value,
                    "action": decision.action.value,
                    "reason": decision.reason,
                    "cycle": decision.cycle,
                    "confidence": round(observation.confidence, 6),
                }
                stream.write(json.dumps(event, ensure_ascii=True) + "\n")
                events.append(event)
                previous_phase = decision.phase

    return {
        "manifest": str(manifest),
        "frames": len(rows),
        "events": events,
        "metrics": asdict(metrics.snapshot()),
    }


def _read_manifest(path: Path) -> list[dict[str, object]]:
    if not path.is_file():
        raise ReplayError(f"manifest does not exist: {path}")
    rows: list[dict[str, object]] = []
    for line_number, raw in enumerate(path.read_text(encoding="utf-8").splitlines(), 1):
        if not raw.strip():
            continue
        try:
            value = json.loads(raw)
        except json.JSONDecodeError as error:
            raise ReplayError(f"invalid JSON at {path}:{line_number}") from error
        if not isinstance(value, dict):
            raise ReplayError(f"manifest row {line_number} is not an object")
        rows.append(value)
    return rows
