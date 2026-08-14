"""Generate mini-game crops and labels from full-screen annotations."""

from __future__ import annotations

import argparse
import math
from pathlib import Path

from PIL import Image

from .build_locator_dataset import _write_yaml
from .generated_output import staged_output
from .labels import MINIGAME_CLASS_IDS, Label, annotation_path, frame_files, read_labels, write_labels


def build_minigame_dataset(
    frames: Path,
    annotations: Path,
    output: Path,
    padding: float = 0.0,
    negative_ratio: float = 0.2,
) -> tuple[int, int]:
    from .audit_annotations import audit_annotations

    audit = audit_annotations(frames, annotations)
    if audit.errors:
        raise ValueError(f"annotation audit failed with {len(audit.errors)} error(s)")
    with staged_output(output) as staging:
        crops_written, labels_written = _build_minigame_into(
            frames, annotations, staging, padding, negative_ratio
        )
    return crops_written, labels_written


def _build_minigame_into(
    frames: Path,
    annotations: Path,
    output: Path,
    padding: float,
    negative_ratio: float,
) -> tuple[int, int]:
    if not 0.0 <= padding <= 0.5:
        raise ValueError("padding must be between 0 and 0.5")
    if not 0.0 <= negative_ratio <= 1.0:
        raise ValueError("negative_ratio must be between 0 and 1")
    crops_written = 0
    labels_written = 0
    recordings = sorted({frame.parent for frame in frame_files(frames)})
    for recording in recordings:
        records: list[tuple[Path, list[Label]]] = []
        for frame in sorted(path for path in recording.iterdir() if path.suffix.casefold() in {".jpg", ".jpeg", ".png"}):
            source_label = annotation_path(annotations, frame)
            if source_label.is_file():
                records.append((frame, read_labels(source_label)))
        positives: list[tuple[int, Path, list[Label], Label]] = []
        negatives: list[tuple[int, Path]] = []
        for index, (frame, labels) in enumerate(records):
            groups = [label for label in labels if label.class_id == 1]
            if len(groups) > 1:
                raise ValueError(f"multiple minigame_panel labels are not supported: {frame}")
            if groups:
                positives.append((index, frame, labels, groups[0]))
            elif not labels:
                negatives.append((index, frame))

        for _, frame, labels, group in positives:
            written = _write_crop(frame, labels, group, output, padding)
            crops_written += 1
            labels_written += written

        negative_count = min(len(negatives), math.ceil(len(positives) * negative_ratio))
        for record_index, frame in _select_evenly(negatives, negative_count):
            nearest = min(positives, key=lambda item: abs(item[0] - record_index))
            _write_crop(frame, [], nearest[3], output, padding)
            crops_written += 1
    if crops_written == 0:
        raise ValueError("no positive minigame_panel frames found")
    _write_yaml(output, ("catch_zone", "moving_target"))
    return crops_written, labels_written


def _select_evenly(items: list[tuple[int, Path]], count: int) -> list[tuple[int, Path]]:
    if count <= 0:
        return []
    if count >= len(items):
        return items
    if count == 1:
        return [items[len(items) // 2]]
    indices = [round(index * (len(items) - 1) / (count - 1)) for index in range(count)]
    return [items[index] for index in indices]


def _write_crop(
    frame: Path,
    labels: list[Label],
    group: Label,
    output: Path,
    padding: float,
) -> int:
    with Image.open(frame) as image:
        crop_box = _crop_box(group, image.width, image.height, padding)
        crop_labels = _local_labels(labels, crop_box, image.width, image.height)
        destination_image = output / "images" / frame.parent.name / frame.name
        destination_label = output / "labels" / frame.parent.name / f"{frame.stem}.txt"
        destination_image.parent.mkdir(parents=True, exist_ok=True)
        image.crop(crop_box).save(destination_image, format="JPEG", quality=95)
        write_labels(destination_label, crop_labels)
    return len(crop_labels)


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
            label.class_id - 2,
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
    parser.add_argument("--padding", type=float, default=0.0)
    parser.add_argument("--negative-ratio", type=float, default=0.2)
    args = parser.parse_args(argv)
    from .audit_annotations import audit_annotations

    audit = audit_annotations(args.frames, args.annotations)
    if audit.errors:
        parser.error(f"annotation audit failed with {len(audit.errors)} error(s)")
    try:
        images, labels = build_minigame_dataset(
            args.frames,
            args.annotations,
            args.output,
            args.padding,
            args.negative_ratio,
        )
    except ValueError as error:
        parser.error(str(error))
    print(f"images={images} labels={labels} output={args.output}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
