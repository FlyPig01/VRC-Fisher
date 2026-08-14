"""Render generated YOLO labels onto review-only JPEG images."""

from __future__ import annotations

import argparse
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont

from .generated_output import staged_output
from .labels import Label, read_labels


TASK_CLASSES = {
    "locator": ("bite_indicator", "minigame_panel"),
    "minigame": ("catch_zone", "moving_target"),
}
COLORS = ((255, 72, 72), (40, 220, 120))


def build_review_images(source: Path, output: Path, task: str, max_size: int = 1280) -> int:
    if task not in TASK_CLASSES:
        raise ValueError(f"unknown review task: {task}")
    if max_size <= 0:
        raise ValueError("max_size must be positive")
    images = sorted(
        path
        for extension in ("*.jpg", "*.jpeg", "*.png")
        for path in (source / "images").glob(f"*/{extension}")
    )
    if not images:
        raise ValueError(f"no generated {task} images found: {source}")
    with staged_output(output) as staging:
        for image_path in images:
            label_path = source / "labels" / image_path.parent.name / f"{image_path.stem}.txt"
            if not label_path.is_file():
                raise ValueError(f"generated image has no label: {image_path}")
            labels = read_labels(label_path)
            with Image.open(image_path) as source_image:
                image = source_image.convert("RGB")
            scale = min(1.0, max_size / max(image.size))
            if scale < 1.0:
                image = image.resize(
                    (max(1, round(image.width * scale)), max(1, round(image.height * scale))),
                    Image.Resampling.LANCZOS,
                )
            _draw_labels(image, labels, TASK_CLASSES[task])
            destination = staging / image_path.parent.name / image_path.name
            destination.parent.mkdir(parents=True, exist_ok=True)
            image.save(destination, format="JPEG", quality=92)
    return len(images)


def _draw_labels(image: Image.Image, labels: list[Label], names: tuple[str, ...]) -> None:
    draw = ImageDraw.Draw(image)
    font = ImageFont.load_default(size=max(12, min(image.size) // 35))
    line_width = max(2, min(image.size) // 250)
    if not labels:
        text = "NEGATIVE"
        box = draw.textbbox((0, 0), text, font=font, stroke_width=1)
        padding = max(4, line_width * 2)
        draw.rectangle(
            (padding, padding, box[2] + padding * 3, box[3] + padding * 3),
            fill=(0, 0, 0),
        )
        draw.text(
            (padding * 2, padding * 2), text, fill=(255, 255, 255), font=font, stroke_width=1
        )
        return
    for label in labels:
        if label.class_id >= len(names):
            raise ValueError(f"review label class {label.class_id} is not valid for {names}")
        left, top, right, bottom = label.pixels(image.width, image.height)
        color = COLORS[label.class_id]
        draw.rectangle((left, top, right, bottom), outline=color, width=line_width)
        name = names[label.class_id]
        text_box = draw.textbbox((left, top), name, font=font, stroke_width=1)
        text_top = max(0, top - (text_box[3] - text_box[1]) - line_width * 2)
        draw.rectangle((left, text_top, text_box[2] + line_width * 2, top), fill=(0, 0, 0))
        draw.text((left + line_width, text_top), name, fill=color, font=font, stroke_width=1)


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source-root", type=Path, default=Path("output"))
    parser.add_argument("--output", type=Path, default=Path("output/review"))
    parser.add_argument("--max-size", type=int, default=1280)
    args = parser.parse_args(argv)
    for task in ("locator", "minigame"):
        try:
            count = build_review_images(
                args.source_root / task,
                args.output / task,
                task,
                args.max_size,
            )
        except ValueError as error:
            parser.error(str(error))
        print(f"{task}: images={count} output={args.output / task}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
