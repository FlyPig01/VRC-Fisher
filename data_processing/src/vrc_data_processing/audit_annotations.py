"""Audit full-screen YOLO annotations before generating either dataset."""

from __future__ import annotations

import argparse
from dataclasses import dataclass
from pathlib import Path

from PIL import Image

from .labels import annotation_path, frame_files, read_labels, validate_frame_labels


@dataclass(frozen=True, slots=True)
class AuditReport:
    frames: int = 0
    positive_frames: int = 0
    negative_frames: int = 0
    unannotated_frames: int = 0
    errors: tuple[str, ...] = ()


def audit_annotations(frames: Path, annotations: Path) -> AuditReport:
    errors: list[str] = []
    frame_list = sorted(frame_files(frames))
    positive = 0
    negative = 0
    unannotated = 0
    for frame in frame_list:
        label_path = annotation_path(annotations, frame)
        if not label_path.is_file():
            unannotated += 1
            continue
        try:
            labels = read_labels(label_path)
            if labels:
                positive += 1
            else:
                negative += 1
            with Image.open(frame) as image:
                width, height = image.size
            errors.extend(f"{label_path}: {error}" for error in validate_frame_labels(labels))
            for label in labels:
                left, top, right, bottom = label.pixels(width, height)
                if left < -1 or top < -1 or right > width + 1 or bottom > height + 1:
                    errors.append(f"{label_path}: box outside image bounds")
        except (OSError, ValueError) as error:
            errors.append(f"{label_path}: {error}")
    return AuditReport(
        len(frame_list), positive, negative, unannotated, tuple(errors)
    )


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--frames", type=Path, default=Path("work/frames"))
    parser.add_argument("--annotations", type=Path, default=Path("input/annotations"))
    args = parser.parse_args(argv)
    report = audit_annotations(args.frames, args.annotations)
    print(
        f"frames={report.frames} positive={report.positive_frames} "
        f"negative={report.negative_frames} unannotated={report.unannotated_frames} "
        f"errors={len(report.errors)}"
    )
    for error in report.errors:
        print(f"ERROR {error}")
    return 1 if report.errors else 0


if __name__ == "__main__":
    raise SystemExit(main())
