"""Windows game-window discovery and activation."""

from .win32 import GameWindow, WindowNotFoundError

__all__ = ["GameWindow", "WindowNotFoundError"]
