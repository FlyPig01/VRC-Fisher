"""Extract a recording and create full-screen YOLO pre-annotations for local review."""

from __future__ import annotations

import argparse
from dataclasses import asdict, dataclass
import hashlib
import json
from pathlib import Path
import shutil
import tempfile

import av
import numpy as np
from PIL import Image

from .video_review import Detection, UltralyticsDetector, map_box_to_full_frame, panel_crop_box


CLASS_NAMES = ("bite_indicator", "minigame_panel", "catch_zone", "moving_target")
YOLO_SCALE = 100_000_000


@dataclass(frozen=True, slots=True)
class Thresholds:
    bite_indicator: float = 0.15
    minigame_panel: float = 0.20
    catch_zone: float = 0.20
    moving_target: float = 0.05

    def validate(self) -> None:
        for name, value in asdict(self).items():
            if not 0 <= value <= 1:
                raise ValueError(f"{name} confidence must be between 0 and 1")


def _best(detections: list[Detection], class_id: int, confidence: float) -> Detection | None:
    candidates = [item for item in detections if item.class_id == class_id and item.confidence >= confidence]
    return max(candidates, key=lambda item: item.confidence, default=None)


def prelabel_frame(
    frame: np.ndarray,
    locator,
    minigame,
    thresholds: Thresholds,
    padding: float,
) -> list[tuple[str, tuple[float, float, float, float]]]:
    thresholds.validate()
    if not 0 <= padding <= 0.5:
        raise ValueError("padding must be between 0 and 0.5")
    height, width = frame.shape[:2]
    locator_detections = locator.detect(frame, min(thresholds.bite_indicator, thresholds.minigame_panel))
    bite = _best(locator_detections, 0, thresholds.bite_indicator)
    panel = _best(locator_detections, 1, thresholds.minigame_panel)
    result: list[tuple[str, tuple[float, float, float, float]]] = []
    if bite is not None:
        result.append((CLASS_NAMES[0], bite.box))
    if panel is None:
        return result
    result.append((CLASS_NAMES[1], panel.box))
    crop = panel_crop_box(panel, width, height, padding)
    if crop is None:
        return result
    left, top, right, bottom = crop
    local = frame[top:bottom, left:right]
    local_detections = minigame.detect(
        local, min(thresholds.catch_zone, thresholds.moving_target)
    )
    zone = _best(local_detections, 0, thresholds.catch_zone)
    target = _best(local_detections, 1, thresholds.moving_target)
    if zone is not None:
        result.append((CLASS_NAMES[2], map_box_to_full_frame(zone.box, crop)))
    if target is not None:
        result.append((CLASS_NAMES[3], map_box_to_full_frame(target.box, crop)))
    return result


def _quantized_yolo_axis(start: float, end: float, size: int) -> tuple[float, float]:
    span_ticks = max(1, min(YOLO_SCALE, round((end - start) * YOLO_SCALE / size)))
    center_ticks = round((start + end) * YOLO_SCALE / (2 * size))
    margin = (span_ticks + 1) // 2
    center_ticks = max(margin, min(YOLO_SCALE - margin, center_ticks))
    return center_ticks / YOLO_SCALE, span_ticks / YOLO_SCALE


def yolo_payload(
    labels: list[tuple[str, tuple[float, float, float, float]]], width: int, height: int
) -> list[tuple[int, float, float, float, float]]:
    output: list[tuple[int, float, float, float, float]] = []
    for name, box in labels:
        if name not in CLASS_NAMES:
            raise ValueError(f"unknown prelabel class: {name}")
        x1, y1, x2, y2 = box
        x1 = max(0.0, min(float(width), x1))
        y1 = max(0.0, min(float(height), y1))
        x2 = max(0.0, min(float(width), x2))
        y2 = max(0.0, min(float(height), y2))
        if x2 <= x1 or y2 <= y1:
            continue
        x_center, box_width = _quantized_yolo_axis(x1, x2, width)
        y_center, box_height = _quantized_yolo_axis(y1, y2, height)
        output.append(
            (
                CLASS_NAMES.index(name),
                x_center,
                y_center,
                box_width,
                box_height,
            )
        )
    return output


