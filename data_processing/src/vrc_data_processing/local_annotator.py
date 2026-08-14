"""Serve the local YOLO annotation editor and commit fully reviewed batches."""

from __future__ import annotations

import argparse
from dataclasses import asdict
from functools import partial
from http import HTTPStatus
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
import json
import mimetypes
from pathlib import Path
import tempfile
from threading import RLock
from urllib.parse import unquote, urlparse
import webbrowser

from PIL import Image

from .generated_output import _sync_tree
from .labels import (
    ALL_CLASSES,
    BOUNDARY_TOLERANCE,
    Label,
    quantize_label,
    read_labels,
    validate_frame_labels,
    write_labels,
)


STATIC_ROOT = Path(__file__).with_name("annotator_web")
IMAGE_SUFFIXES = {".jpg", ".jpeg", ".png"}


class AnnotationBatch:
    def __init__(self, recording: str, frames_root: Path, batches_root: Path):
        if Path(recording).name != recording or recording in {"", ".", ".."}:
            raise ValueError("recording must be a directory name without path separators")
        self.recording = recording
        self.frames = (frames_root / recording).resolve()
        self.batch = (batches_root / recording).resolve()
        self.prelabels = self.batch / "prelabels"
        self.labels = self.batch / "labels"
        self.review_path = self.batch / "review.json"
        self.mapping_path = self.batch / "mapping.json"
        if not self.frames.is_dir() or not self.batch.is_dir():
            raise FileNotFoundError(f"local annotation batch not found for {recording}")
        self.mapping = json.loads(self.mapping_path.read_text(encoding="utf-8"))
        if self.mapping.get("schema_version") != 2 or self.mapping.get("recording") != recording:
            raise ValueError("mapping.json does not describe a local annotation batch")
        records = self.mapping.get("frames")
        if not isinstance(records, list) or not records:
            raise ValueError("mapping.json contains no frames")
        self.records = {item["filename"]: item for item in records}
        if len(self.records) != len(records):
            raise ValueError("mapping.json contains duplicate frame names")
        for filename in self.records:
            if Path(filename).name != filename or Path(filename).suffix.casefold() not in IMAGE_SUFFIXES:
                raise ValueError(f"invalid frame filename: {filename}")
            if not (self.frames / filename).is_file():
                raise FileNotFoundError(f"frame is missing: {filename}")
            if not (self.prelabels / f"{Path(filename).stem}.txt").is_file():
                raise FileNotFoundError(f"prelabel is missing: {filename}")
        self.labels.mkdir(parents=True, exist_ok=True)
        self.lock = RLock()
        self.reviewed = self._load_reviewed()

    def _load_reviewed(self) -> set[str]:
        payload = json.loads(self.review_path.read_text(encoding="utf-8"))
        if payload.get("schema_version") != 1 or payload.get("recording") != self.recording:
            raise ValueError("review.json does not describe this recording")
        reviewed = payload.get("reviewed")
        if not isinstance(reviewed, list) or any(name not in self.records for name in reviewed):
            raise ValueError("review.json contains invalid frame names")
        for filename in reviewed:
            if not (self.labels / f"{Path(filename).stem}.txt").is_file():
                raise ValueError(f"reviewed frame has no human draft: {filename}")
        return set(reviewed)

    def _save_reviewed(self) -> None:
        payload = {
            "schema_version": 1,
            "recording": self.recording,
            "reviewed": [name for name in self.records if name in self.reviewed],
        }
        temporary = self.review_path.with_suffix(".tmp")
        temporary.write_text(json.dumps(payload, ensure_ascii=False, indent=2), encoding="utf-8")
        temporary.replace(self.review_path)

    def frame_names(self) -> list[str]:
        return list(self.records)

    def _label_path(self, filename: str) -> Path:
        if filename not in self.records:
            raise FileNotFoundError("unknown frame")
        name = f"{Path(filename).stem}.txt"
        draft = self.labels / name
        return draft if draft.is_file() else self.prelabels / name

    def frame_payload(self, filename: str) -> dict:
        record = self.records.get(filename)
        if record is None:
            raise FileNotFoundError("unknown frame")
        labels = read_labels(self._label_path(filename))
        return {
            "filename": filename,
            "width": record["width"],
            "height": record["height"],
            "reviewed": filename in self.reviewed,
            "labels": [asdict(label) for label in labels],
            "errors": validate_frame_labels(labels),
        }

    def save_frame(self, filename: str, raw_labels: object, reviewed: bool) -> dict:
        with self.lock:
            if filename not in self.records:
                raise FileNotFoundError("unknown frame")
            if not isinstance(raw_labels, list):
                raise ValueError("labels must be a list")
            labels: list[Label] = []
            for item in raw_labels:
                if not isinstance(item, dict):
                    raise ValueError("each label must be an object")
                try:
                    label = Label(
                        int(item["class_id"]),
                        float(item["x_center"]),
                        float(item["y_center"]),
                        float(item["width"]),
                        float(item["height"]),
                    )
                except (KeyError, TypeError, ValueError) as error:
                    raise ValueError("invalid YOLO label fields") from error
                label.validate()
                if (
                    label.x_center - label.width / 2 < -BOUNDARY_TOLERANCE
                    or label.y_center - label.height / 2 < -BOUNDARY_TOLERANCE
                    or label.x_center + label.width / 2 > 1 + BOUNDARY_TOLERANCE
                    or label.y_center + label.height / 2 > 1 + BOUNDARY_TOLERANCE
                ):
                    raise ValueError("box is outside image bounds")
                labels.append(quantize_label(label))
            errors = validate_frame_labels(labels)
            if reviewed and errors:
                raise ValueError("; ".join(errors))
            write_labels(self.labels / f"{Path(filename).stem}.txt", labels)
            if reviewed:
                self.reviewed.add(filename)
            else:
                self.reviewed.discard(filename)
            self._save_reviewed()
            return self.frame_payload(filename)

    def reset_frame(self, filename: str) -> dict:
        with self.lock:
            if filename not in self.records:
                raise FileNotFoundError("unknown frame")
            draft = self.labels / f"{Path(filename).stem}.txt"
            if draft.exists():
                draft.unlink()
            self.reviewed.discard(filename)
            self._save_reviewed()
            return self.frame_payload(filename)

    def image_path(self, filename: str) -> Path:
        if filename not in self.records:
            raise FileNotFoundError("unknown frame")
        return self.frames / filename

    def summary(self) -> dict:
        with self.lock:
            names = self.frame_names()
            reviewed_labels = [
                read_labels(self._label_path(name))
                for name in names
                if name in self.reviewed
            ]
            return {
                "recording": self.recording,
                "classes": list(ALL_CLASSES),
                "frames": names,
                "total": len(names),
                "reviewed": len(self.reviewed),
                "remaining": len(names) - len(self.reviewed),
                "positive": sum(bool(labels) for labels in reviewed_labels),
                "negative": sum(not labels for labels in reviewed_labels),
            }


