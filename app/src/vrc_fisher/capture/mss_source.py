"""Full-monitor capture backed by mss."""

from __future__ import annotations

from time import perf_counter_ns

import mss
import numpy as np

from vrc_fisher.contracts import Frame


class MssSource:
    def __init__(self, monitor: int = 1) -> None:
        self._monitor_index = monitor
        self._sequence = 0
        self._capture: mss.mss | None = None

    def __enter__(self) -> "MssSource":
        self._capture = mss.mss()
        if not 1 <= self._monitor_index < len(self._capture.monitors):
            self.close()
            raise ValueError(f"monitor {self._monitor_index} does not exist")
        return self

    def __exit__(self, *_: object) -> None:
        self.close()

    def close(self) -> None:
        if self._capture is not None:
            self._capture.close()
            self._capture = None

    def grab(self) -> Frame:
        if self._capture is None:
            raise RuntimeError("MssSource must be opened with a context manager")
        shot = self._capture.grab(self._capture.monitors[self._monitor_index])
        image = np.asarray(shot, dtype=np.uint8)[:, :, :3].copy()
        frame = Frame(self._sequence, perf_counter_ns(), image)
        self._sequence += 1
        return frame
