from pathlib import Path
import json

from PIL import Image
import pytest

from vrc_data_processing.audit_annotations import audit_annotations
from vrc_data_processing.build_locator_dataset import build_locator_dataset
from vrc_data_processing.build_locator_dataset import main as build_locator_main
from vrc_data_processing.build_minigame_dataset import build_minigame_dataset
from vrc_data_processing.build_minigame_dataset import main as build_minigame_main
from vrc_data_processing.build_review_images import build_review_images
from vrc_data_processing.split_by_recording import main as split_main
from vrc_data_processing.split_by_recording import split_by_recording


def sample(tmp_path: Path, recording: str = "recording-a") -> tuple[Path, Path]:
    frames = tmp_path / "frames"
    annotations = tmp_path / "annotations"
    (frames / recording).mkdir(parents=True, exist_ok=True)
    (annotations / recording).mkdir(parents=True, exist_ok=True)
    image = f"{recording}/frame-1.jpg"
    Image.new("RGB", (100, 100), "black").save(frames / image)
    (annotations / Path(image).with_suffix(".txt")).write_text(
        "1 0.5 0.5 0.6 0.8\n"
        "2 0.5 0.6 0.1 0.2\n"
        "3 0.5 0.3 0.1 0.1\n",
        encoding="ascii",
    )
    return frames, annotations


def test_builds_locator_and_minigame_from_one_full_screen_label_set(tmp_path) -> None:
    frames, annotations = sample(tmp_path)
    locator = tmp_path / "locator"
    minigame = tmp_path / "minigame"

    assert build_locator_dataset(frames, annotations, locator) == (1, 1)
    assert build_minigame_dataset(frames, annotations, minigame, padding=0.0) == (1, 2)
    assert Image.open(minigame / "images/recording-a/frame-1.jpg").size == (60, 80)
    assert (locator / "labels/recording-a/frame-1.txt").read_text().startswith("1 ")
    assert "path:" not in (locator / "data.yaml").read_text(encoding="ascii")
    local_ids = [
        line.split()[0]
        for line in (minigame / "labels/recording-a/frame-1.txt").read_text().splitlines()
    ]
    assert local_ids == ["0", "1"]
    assert "path:" not in (minigame / "data.yaml").read_text(encoding="ascii")


def test_unannotated_frames_are_ignored_and_empty_txt_is_a_negative_sample(tmp_path) -> None:
    frames, annotations = sample(tmp_path)
    recording = frames / "recording-a"
    Image.new("RGB", (100, 100), "black").save(recording / "frame-2.jpg")
    Image.new("RGB", (100, 100), "black").save(recording / "frame-3.jpg")
    (annotations / "recording-a/frame-2.txt").write_text("", encoding="ascii")

    report = audit_annotations(frames, annotations)

    assert (report.positive_frames, report.negative_frames, report.unannotated_frames) == (1, 1, 1)
    assert build_locator_dataset(frames, annotations, tmp_path / "locator") == (2, 1)


def test_minigame_uses_local_crops_from_reviewed_locator_negatives(tmp_path) -> None:
    frames, annotations = sample(tmp_path)
    recording = frames / "recording-a"
    Image.new("RGB", (100, 100), "white").save(recording / "frame-2.jpg")
    (annotations / "recording-a/frame-2.txt").write_text("", encoding="ascii")
    output = tmp_path / "minigame"

    assert build_minigame_dataset(
        frames,
        annotations,
        output,
        padding=0.0,
        negative_ratio=1.0,
    ) == (2, 2)
    assert Image.open(output / "images/recording-a/frame-2.jpg").size == (60, 80)
    assert (output / "labels/recording-a/frame-2.txt").read_text() == ""


def test_label_without_cached_image_is_ignored(tmp_path) -> None:
    frames, annotations = sample(tmp_path)
    (frames / "recording-a/frame-1.jpg").unlink()

    report = audit_annotations(frames, annotations)

    assert report.frames == 0
    assert not report.errors
    output = tmp_path / "locator"
    with pytest.raises(ValueError, match="no annotated frames"):
        build_locator_dataset(frames, annotations, output)
    assert not (output / "data.yaml").exists()


def test_split_refuses_one_recording_for_train_and_validation(tmp_path) -> None:
    frames, annotations = sample(tmp_path)
    generated = tmp_path / "locator"
    build_locator_dataset(frames, annotations, generated)

    with pytest.raises(ValueError, match="collect more recordings"):
        split_by_recording(generated, tmp_path / "dataset")

    with pytest.raises(SystemExit):
        split_main(["--input", str(generated), "--output", str(tmp_path / "dataset")])