def commit_reviewed_batch(
    batch: AnnotationBatch, annotations_root: Path, replace: bool = False
) -> tuple[int, int]:
    names = batch.frame_names()
    missing = [name for name in names if name not in batch.reviewed]
    if missing:
        raise ValueError(f"{len(missing)} frames have not been explicitly reviewed")
    destination = annotations_root / batch.recording
    if destination.exists() and any(destination.glob("*.txt")) and not replace:
        raise FileExistsError(f"annotations already exist for {batch.recording}")
    annotations_root.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix="vrc-commit-", dir=annotations_root.parent) as temporary:
        staging = Path(temporary) / batch.recording
        boxes = 0
        for filename in names:
            draft = batch.labels / f"{Path(filename).stem}.txt"
            if not draft.is_file():
                raise ValueError(f"{filename}: reviewed frame has no human draft")
            labels = read_labels(batch._label_path(filename))
            errors = validate_frame_labels(labels)
            if errors:
                raise ValueError(f"{filename}: {'; '.join(errors)}")
            write_labels(staging / f"{Path(filename).stem}.txt", labels)
            boxes += len(labels)
        _sync_tree(staging, destination)
    return len(names), boxes


class AnnotatorHandler(BaseHTTPRequestHandler):
    server_version = "VrcFisherAnnotator/1"

    @property
    def batch(self) -> AnnotationBatch:
        return self.server.batch  # type: ignore[attr-defined]

    def _json(self, status: int, payload: object) -> None:
        body = json.dumps(payload, ensure_ascii=False).encode("utf-8")
        self.send_response(status)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Content-Length", str(len(body)))
        self.send_header("Cache-Control", "no-store")
        self.end_headers()
        self.wfile.write(body)

    def _file(self, path: Path, content_type: str | None = None) -> None:
        if not path.is_file():
            self.send_error(HTTPStatus.NOT_FOUND)
            return
        body = path.read_bytes()
        self.send_response(HTTPStatus.OK)
        self.send_header("Content-Type", content_type or mimetypes.guess_type(path.name)[0] or "application/octet-stream")
        self.send_header("Content-Length", str(len(body)))
        self.send_header("Cache-Control", "no-store")
        self.end_headers()
        self.wfile.write(body)

    def do_GET(self) -> None:
        path = urlparse(self.path).path
        try:
            if path == "/api/summary":
                self._json(HTTPStatus.OK, self.batch.summary())
            elif path.startswith("/api/frame/"):
                self._json(HTTPStatus.OK, self.batch.frame_payload(unquote(path.removeprefix("/api/frame/"))))
            elif path.startswith("/frames/"):
                self._file(self.batch.image_path(unquote(path.removeprefix("/frames/"))))
            elif path in {"/", "/index.html"}:
                self._file(STATIC_ROOT / "index.html", "text/html; charset=utf-8")
            elif path in {"/app.js", "/styles.css"}:
                self._file(STATIC_ROOT / path.removeprefix("/"))
            else:
                self.send_error(HTTPStatus.NOT_FOUND)
        except (FileNotFoundError, OSError, ValueError) as error:
            self._json(HTTPStatus.BAD_REQUEST, {"error": str(error)})

    def do_PUT(self) -> None:
        path = urlparse(self.path).path
        if not path.startswith("/api/frame/"):
            self.send_error(HTTPStatus.NOT_FOUND)
            return
        try:
            length = int(self.headers.get("Content-Length", "0"))
            if length <= 0 or length > 1_000_000:
                raise ValueError("invalid request size")
            payload = json.loads(self.rfile.read(length).decode("utf-8"))
            result = self.batch.save_frame(
                unquote(path.removeprefix("/api/frame/")),
                payload.get("labels"),
                bool(payload.get("reviewed", False)),
            )
            self._json(HTTPStatus.OK, result)
        except (FileNotFoundError, json.JSONDecodeError, OSError, ValueError) as error:
            self._json(HTTPStatus.BAD_REQUEST, {"error": str(error)})

    def do_DELETE(self) -> None:
        path = urlparse(self.path).path
        if not path.startswith("/api/frame/"):
            self.send_error(HTTPStatus.NOT_FOUND)
            return
        try:
            result = self.batch.reset_frame(unquote(path.removeprefix("/api/frame/")))
            self._json(HTTPStatus.OK, result)
        except (FileNotFoundError, OSError, ValueError) as error:
            self._json(HTTPStatus.BAD_REQUEST, {"error": str(error)})

    def log_message(self, format: str, *args) -> None:
        return


