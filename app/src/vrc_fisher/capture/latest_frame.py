"""Thread-safe capacity-one frame slot."""

from __future__ import annotations

from threading import Condition

from vrc_fisher.contracts import Frame


class LatestFrameSlot:
    def __init__(self) -> None:
        self._condition = Condition()
        self._frame: Frame | None = None

    def put(self, frame: Frame) -> None:
        with self._condition:
            self._frame = frame
            self._condition.notify_all()

    def get(self) -> Frame | None:
        with self._condition:
            return self._frame

    def wait_for_newer(self, sequence: int, timeout: float | None = None) -> Frame | None:
        with self._condition:
            self._condition.wait_for(
                lambda: self._frame is not None and self._frame.sequence > sequence,
                timeout=timeout,
            )
            if self._frame is None or self._frame.sequence <= sequence:
                return None
            return self._frame
