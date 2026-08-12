"""Train one or both VRC-Fisher object detectors."""

from __future__ import annotations

import argparse
from pathlib import Path

from .config import TaskConfig, load_train_config


def _ultralytics_model(base_model: str):
    try:
        from ultralytics import YOLO
    except ImportError as error:
        raise RuntimeError(
            "training dependencies are missing; install torch, torchvision, and ultralytics"
        ) from error
    return YOLO(base_model)


def train_task(
    name: str,
    task: TaskConfig,
    root: Path,
    device: str,
    workers: int,
    seed: int,
):
    data = (root / task.data).resolve()
    if not data.is_file():
        raise FileNotFoundError(f"{name} dataset config not found: {data}")
    model = _ultralytics_model(task.base_model)
    return model.train(
        data=str(data),
        imgsz=task.image_size,
        epochs=task.epochs,
        batch=task.batch,
        patience=task.patience,
        device=device,
        workers=workers,
        seed=seed,
        project=str(root / "runs"),
        name=name,
        exist_ok=False,
        pretrained=True,
        plots=True,
    )


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--config", type=Path, default=Path("configs/default.toml"))
    parser.add_argument("--task", choices=("locator", "minigame", "all"), default="all")
    args = parser.parse_args(argv)
    root = Path.cwd()
    config = load_train_config(args.config)
    names = ("locator", "minigame") if args.task == "all" else (args.task,)
    for name in names:
        train_task(
            name,
            getattr(config, name),
            root,
            config.device,
            config.workers,
            config.seed,
        )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
