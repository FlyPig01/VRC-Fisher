"""Runtime latency and detection counters."""

from __future__ import annotations

from dataclasses import asdict, dataclass
import json
from pathlib import Path
from time import perf_counter_ns

import numpy as np
import psutil

from vrc_fisher.contracts import Frame, Observation


@dataclass(frozen=True, slots=True)
class MetricsSnapshot:
    frames: int
    fps: float
    inference_mean_ms: float
    inference_p95_ms: float
    frame_age_p95_ms: float
    ui_frames: int
    prompt_frames: int
    success_frames: int
    process_rss_mb: float


class RuntimeMetrics:
    def __init__(self) -> None:
        self._started_ns = perf_counter_ns()
        self._inference_ms: list[float] = []
        self._frame_age_ms: list[float] = []
        self._ui_frames = 0
        self._prompt_frames = 0
        self._success_frames = 0

    def record(self, frame: Frame, observation: Observation, inference_ms: float) -> None:
        self._inference_ms.append(inference_ms)
        self._frame_age_ms.append((perf_counter_ns() - frame.captured_at_ns) / 1_000_000)
        self._ui_frames += int(observation.has_fishing_ui)
        self._prompt_frames += int(observation.has_prompt)
        self._success_frames += int(observation.has_success)

    def snapshot(self) -> MetricsSnapshot:
        frames = len(self._inference_ms)
        elapsed = max(1, perf_counter_ns() - self._started_ns) / 1_000_000_000
        process = psutil.Process()
        return MetricsSnapshot(
            frames=frames,
            fps=frames / elapsed,
            inference_mean_ms=float(np.mean(self._inference_ms)) if frames else 0.0,
            inference_p95_ms=float(np.percentile(self._inference_ms, 95)) if frames else 0.0,
            frame_age_p95_ms=float(np.percentile(self._frame_age_ms, 95)) if frames else 0.0,
            ui_frames=self._ui_frames,
            prompt_frames=self._prompt_frames,
            success_frames=self._success_frames,
            process_rss_mb=process.memory_info().rss / (1024 * 1024),
        )

    def summary(self) -> str:
        value = self.snapshot()
        return (
            f"frames={value.frames} fps={value.fps:.1f} "
            f"inference_p95={value.inference_p95_ms:.1f}ms "
            f"frame_age_p95={value.frame_age_p95_ms:.1f}ms "
            f"rss={value.process_rss_mb:.1f}MB"
        )

    def write(self, path: Path) -> None:
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(
            json.dumps(asdict(self.snapshot()), ensure_ascii=True, indent=2) + "\n",
            encoding="utf-8",
        )
