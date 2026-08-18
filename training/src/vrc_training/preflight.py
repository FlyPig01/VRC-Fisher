"""Validate reviewed YOLO datasets without loading a model or starting training."""

from __future__ import annotations

import argparse
from dataclasses import dataclass
import json
from pathlib import Path
import re

from PIL import Image

from .config import TaskConfig, load_train_config


EXPECTED_CLASSES = {
    "locator": ("bite_indicator", "minigame_panel"),
    "minigame": ("catch_zone", "moving_target"),
}
IMAGE_SUFFIXES = {".jpg", ".jpeg", ".png"}


@dataclass(frozen=True, slots=True)
class DatasetReport:
    task: str
    recordings: int
    images: int
    positives: int
    negatives: int
    boxes: tuple[int, int]


def preflight_task(name: str, task: TaskConfig, root: Path) -> DatasetReport:
    data = (root / task.data).resolve()
    if not data.is_file():
        raise ValueError(f"{name}: dataset config not found: {data}")
    fields, names = _read_dataset_yaml(data)
    if tuple(names) != EXPECTED_CLASSES[name]:
        raise ValueError(
            f"{name}: expected classes {EXPECTED_CLASSES[name]}, got {tuple(names)}"
        )
    dataset_root = (data.parent / fields.get("path", ".")).resolve()
    split_path = dataset_root / "split.json"
    if not split_path.is_file():
        raise ValueError(f"{name}: split.json not found; use vrc-split-dataset")
    try:
        assignments = json.loads(split_path.read_text(encoding="utf-8"))
    except (json.JSONDecodeError, OSError) as error:
        raise ValueError(f"{name}: invalid split.json") from error
    _validate_assignments(name, assignments)

    total_images = 0
    total_positive = 0
    total_negative = 0
    total_boxes = [0, 0]
    for split in ("train", "val"):
        image_value = fields.get(split)
        if not image_value:
            raise ValueError(f"{name}: {split} path missing from data.yaml")
        image_dir = dataset_root / image_value
        label_dir = dataset_root / "labels" / split
        images = sorted(
            path for path in image_dir.glob("*")
            if path.is_file() and path.suffix.casefold() in IMAGE_SUFFIXES
        ) if image_dir.is_dir() else []
        labels = sorted(label_dir.glob("*.txt")) if label_dir.is_dir() else []
        image_keys = {path.stem for path in images}
        label_keys = {path.stem for path in labels}
        if not images:
            raise ValueError(f"{name}: {split} has no images")
        if image_keys != label_keys:
            raise ValueError(
                f"{name}: {split} image/label mismatch "
                f"missing_labels={len(image_keys - label_keys)} "
                f"orphan_labels={len(label_keys - image_keys)}"
            )
        allowed_samples = set(assignments[split])
        observed_samples = {path.name for path in images}
        if observed_samples != allowed_samples:
            raise ValueError(
                f"{name}: {split} samples do not match split.json: "
                f"expected={len(allowed_samples)} observed={len(observed_samples)}"
            )

        split_boxes = [0, 0]
        split_positive = 0
        split_negative = 0
        for image in images:
            try:
                with Image.open(image) as opened:
                    opened.verify()
            except (OSError, ValueError) as error:
                raise ValueError(f"{name}: unreadable image: {image}") from error
            label = label_dir / f"{image.stem}.txt"
            counts = _read_yolo_label(label)
            if name == "minigame" and counts not in {(0, 0), (1, 1)}:
                raise ValueError(
                    f"{name}: each image must be empty or contain one catch_zone and one moving_target: "
                    f"{label} has {counts}"
                )
            if name == "locator" and any(count > 1 for count in counts):
                raise ValueError(f"{name}: duplicate class box in {label}: {counts}")
            if sum(counts):
                split_positive += 1
            else:
                split_negative += 1
            split_boxes[0] += counts[0]
            split_boxes[1] += counts[1]
        if any(count == 0 for count in split_boxes):
            raise ValueError(
                f"{name}: {split} must contain both classes; boxes={tuple(split_boxes)}"
            )
        total_images += len(images)
        total_positive += split_positive
        total_negative += split_negative
        total_boxes[0] += split_boxes[0]
        total_boxes[1] += split_boxes[1]

    recordings = {
        _recording_from_sample_name(sample)
        for sample in assignments["train"] + assignments["val"]
    }
    return DatasetReport(
        name,
        len(recordings),
        total_images,
        total_positive,
        total_negative,
        (total_boxes[0], total_boxes[1]),
    )


