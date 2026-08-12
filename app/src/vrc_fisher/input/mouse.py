"""Windows mouse input with an observe-only fallback."""

from __future__ import annotations

import ctypes
from time import sleep


LEFT_DOWN = 0x0002
LEFT_UP = 0x0004


class NoopInputSink:
    def click(self) -> None:
        return None

    def press(self) -> None:
        return None

    def release(self) -> None:
        return None


class MouseInputSink:
    def __init__(self, click_duration_seconds: float = 0.04) -> None:
        if click_duration_seconds < 0:
            raise ValueError("click duration cannot be negative")
        self._click_duration = click_duration_seconds
        self._pressed = False
        self._user32 = ctypes.windll.user32

    @property
    def pressed(self) -> bool:
        return self._pressed

    def click(self) -> None:
        self.press()
        sleep(self._click_duration)
        self.release()

    def press(self) -> None:
        if self._pressed:
            return
        self._user32.mouse_event(LEFT_DOWN, 0, 0, 0, 0)
        self._pressed = True

    def release(self) -> None:
        if not self._pressed:
            return
        self._user32.mouse_event(LEFT_UP, 0, 0, 0, 0)
        self._pressed = False
