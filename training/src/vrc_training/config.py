"""TOML training configuration."""

from __future__ import annotations

from dataclasses import dataclass, fields
from pathlib import Path
import tomllib
from typing import Any, TypeVar


@dataclass(frozen=True, slots=True)
class TaskConfig:
    data: str
    base_model: str = "yolo11n.pt"
    image_size: int = 640
    epochs: int = 100
    batch: int = 8
    patience: int = 20


@dataclass(frozen=True, slots=True)
class TrainConfig:
    device: str = "cpu"
    workers: int = 4
    seed: int = 42
    locator: TaskConfig = TaskConfig("datasets/locator/data.yaml")
    minigame: TaskConfig = TaskConfig("datasets/minigame/data.yaml")


T = TypeVar("T")


def _load_dataclass(cls: type[T], raw: dict[str, Any]) -> T:
    allowed = {field.name for field in fields(cls)}
    unknown = set(raw) - allowed
    if unknown:
        raise ValueError(f"unknown {cls.__name__} settings: {sorted(unknown)}")
    return cls(**raw)


def load_train_config(path: Path) -> TrainConfig:
    with path.open("rb") as stream:
        raw = tomllib.load(stream)
    known = {"runtime", "locator", "minigame"}
    unknown = set(raw) - known
    if unknown:
        raise ValueError(f"unknown training sections: {sorted(unknown)}")
    runtime = raw.get("runtime", {})
    runtime_allowed = {"device", "workers", "seed"}
    unknown_runtime = set(runtime) - runtime_allowed
    if unknown_runtime:
        raise ValueError(f"unknown runtime settings: {sorted(unknown_runtime)}")
    config = TrainConfig(
        **runtime,
        locator=_load_dataclass(TaskConfig, raw.get("locator", {"data": "datasets/locator/data.yaml"})),
        minigame=_load_dataclass(TaskConfig, raw.get("minigame", {"data": "datasets/minigame/data.yaml"})),
    )
    for name, task in (("locator", config.locator), ("minigame", config.minigame)):
        if min(task.image_size, task.epochs, task.batch, task.patience) <= 0:
            raise ValueError(f"{name} numeric settings must be positive")
    if config.workers < 0:
        raise ValueError("workers cannot be negative")
    return config
