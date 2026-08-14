from __future__ import annotations

import json
from pathlib import Path

from PIL import Image
import pytest

from vrc_data_processing.labels import Label, read_labels, write_labels
from vrc_data_processing.local_annotator import (
    STATIC_ROOT,
    AnnotationBatch,
    commit_reviewed_batch,
)


def _batch(tmp_path: Path, count: int = 2) -> tuple[AnnotationBatch, Path]:
    recording = "recording-a"
    frames_root = tmp_path / "frames"
    batches_root = tmp_path / "batches"
    frames = frames_root / recording
    batch = batches_root / recording
    prelabels = batch / "prelabels"
    labels = batch / "labels"
    frames.mkdir(parents=True)
    prelabels.mkdir(parents=True)
    labels.mkdir()
    records = []
    for index in range(count):
        filename = f"frame-{index:08d}.jpg"
        Image.new("RGB", (100, 80), "black").save(frames / filename)
        write_labels(
            prelabels / f"frame-{index:08d}.txt",
            [Label(0, 0.5, 0.5, 0.2, 0.4)] if index == 0 else [],
        )
        records.append({"filename": filename, "width": 100, "height": 80})
    (batch / "mapping.json").write_text(
        json.dumps(
            {"schema_version": 2, "recording": recording, "frames": records}
        ),
        encoding="utf-8",
    )
    (batch / "review.json").write_text(
        json.dumps({"schema_version": 1, "recording": recording, "reviewed": []}),
        encoding="utf-8",
    )
    return AnnotationBatch(recording, frames_root, batches_root), tmp_path / "annotations"


def test_batch_uses_prelabels_until_a_draft_is_saved(tmp_path: Path) -> None:
    batch, _ = _batch(tmp_path)

    assert batch.frame_payload("frame-00000000.jpg")["labels"][0]["class_id"] == 0
    result = batch.save_frame(
        "frame-00000000.jpg",
        [{"class_id": 0, "x_center": 0.4, "y_center": 0.4, "width": 0.1, "height": 0.2}],
        reviewed=True,
    )

    assert result["reviewed"] is True
    assert batch.summary() == {
        "recording": "recording-a",
        "classes": ["bite_indicator", "minigame_panel", "catch_zone", "moving_target"],
        "frames": ["frame-00000000.jpg", "frame-00000001.jpg"],
        "total": 2,
        "reviewed": 1,
        "remaining": 1,
        "positive": 1,
        "negative": 0,
    }


def test_empty_prelabel_is_not_a_negative_until_explicitly_reviewed(tmp_path: Path) -> None:
    batch, _ = _batch(tmp_path)

    assert batch.summary()["negative"] == 0
    batch.save_frame("frame-00000001.jpg", [], reviewed=True)

    assert batch.summary()["negative"] == 1


def test_review_rejects_incomplete_minigame_group(tmp_path: Path) -> None:
    batch, _ = _batch(tmp_path)

    with pytest.raises(ValueError, match="catch_zone"):
        batch.save_frame(
            "frame-00000000.jpg",
            [{"class_id": 1, "x_center": 0.5, "y_center": 0.5, "width": 0.8, "height": 0.8}],
            reviewed=True,
        )

    assert batch.summary()["reviewed"] == 0


def test_edge_box_is_quantized_without_leaving_image(tmp_path: Path) -> None:
    batch, _ = _batch(tmp_path)

    result = batch.save_frame(
        "frame-00000000.jpg",
        [
            {
                "class_id": 0,
                "x_center": 0.15,
                "y_center": 0.331122515,
                "width": 0.1,
                "height": 0.66224503,
            }
        ],
        reviewed=True,
    )

    label = result["labels"][0]
    assert label["y_center"] == 0.33112252
    assert label["y_center"] - label["height"] / 2 >= 0


def test_outside_box_is_rejected_before_saving_draft(tmp_path: Path) -> None:
    batch, _ = _batch(tmp_path)

    with pytest.raises(ValueError, match="outside image bounds"):
        batch.save_frame(
            "frame-00000000.jpg",
            [
                {
                    "class_id": 0,
                    "x_center": 0.5,
                    "y_center": 0.1,
                    "width": 0.2,
                    "height": 0.4,
                }
            ],
            reviewed=False,
        )


def test_reset_restores_original_prelabel_and_unreviews(tmp_path: Path) -> None:
    batch, _ = _batch(tmp_path)
    batch.save_frame("frame-00000000.jpg", [], reviewed=True)

    result = batch.reset_frame("frame-00000000.jpg")

    assert result["reviewed"] is False
    assert result["labels"][0]["class_id"] == 0


def test_commit_requires_every_frame_and_writes_project_yolo(tmp_path: Path) -> None:
    batch, annotations = _batch(tmp_path)
    batch.save_frame("frame-00000000.jpg", [
        {"class_id": 0, "x_center": 0.4, "y_center": 0.4, "width": 0.1, "height": 0.2}
    ], reviewed=True)

    with pytest.raises(ValueError, match="1 frames"):
        commit_reviewed_batch(batch, annotations)
    assert not (annotations / "recording-a").exists()

    batch.save_frame("frame-00000001.jpg", [], reviewed=True)
    assert commit_reviewed_batch(batch, annotations) == (2, 1)
    assert read_labels(annotations / "recording-a/frame-00000000.txt") == [
        Label(0, 0.4, 0.4, 0.1, 0.2)
    ]
    assert (annotations / "recording-a/frame-00000001.txt").read_text() == ""


def test_commit_does_not_overwrite_existing_annotations(tmp_path: Path) -> None:
    batch, annotations = _batch(tmp_path, count=1)
    batch.save_frame("frame-00000000.jpg", [], reviewed=True)
    existing = annotations / "recording-a/frame-00000000.txt"
    existing.parent.mkdir(parents=True)
    existing.write_text("existing", encoding="ascii")

    with pytest.raises(FileExistsError):
        commit_reviewed_batch(batch, annotations)

    assert existing.read_text(encoding="ascii") == "existing"


def test_annotator_web_has_precise_box_selection_and_frame_scrubber() -> None:
    html = (STATIC_ROOT / "index.html").read_text(encoding="utf-8")
    javascript = (STATIC_ROOT / "app.js").read_text(encoding="utf-8")
    css = (STATIC_ROOT / "styles.css").read_text(encoding="utf-8")

    assert 'id="boxList"' in html
    assert 'id="frameSlider" type="range"' in html
    assert "function renderBoxList()" in javascript
    assert '$("frameSlider").addEventListener("change"' in javascript
    assert "box-label" not in javascript
    assert "pointer-events: stroke" in css
    assert ".box-node.selected" in css