def test_split_keeps_each_recording_in_one_partition(tmp_path) -> None:
    frames, annotations = sample(tmp_path, "recording-a")
    sample(tmp_path, "recording-b")
    generated = tmp_path / "locator"
    build_locator_dataset(frames, annotations, generated)

    assignments = split_by_recording(generated, tmp_path / "dataset", 0.5, 0.5)
    assert len(assignments["train"]) == 1
    assert len(assignments["val"]) == 1
    assert set(assignments["train"]).isdisjoint(assignments["val"])
    assert json.loads((tmp_path / "dataset/split.json").read_text(encoding="utf-8")) == assignments
    assert set(assignments) == {"train", "val"}
    for split in ("train", "val"):
        assert (tmp_path / f"dataset/images/{split}").is_dir()
        assert (tmp_path / f"dataset/labels/{split}").is_dir()
    assert not (tmp_path / "dataset/images/test").exists()
    assert not (tmp_path / "dataset/labels/test").exists()


def test_rebuild_removes_stale_generated_files(tmp_path) -> None:
    frames, annotations = sample(tmp_path)
    output = tmp_path / "locator"
    build_locator_dataset(frames, annotations, output)
    stale = output / "images/stale.jpg"
    stale.write_bytes(b"stale")

    build_locator_dataset(frames, annotations, output)

    assert not stale.exists()


def test_builds_review_images_for_positive_and_negative_samples(tmp_path) -> None:
    frames, annotations = sample(tmp_path)
    Image.new("RGB", (100, 100), "black").save(frames / "recording-a/frame-2.jpg")
    (annotations / "recording-a/frame-2.txt").write_text("", encoding="ascii")
    generated = tmp_path / "locator"
    review = tmp_path / "review"
    build_locator_dataset(frames, annotations, generated)

    assert build_review_images(generated, review, "locator", 80) == 2
    assert Image.open(review / "recording-a/frame-1.jpg").size == (80, 80)
    assert Image.open(review / "recording-a/frame-2.jpg").size == (80, 80)


def test_audit_rejects_incomplete_minigame_annotation(tmp_path) -> None:
    frames, annotations = sample(tmp_path)
    label = annotations / "recording-a/frame-1.txt"
    label.write_text(
        "1 0.5 0.5 0.6 0.8\n"
        "2 0.5 0.6 0.1 0.2\n"
        "1 0.5 0.3 0.1 0.1\n",
        encoding="ascii",
    )

    report = audit_annotations(frames, annotations)
    assert any("multiple minigame_panel boxes" in error for error in report.errors)
    assert any("requires exactly one catch_zone and one moving_target" in error for error in report.errors)

    with pytest.raises(ValueError, match="annotation audit failed"):
        build_locator_dataset(frames, annotations, tmp_path / "direct-locator")
    with pytest.raises(ValueError, match="annotation audit failed"):
        build_minigame_dataset(frames, annotations, tmp_path / "direct-minigame")
    with pytest.raises(SystemExit):
        build_locator_main(
            ["--frames", str(frames), "--annotations", str(annotations), "--output", str(tmp_path / "locator")]
        )
    with pytest.raises(SystemExit):
        build_minigame_main(
            ["--frames", str(frames), "--annotations", str(annotations), "--output", str(tmp_path / "minigame")]
        )


def test_locator_command_rejects_empty_reviewed_dataset(tmp_path) -> None:
    frames = tmp_path / "frames"
    annotations = tmp_path / "annotations"
    (frames / "recording-a").mkdir(parents=True)
    annotations.mkdir(parents=True)
    Image.new("RGB", (10, 10), "black").save(frames / "recording-a/frame.jpg")

    output = tmp_path / "output"
    with pytest.raises(SystemExit):
        build_locator_main(
            [
                "--frames", str(frames),
                "--annotations", str(annotations),
                "--output", str(output),
            ]
        )
    assert not (output / "data.yaml").exists()


def test_minigame_command_rejects_dataset_without_panels(tmp_path) -> None:
    frames = tmp_path / "frames"
    annotations = tmp_path / "annotations"
    (frames / "recording-a").mkdir(parents=True)
    (annotations / "recording-a").mkdir(parents=True)
    Image.new("RGB", (10, 10), "black").save(frames / "recording-a/frame.jpg")
    (annotations / "recording-a/frame.txt").write_text("", encoding="ascii")
    output = tmp_path / "output"

    with pytest.raises(SystemExit):
        build_minigame_main(
            [
                "--frames", str(frames),
                "--annotations", str(annotations),
                "--output", str(output),
            ]
        )
    assert not (output / "data.yaml").exists()
