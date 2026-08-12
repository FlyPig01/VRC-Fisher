"""Export reviewed locator and mini-game .pt checkpoints to ONNX."""

from __future__ import annotations

import argparse
from pathlib import Path
import shutil

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


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--locator", type=Path, required=True)
    parser.add_argument("--minigame", type=Path, required=True)
    parser.add_argument("--output", type=Path, default=Path("exports"))
    parser.add_argument("--image-size", type=int, default=640)
    args = parser.parse_args(argv)
    if args.image_size <= 0:
        parser.error("--image-size must be positive")
    for name, checkpoint in (("locator", args.locator), ("minigame", args.minigame)):
        destination = export_model(checkpoint, args.output / f"{name}.onnx", args.image_size)
        print(f"{name}={destination}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
