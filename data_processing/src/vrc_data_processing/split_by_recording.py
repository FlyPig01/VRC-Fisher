"""Split a generated dataset by image with deterministic stratification.

The application-oriented dataset split intentionally allows frames from the
same recording to appear in both partitions. This keeps long recordings from
consuming the whole validation set and lets new feedback frames contribute to
training. A separate test video remains the place for qualitative review.
"""

from __future__ import annotations

import argparse
import json
import random
from pathlib import Path
import shutil

from .generated_output import staged_output
from .labels import read_labels


def split_by_image(
    source: Path,
    output: Path,
    train_ratio: float = 0.9,
    val_ratio: float = 0.1,
    seed: int = 42,
) -> dict[str, list[str]]:
    if abs(train_ratio + val_ratio - 1.0) > 1e-9:
        raise ValueError("train and val ratios must sum to 1")
    if min(train_ratio, val_ratio) <= 0:
        raise ValueError("train and val ratios must be positive")

    records: list[tuple[str, Path, str]] = []
    image_root = source / "images"
    label_root = source / "labels"
    if not image_root.is_dir():
        raise ValueError(f"generated dataset has no images directory: {source}")
    for recording in sorted(path for path in image_root.iterdir() if path.is_dir()):
        for image in sorted(path for path in recording.iterdir() if path.is_file()):
            label = label_root / recording.name / f"{image.stem}.txt"
            if not label.is_file():
                raise ValueError(f"generated image has no label: {image}")
            labels = read_labels(label)
            signature = ",".join(
                str(item.class_id)
                for item in sorted(labels, key=lambda item: item.class_id)
            )
            records.append(
                (f"{recording.name}--{image.name}", image, signature or "negative")
            )

    if len(records) < 2:
        raise ValueError("at least two generated images are required")

    rng = random.Random(seed)
    strata: dict[str, list[tuple[str, Path, str]]] = {}
    for record in records:
        strata.setdefault(record[2], []).append(record)

    train_records: list[tuple[str, Path, str]] = []
    val_records: list[tuple[str, Path, str]] = []
    for group in strata.values():
        rng.shuffle(group)
        if len(group) == 1:
            val_count = 0
        else:
            val_count = max(1, min(len(group) - 1, round(len(group) * val_ratio)))
        val_records.extend(group[:val_count])
        train_records.extend(group[val_count:])

    rng.shuffle(train_records)
    rng.shuffle(val_records)
    assignments = {
        "train": [record[0] for record in train_records],
        "val": [record[0] for record in val_records],
    }
    with staged_output(output) as staging:
        for split in ("train", "val"):
            (staging / "images" / split).mkdir(parents=True, exist_ok=True)
            (staging / "labels" / split).mkdir(parents=True, exist_ok=True)
        for split, split_records in (("train", train_records), ("val", val_records)):
            for sample_id, image, _ in split_records:
                label = label_root / image.parent.name / f"{image.stem}.txt"
                shutil.copy2(image, staging / "images" / split / sample_id)
                shutil.copy2(
                    label,
                    staging / "labels" / split / f"{Path(sample_id).stem}.txt",
                )
        shutil.copy2(source / "data.yaml", staging / "data.yaml")
        (staging / "split.json").write_text(
            json.dumps(assignments, ensure_ascii=False, indent=2) + "\n",
            encoding="utf-8",
        )
    return assignments


def split_by_recording(
    source: Path,
    output: Path,
    train_ratio: float = 0.9,
    val_ratio: float = 0.1,
    seed: int = 42,
) -> dict[str, list[str]]:
    """Compatibility alias; the implementation now splits by image."""

    return split_by_image(source, output, train_ratio, val_ratio, seed)


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--input", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--train", type=float, default=0.9)
    parser.add_argument("--val", type=float, default=0.1)
    parser.add_argument("--seed", type=int, default=42)
    args = parser.parse_args(argv)
    try:
        assignments = split_by_image(
            args.input,
            args.output,
            args.train,
            args.val,
            args.seed,
        )
    except (OSError, ValueError) as error:
        parser.error(str(error))
    print(" ".join(f"{name}={len(items)}" for name, items in assignments.items()))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
