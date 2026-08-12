"""Generate mini-game crops and labels from full-screen annotations."""

from __future__ import annotations

import argparse
from pathlib import Path

from PIL import Image

from .build_locator_dataset import _write_yaml
from .labels import MINIGAME_CLASS_IDS, Label, annotation_path, frame_files, read_labels, write_labels


def build_minigame_dataset(
    frames: Path,
    annotations: Path,
    output: Path,
    padding: float = 0.08,
) -> tuple[int, int]:
    if not 0.0 <= padding <= 0.5:
        raise ValueError("padding must be between 0 and 0.5")
    crops_written = 0
    labels_written = 0
    for frame in sorted(frame_files(frames)):
        source_label = annotation_path(annotations, frame)
        if not source_label.is_file():
            continue
        labels = read_labels(source_label)
        groups = [label for label in labels if label.class_id == 1]
        if len(groups) > 1:
            raise ValueError(f"multiple fishing_ui_group labels are not supported: {frame}")
        if not groups:
            continue
        with Image.open(frame) as image:
            crop_box = _crop_box(groups[0], image.width, image.height, padding)
            crop_labels = _local_labels(labels, crop_box, image.width, image.height)
            destination_image = output / "images" / frame.parent.name / frame.name
            destination_label = output / "labels" / frame.parent.name / f"{frame.stem}.txt"
            destination_image.parent.mkdir(parents=True, exist_ok=True)
            image.crop(crop_box).save(destination_image, format="JPEG", quality=95)
            write_labels(destination_label, crop_labels)
        crops_written += 1
        labels_written += len(crop_labels)
    _write_yaml(output, ("rail", "control_bar", "target", "progress_bar"))
    return crops_written, labels_written


def _crop_box(
    group: Label,
    image_width: int,
    image_height: int,
    padding: float,
) -> tuple[int, int, int, int]:
    left, top, right, bottom = group.pixels(image_width, image_height)
    pad_x = (right - left) * padding
    pad_y = (bottom - top) * padding
    return (
        max(0, int(round(left - pad_x))),
        max(0, int(round(top - pad_y))),
        min(image_width, int(round(right + pad_x))),
        min(image_height, int(round(bottom + pad_y))),
    )


def _local_labels(
    labels: list[Label],
    crop_box: tuple[int, int, int, int],
    image_width: int,
    image_height: int,
) -> list[Label]:
    crop_left, crop_top, crop_right, crop_bottom = crop_box
    crop_width = crop_right - crop_left
    crop_height = crop_bottom - crop_top
    local: list[Label] = []
    for label in labels:
        if label.class_id not in MINIGAME_CLASS_IDS:
            continue
        left, top, right, bottom = label.pixels(image_width, image_height)
        center_x = (left + right) / 2
        center_y = (top + bottom) / 2
        if not (crop_left <= center_x <= crop_right and crop_top <= center_y <= crop_bottom):
            continue
        clipped_left = max(crop_left, left)
        clipped_top = max(crop_top, top)
        clipped_right = min(crop_right, right)
        clipped_bottom = min(crop_bottom, bottom)
        local_label = Label(
            label.class_id - 4,
            ((clipped_left + clipped_right) / 2 - crop_left) / crop_width,
            ((clipped_top + clipped_bottom) / 2 - crop_top) / crop_height,
            (clipped_right - clipped_left) / crop_width,
            (clipped_bottom - clipped_top) / crop_height,
        )
        local_label.validate()
        local.append(local_label)
    return local


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--frames", type=Path, default=Path("work/frames"))
    parser.add_argument("--annotations", type=Path, default=Path("input/annotations"))
    parser.add_argument("--output", type=Path, default=Path("output/minigame"))
    parser.add_argument("--padding", type=float, default=0.08)
    args = parser.parse_args(argv)
    images, labels = build_minigame_dataset(
        args.frames,
        args.annotations,
        args.output,
        args.padding,
    )
    if images == 0:
        parser.error("no annotated fishing_ui_group frames found; add reviewed labels first")
    print(f"images={images} labels={labels} output={args.output}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
