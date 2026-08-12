"""Train one or both VRC-Fisher object detectors."""

from __future__ import annotations

import argparse
from pathlib import Path
import re

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
    _require_reviewed_dataset(data, name)
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


def _require_reviewed_dataset(data: Path, name: str) -> None:
    text = data.read_text(encoding="utf-8")
    path_match = re.search(r"^path:\s*(.+?)\s*$", text, re.MULTILINE)
    dataset_root = (data.parent / (path_match.group(1) if path_match else ".")).resolve()
    missing: list[str] = []
    for split in ("train", "val"):
        image_dir = dataset_root / "images" / split
        label_dir = dataset_root / "labels" / split
        images = [path for path in image_dir.glob("*") if path.is_file()] if image_dir.is_dir() else []
        labels = [path for path in label_dir.glob("*.txt") if path.is_file()] if label_dir.is_dir() else []
        non_empty = [path for path in labels if path.stat().st_size > 0]
        if not images or not labels or not non_empty:
            missing.append(split)
    if missing:
        raise RuntimeError(
            f"{name} dataset has no reviewed non-empty {', '.join(missing)} split; "
            "the current tiny/unannotated dataset is a normal blocking condition, so training stopped"
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