def write_yolo(path: Path, labels: list[tuple[int, float, float, float, float]]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(
        "".join(
            f"{class_id} {x:.8f} {y:.8f} {width:.8f} {height:.8f}\n"
            for class_id, x, y, width, height in labels
        ),
        encoding="ascii",
    )


def _sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def _install_directory(source: Path, destination: Path, replace: bool) -> None:
    if destination.exists():
        if not replace:
            raise FileExistsError(f"output already exists: {destination}")
        shutil.rmtree(destination)
    destination.parent.mkdir(parents=True, exist_ok=True)
    shutil.move(str(source), str(destination))


def create_local_annotation_batch(
    source: Path,
    locator_path: Path,
    minigame_path: Path,
    frames_root: Path,
    batches_root: Path,
    interval: float = 0.5,
    quality: int = 95,
    device: str = "0",
    thresholds: Thresholds = Thresholds(),
    padding: float = 0.08,
    max_frames: int | None = None,
    replace: bool = False,
) -> tuple[int, int, Path]:
    for required in (source, locator_path, minigame_path):
        if not required.is_file():
            raise FileNotFoundError(f"required input not found: {required}")
    if interval <= 0 or not 1 <= quality <= 100:
        raise ValueError("interval must be positive and quality must be between 1 and 100")
    if max_frames is not None and max_frames <= 0:
        raise ValueError("max_frames must be positive")
    thresholds.validate()
    recording = source.stem
    frames_destination = frames_root / recording
    batch_destination = batches_root / recording
    if not replace:
        for destination in (frames_destination, batch_destination):
            if destination.exists():
                raise FileExistsError(f"output already exists: {destination}")
    locator = UltralyticsDetector(locator_path, device, 960)
    minigame = UltralyticsDetector(minigame_path, device, 640)
    batches_root.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix=f".{recording}-prelabel-", dir=batches_root) as temporary:
        staging = Path(temporary)
        staged_frames = staging / "frames"
        batch = staging / "batch"
        prelabels = batch / "prelabels"
        labels_root = batch / "labels"
        staged_frames.mkdir(parents=True)
        prelabels.mkdir(parents=True)
        labels_root.mkdir(parents=True)
        next_timestamp = 0.0
        frame_records = []
        image_hashes: set[str] = set()
        boxes = 0
        with av.open(str(source)) as container:
            stream = container.streams.video[0]
            for frame_index, decoded in enumerate(container.decode(stream)):
                timestamp = (
                    float(decoded.time)
                    if decoded.time is not None
                    else frame_index / float(stream.average_rate)
                    if stream.average_rate
                    else None
                )
                if timestamp is None:
                    raise RuntimeError(f"cannot determine frame timestamps: {source}")
                if timestamp + 1e-9 < next_timestamp:
                    continue
                filename = f"frame-{frame_index:08d}.jpg"
                frame_path = staged_frames / filename
                rgb = decoded.to_ndarray(format="rgb24")
                Image.fromarray(rgb).save(frame_path, format="JPEG", quality=quality)
                width, height = decoded.width, decoded.height
                image_hash = _sha256(frame_path)
                if image_hash in image_hashes:
                    frame_path.unlink()
                    while next_timestamp <= timestamp + 1e-9:
                        next_timestamp += interval
                    continue
                image_hashes.add(image_hash)
                payload = yolo_payload(
                    prelabel_frame(rgb, locator, minigame, thresholds, padding),
                    width,
                    height,
                )
                write_yolo(prelabels / f"{Path(filename).stem}.txt", payload)
                frame_records.append(
                    {
                        "filename": filename,
                        "frame_index": frame_index,
                        "timestamp_seconds": round(timestamp, 6),
                        "width": width,
                        "height": height,
                        "sha256": image_hash,
                    }
                )
                boxes += len(payload)
                while next_timestamp <= timestamp + 1e-9:
                    next_timestamp += interval
                if max_frames is not None and len(frame_records) >= max_frames:
                    break
        if not frame_records:
            raise ValueError("recording produced no frames")
        mapping = {
            "schema_version": 2,
            "recording": recording,
            "source_video": source.name,
            "interval_seconds": interval,
            "classes": list(CLASS_NAMES),
            "thresholds": asdict(thresholds),
            "models": {
                "locator": {"path": locator_path.name, "sha256": _sha256(locator_path)},
                "minigame": {"path": minigame_path.name, "sha256": _sha256(minigame_path)},
            },
            "frames": frame_records,
        }
        (batch / "mapping.json").write_text(
            json.dumps(mapping, ensure_ascii=False, indent=2), encoding="utf-8"
        )
        (batch / "review.json").write_text(
            json.dumps(
                {"schema_version": 1, "recording": recording, "reviewed": []},
                ensure_ascii=False,
                indent=2,
            ),
            encoding="utf-8",
        )
        _install_directory(staged_frames, frames_destination, replace)
        _install_directory(batch, batch_destination, replace)
    return len(frame_records), boxes, batch_destination


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--input", type=Path, required=True)
    parser.add_argument("--locator", type=Path, required=True)
    parser.add_argument("--minigame", type=Path, required=True)
    parser.add_argument("--frames-root", type=Path, required=True)
    parser.add_argument("--batches-root", type=Path, required=True)
    parser.add_argument("--interval", type=float, default=0.5)
    parser.add_argument("--quality", type=int, default=95)
    parser.add_argument("--device", default="0")
    parser.add_argument("--bite-confidence", type=float, default=0.15)
    parser.add_argument("--panel-confidence", type=float, default=0.20)
    parser.add_argument("--zone-confidence", type=float, default=0.20)
    parser.add_argument("--target-confidence", type=float, default=0.05)
    parser.add_argument("--padding", type=float, default=0.08)
    parser.add_argument("--max-frames", type=int)
    parser.add_argument("--replace", action="store_true")
    args = parser.parse_args(argv)
    thresholds = Thresholds(
        args.bite_confidence,
        args.panel_confidence,
        args.zone_confidence,
        args.target_confidence,
    )
    try:
        frames, boxes, batch = create_local_annotation_batch(
            args.input,
            args.locator,
            args.minigame,
            args.frames_root,
            args.batches_root,
            args.interval,
            args.quality,
            args.device,
            thresholds,
            args.padding,
            args.max_frames,
            args.replace,
        )
    except (FileExistsError, FileNotFoundError, OSError, RuntimeError, ValueError) as error:
        parser.error(str(error))
    print(f"recording={args.input.stem} frames={frames} boxes={boxes} batch={batch}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
