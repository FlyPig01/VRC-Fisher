"""Audit full-screen YOLO annotations before generating either dataset."""

from __future__ import annotations

import argparse
from dataclasses import dataclass
from pathlib import Path

from PIL import Image

from .labels import annotation_path, frame_files, read_labels


@dataclass(frozen=True, slots=True)
class AuditReport:
    frames: int = 0
    annotated_frames: int = 0
    unannotated_frames: int = 0
    errors: tuple[str, ...] = ()


def audit_annotations(frames: Path, annotations: Path) -> AuditReport:
    errors: list[str] = []
    frames_count = 0
    annotated = 0
    unannotated = 0
    for frame in sorted(frame_files(frames)):
        frames_count += 1
        label_path = annotation_path(annotations, frame)
        if not label_path.is_file():
            unannotated += 1
            continue
        annotated += 1
        try:
            labels = read_labels(label_path)
            with Image.open(frame) as image:
                width, height = image.size
            groups = [label for label in labels if label.class_id == 1]
            if len(groups) > 1:
                errors.append(f"{label_path}: multiple fishing_ui_group labels")
            if any(label.class_id >= 4 for label in labels) and not groups:
                errors.append(f"{label_path}: minigame labels without fishing_ui_group")
            for label in labels:
                left, top, right, bottom = label.pixels(width, height)
                if left < -1 or top < -1 or right > width + 1 or bottom > height + 1:
                    errors.append(f"{label_path}: box outside image bounds")
        except (OSError, ValueError) as error:
            errors.append(f"{label_path}: {error}")
    return AuditReport(frames_count, annotated, unannotated, tuple(errors))


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--frames", type=Path, default=Path("work/frames"))
    parser.add_argument("--annotations", type=Path, default=Path("input/annotations"))
    args = parser.parse_args(argv)
    report = audit_annotations(args.frames, args.annotations)
    print(
        f"frames={report.frames} annotated={report.annotated_frames} "
        f"unannotated={report.unannotated_frames} errors={len(report.errors)}"
    )
    for error in report.errors:
        print(f"ERROR {error}")
    return 1 if report.errors else 0


if __name__ == "__main__":
    raise SystemExit(main())
