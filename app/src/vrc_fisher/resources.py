"""Locate the single application directory used by source and installed builds."""

from __future__ import annotations

from pathlib import Path
import sys


def software_root() -> Path:
    if getattr(sys, "frozen", False):
        return Path(sys.executable).resolve().parent
    return Path(__file__).resolve().parents[2]


def resource_root() -> Path:
    return software_root()


def user_data_root() -> Path:
    return software_root()


def release_metadata_path() -> Path:
    return resource_root() / "release.json"