def _read_dataset_yaml(path: Path) -> tuple[dict[str, str], list[str]]:
    fields: dict[str, str] = {}
    names: dict[int, str] = {}
    in_names = False
    for raw in path.read_text(encoding="utf-8").splitlines():
        line = raw.split("#", 1)[0].rstrip()
        if not line.strip():
            continue
        if not line.startswith((" ", "\t")):
            key, separator, value = line.partition(":")
            if not separator:
                raise ValueError(f"invalid data.yaml line: {raw!r}")
            in_names = key.strip() == "names"
            if not in_names:
                fields[key.strip()] = value.strip().strip("'\"")
            continue
        if in_names:
            match = re.fullmatch(r"\s*(\d+)\s*:\s*(.+?)\s*", line)
            if not match:
                raise ValueError(f"invalid class name line: {raw!r}")
            names[int(match.group(1))] = match.group(2).strip("'\"")
    ordered = [names[index] for index in sorted(names)]
    if sorted(names) != list(range(len(names))):
        raise ValueError("data.yaml class ids must be contiguous from zero")
    return fields, ordered


def _validate_assignments(name: str, assignments: object) -> None:
    if not isinstance(assignments, dict):
        raise ValueError(f"{name}: split.json must contain an object")
    for split in ("train", "val"):
        values = assignments.get(split)
        if not isinstance(values, list) or not all(isinstance(value, str) for value in values):
            raise ValueError(f"{name}: split.json {split} must be a string list")
    if not assignments["train"] or not assignments["val"]:
        raise ValueError(f"{name}: train and val must each contain samples")
    if set(assignments) != {"train", "val"}:
        raise ValueError(f"{name}: split.json must contain only train and val")
    all_samples = assignments["train"] + assignments["val"]
    if len(all_samples) != len(set(all_samples)):
        raise ValueError(f"{name}: a sample appears in multiple splits")


def _recording_from_sample_name(filename: str) -> str:
    recording, separator, _ = filename.partition("--")
    if not separator or not recording:
        raise ValueError(f"invalid generated sample name: {filename}")
    return recording


def _read_yolo_label(path: Path) -> tuple[int, int]:
    counts = [0, 0]
    for line_number, raw in enumerate(path.read_text(encoding="utf-8").splitlines(), 1):
        if not raw.strip():
            continue
        parts = raw.split()
        if len(parts) != 5:
            raise ValueError(f"{path}:{line_number}: expected 5 YOLO fields")
        try:
            class_id = int(parts[0])
            values = [float(value) for value in parts[1:]]
        except ValueError as error:
            raise ValueError(f"{path}:{line_number}: invalid YOLO label") from error
        if class_id not in (0, 1):
            raise ValueError(f"{path}:{line_number}: class id must be 0 or 1")
        x_center, y_center, width, height = values
        if (
            not all(0.0 <= value <= 1.0 for value in values)
            or width <= 0
            or height <= 0
            or x_center - width / 2 < -1e-6
            or y_center - height / 2 < -1e-6
            or x_center + width / 2 > 1 + 1e-6
            or y_center + height / 2 > 1 + 1e-6
        ):
            raise ValueError(f"{path}:{line_number}: invalid normalized box")
        counts[class_id] += 1
    return counts[0], counts[1]


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--config", type=Path, default=Path("configs/default.toml"))
    parser.add_argument("--task", choices=("locator", "minigame", "all"), default="all")
    args = parser.parse_args(argv)
    root = Path.cwd()
    config = load_train_config(args.config)
    names = ("locator", "minigame") if args.task == "all" else (args.task,)
    try:
        reports = [preflight_task(name, getattr(config, name), root) for name in names]
    except ValueError as error:
        parser.error(str(error))
    for report in reports:
        print(
            f"{report.task}: recordings={report.recordings} images={report.images} "
            f"positive={report.positives} negative={report.negatives} boxes={report.boxes}"
        )
    print("READY: dataset preflight passed; training was not started")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
