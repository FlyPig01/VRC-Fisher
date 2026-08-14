"""Export reviewed locator and mini-game .pt checkpoints to ONNX."""

from __future__ import annotations

import argparse
from pathlib import Path
import shutil

from .config import load_train_config
from .train import _ultralytics_model


def export_model(source: Path, destination: Path, image_size: int) -> Path:
    if not source.is_file():
        raise FileNotFoundError(f"checkpoint not found: {source}")
    result = _ultralytics_model(str(source)).export(
        format="onnx",
        imgsz=image_size,
        simplify=True,
        dynamic=False,
        nms=False,
        opset=17,
    )
    exported = Path(result)
    destination.parent.mkdir(parents=True, exist_ok=True)
    shutil.copy2(exported, destination)
    return destination


def export_image_sizes(
    config,
    locator_override: int | None,
    minigame_override: int | None,
) -> dict[str, int]:
    sizes = {
        "locator": (
            config.locator.image_size if locator_override is None else locator_override
        ),
        "minigame": (
            config.minigame.image_size if minigame_override is None else minigame_override
        ),
    }
    if any(size <= 0 for size in sizes.values()):
        raise ValueError("export image sizes must be positive")
    return sizes


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--config", type=Path, default=Path("configs/default.toml"))
    parser.add_argument("--locator", type=Path, required=True)
    parser.add_argument("--minigame", type=Path, required=True)
    parser.add_argument("--output", type=Path, default=Path("exports"))
    parser.add_argument("--locator-image-size", type=int)
    parser.add_argument("--minigame-image-size", type=int)
    args = parser.parse_args(argv)
    config = load_train_config(args.config)
    try:
        image_sizes = export_image_sizes(
            config,
            args.locator_image_size,
            args.minigame_image_size,
        )
    except ValueError as error:
        parser.error(str(error))
    for name, checkpoint in (("locator", args.locator), ("minigame", args.minigame)):
        destination = export_model(
            checkpoint,
            args.output / f"{name}.onnx",
            image_sizes[name],
        )
        print(f"{name}={destination} image_size={image_sizes[name]}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
