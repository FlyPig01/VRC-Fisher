"""Train one or both VRC-Fisher object detectors."""

from __future__ import annotations

import argparse
from pathlib import Path

from .config import TaskConfig, load_train_config
from .environment import configure_ultralytics
from .preflight import preflight_task


def _ultralytics_model(base_model: str):
    configure_ultralytics()
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
    preflight_report=None,
):
    report = preflight_report or preflight_task(name, task, root)
    data = (root / task.data).resolve()
    run_name = task.run_name or name
    run_directory = root / "runs" / run_name
    if run_directory.exists():
        raise FileExistsError(f"training run already exists: {run_directory}")
    print(
        f"{run_name} preflight: recordings={report.recordings} images={report.images} "
        f"positive={report.positives} negative={report.negatives} boxes={report.boxes}"
    )
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
        name=run_name,
        exist_ok=False,
        pretrained=True,
        plots=True,
    )


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--config", type=Path, default=Path("configs/default.toml"))
    parser.add_argument("--task", choices=("locator", "minigame", "all"), default="all")
    parser.add_argument(
        "--confirm-reviewed",
        action="store_true",
        help="confirm that generated previews and train/val assignments were reviewed",
    )
    args = parser.parse_args(argv)
    if not args.confirm_reviewed:
        parser.error("training requires --confirm-reviewed after manual dataset review")
    root = Path.cwd()
    config = load_train_config(args.config)
    names = ("locator", "minigame") if args.task == "all" else (args.task,)
    try:
        reports = {
            name: preflight_task(name, getattr(config, name), root)
            for name in names
        }
    except ValueError as error:
        parser.error(str(error))
    for name in names:
        train_task(
            name,
            getattr(config, name),
            root,
            config.device,
            config.workers,
            config.seed,
            reports[name],
        )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