def serve(batch: AnnotationBatch, host: str, port: int, open_browser: bool) -> None:
    if host not in {"127.0.0.1", "localhost"}:
        raise ValueError("annotator may only listen on localhost")
    server = ThreadingHTTPServer((host, port), partial(AnnotatorHandler))
    server.batch = batch  # type: ignore[attr-defined]
    url = f"http://{host}:{server.server_port}/"
    print(f"annotator={url} recording={batch.recording}")
    if open_browser:
        webbrowser.open(url)
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        pass
    finally:
        server.server_close()


def serve_main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--recording", required=True)
    parser.add_argument("--frames-root", type=Path, default=Path("work/frames"))
    parser.add_argument("--batches-root", type=Path, default=Path("work/annotations"))
    parser.add_argument("--host", default="127.0.0.1")
    parser.add_argument("--port", type=int, default=8765)
    parser.add_argument("--no-browser", action="store_true")
    args = parser.parse_args(argv)
    try:
        batch = AnnotationBatch(args.recording, args.frames_root, args.batches_root)
        serve(batch, args.host, args.port, not args.no_browser)
    except (FileExistsError, FileNotFoundError, OSError, ValueError) as error:
        parser.error(str(error))
    return 0


def commit_main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--recording", required=True)
    parser.add_argument("--frames-root", type=Path, default=Path("work/frames"))
    parser.add_argument("--batches-root", type=Path, default=Path("work/annotations"))
    parser.add_argument("--annotations-root", type=Path, default=Path("input/annotations"))
    parser.add_argument("--replace", action="store_true")
    args = parser.parse_args(argv)
    try:
        batch = AnnotationBatch(args.recording, args.frames_root, args.batches_root)
        frames, boxes = commit_reviewed_batch(batch, args.annotations_root, args.replace)
        print(
            f"recording={args.recording} frames={frames} boxes={boxes} "
            f"output={args.annotations_root / args.recording}"
        )
    except (FileExistsError, FileNotFoundError, OSError, ValueError) as error:
        parser.error(str(error))
    return 0


if __name__ == "__main__":
    raise SystemExit(serve_main())
