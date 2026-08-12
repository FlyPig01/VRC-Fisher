"""Split a generated dataset by recording, never by adjacent frame."""

from __future__ import annotations

import argparse
import random
from pathlib import Path
import shutil


def split_by_recording(
    source: Path,
    output: Path,
    train_ratio: float = 0.8,
    val_ratio: float = 0.2,
    test_ratio: float = 0.0,
    seed: int = 42,
) -> dict[str, list[str]]:
    if abs(train_ratio + val_ratio + test_ratio - 1.0) > 1e-9:
        raise ValueError("train, val, and test ratios must sum to 1")
    if min(train_ratio, val_ratio, test_ratio) < 0:
        raise ValueError("split ratios cannot be negative")
    recordings = sorted(path.name for path in (source / "images").iterdir() if path.is_dir())
    required = sum(ratio > 0 for ratio in (train_ratio, val_ratio, test_ratio))
    if len(recordings) < required:
        raise ValueError(
            f"{len(recordings)} recording(s) cannot populate {required} non-empty splits; "
            "collect more recordings or set unused ratios to zero"
        )
    random.Random(seed).shuffle(recordings)
    counts = _split_counts(len(recordings), (train_ratio, val_ratio, test_ratio))
    assignments: dict[str, list[str]] = {}
    position = 0
    for split, count in zip(("train", "val", "test"), counts):
        assignments[split] = recordings[position : position + count]
        position += count
        for recording in assignments[split]:
            for image in sorted((source / "images" / recording).iterdir()):
                if not image.is_file():
                    continue
                label = source / "labels" / recording / f"{image.stem}.txt"
                destination_image = output / "images" / split / f"{recording}--{image.name}"
                destination_label = output / "labels" / split / f"{recording}--{image.stem}.txt"
                destination_image.parent.mkdir(parents=True, exist_ok=True)
                destination_label.parent.mkdir(parents=True, exist_ok=True)
                shutil.copy2(image, destination_image)
                shutil.copy2(label, destination_label)
    shutil.copy2(source / "data.yaml", output / "data.yaml")
    return assignments


def _split_counts(total: int, ratios: tuple[float, float, float]) -> tuple[int, int, int]:
    counts = [1 if ratio > 0 else 0 for ratio in ratios]
    remaining = total - sum(counts)
    while remaining:
        index = max(range(3), key=lambda item: ratios[item] * total - counts[item])
        counts[index] += 1
        remaining -= 1
    return counts[0], counts[1], counts[2]


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--input", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--train", type=float, default=0.8)
    parser.add_argument("--val", type=float, default=0.2)
    parser.add_argument("--test", type=float, default=0.0)
    parser.add_argument("--seed", type=int, default=42)
    args = parser.parse_args(argv)
    assignments = split_by_recording(
        args.input,
        args.output,
        args.train,
        args.val,
        args.test,
        args.seed,
    )
    print(" ".join(f"{name}={len(items)}" for name, items in assignments.items()))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
