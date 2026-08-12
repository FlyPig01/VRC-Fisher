"""Extract complete-screen frames at a fixed time interval."""

from __future__ import annotations

import argparse
import json
from pathlib import Path

import av
from PIL import Image


def extract_recording(
    source: Path,
    output_root: Path,
    interval_seconds: float,
    jpeg_quality: int = 95,
) -> list[dict[str, object]]:
    if interval_seconds <= 0:
        raise ValueError("interval_seconds must be positive")
    destination = output_root / source.stem
    destination.mkdir(parents=True, exist_ok=True)
    rows: list[dict[str, object]] = []
    next_timestamp = 0.0
    with av.open(str(source)) as container:
        stream = container.streams.video[0]
        for frame_index, frame in enumerate(container.decode(stream)):
            if frame.time is not None:
                timestamp = float(frame.time)
            elif stream.average_rate:
                timestamp = frame_index / float(stream.average_rate)
            else:
                raise RuntimeError(f"cannot determine frame timestamps: {source}")
            if timestamp + 1e-9 < next_timestamp:
                continue
            filename = f"frame-{frame_index:08d}.jpg"
            path = destination / filename
            Image.fromarray(frame.to_ndarray(format="rgb24")).save(
                path,
                format="JPEG",
                quality=jpeg_quality,
            )
            rows.append(
                {
                    "recording": source.name,
                    "recording_id": source.stem,
                    "frame_index": frame_index,
                    "timestamp_seconds": round(timestamp, 6),
                    "image": str(path.relative_to(output_root)).replace("\\", "/"),
                }
            )
            while next_timestamp <= timestamp + 1e-9:
                next_timestamp += interval_seconds
    return rows


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--input", type=Path, default=Path("input/recordings"))
    parser.add_argument("--output", type=Path, default=Path("work/frames"))
    parser.add_argument("--interval", type=float, default=0.25, help="seconds between frames")
    parser.add_argument("--quality", type=int, default=95)
    args = parser.parse_args(argv)
    if not 1 <= args.quality <= 100:
        parser.error("--quality must be between 1 and 100")
    recordings = sorted(
        path
        for path in args.input.iterdir()
        if path.is_file() and path.suffix.casefold() in {".mp4", ".mkv", ".mov", ".avi"}
    )
    if not recordings:
        parser.error(f"no recordings found in {args.input}")
    rows: list[dict[str, object]] = []
    for recording in recordings:
        rows.extend(extract_recording(recording, args.output, args.interval, args.quality))
    manifest = args.output / "manifest.jsonl"
    manifest.write_text(
        "".join(json.dumps(row, ensure_ascii=True) + "\n" for row in rows),
        encoding="utf-8",
    )
    print(f"recordings={len(recordings)} frames={len(rows)} manifest={manifest}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
