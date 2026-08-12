"""Minimal Win32 window management without pywin32."""

from __future__ import annotations

import ctypes
from ctypes import wintypes


SW_RESTORE = 9


class WindowNotFoundError(RuntimeError):
    pass


class GameWindow:
    def __init__(self, title_contains: str) -> None:
        if not title_contains.strip():
            raise ValueError("window title fragment cannot be empty")
        self._title_contains = title_contains.casefold()
        self._user32 = ctypes.windll.user32
        self._configure_api()
        self._handle: int | None = None

    def _configure_api(self) -> None:
        self._user32.EnumWindows.argtypes = [ctypes.c_void_p, wintypes.LPARAM]
        self._user32.EnumWindows.restype = wintypes.BOOL
        self._user32.IsWindowVisible.argtypes = [wintypes.HWND]
        self._user32.IsWindowVisible.restype = wintypes.BOOL
        self._user32.GetWindowTextLengthW.argtypes = [wintypes.HWND]
        self._user32.GetWindowTextLengthW.restype = ctypes.c_int
        self._user32.GetWindowTextW.argtypes = [wintypes.HWND, wintypes.LPWSTR, ctypes.c_int]
        self._user32.GetWindowTextW.restype = ctypes.c_int
        self._user32.ShowWindow.argtypes = [wintypes.HWND, ctypes.c_int]
        self._user32.ShowWindow.restype = wintypes.BOOL
        self._user32.SetForegroundWindow.argtypes = [wintypes.HWND]
        self._user32.SetForegroundWindow.restype = wintypes.BOOL
        self._user32.GetForegroundWindow.argtypes = []
        self._user32.GetForegroundWindow.restype = wintypes.HWND

    @property
    def handle(self) -> int | None:
        return self._handle

    def find(self) -> int:
        matches: list[int] = []
        callback_type = ctypes.WINFUNCTYPE(wintypes.BOOL, wintypes.HWND, wintypes.LPARAM)

        @callback_type
        def callback(handle: int, _parameter: int) -> int:
            if not self._user32.IsWindowVisible(handle):
                return 1
            length = self._user32.GetWindowTextLengthW(handle)
            if length <= 0:
                return 1
            buffer = ctypes.create_unicode_buffer(length + 1)
            self._user32.GetWindowTextW(handle, buffer, len(buffer))
            if self._title_contains in buffer.value.casefold():
                matches.append(int(handle))
                return 0
            return 1

        self._user32.EnumWindows(callback, 0)
        if not matches:
            raise WindowNotFoundError(
                f"no visible window contains {self._title_contains!r}"
            )
        self._handle = matches[0]
        return self._handle

    def activate(self) -> bool:
        handle = self._handle or self.find()
        self._user32.ShowWindow(handle, SW_RESTORE)
        return bool(self._user32.SetForegroundWindow(handle))

    def is_foreground(self) -> bool:
        return self._handle is not None and int(self._user32.GetForegroundWindow()) == self._handle


def emergency_stop_pressed() -> bool:
    return bool(ctypes.windll.user32.GetAsyncKeyState(0x77) & 0x8000)  # F8
