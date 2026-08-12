from pathlib import Path

from PIL import Image
import pytest

from vrc_data_processing.build_locator_dataset import build_locator_dataset
from vrc_data_processing.build_minigame_dataset import build_minigame_dataset
from vrc_data_processing.audit_annotations import audit_annotations
from vrc_data_processing.build_locator_dataset import main as build_locator_main
from vrc_data_processing.split_by_recording import split_by_recording


def sample(tmp_path: Path, recording: str = "recording-a") -> tuple[Path, Path]:
    frames = tmp_path / "frames"
    annotations = tmp_path / "annotations"
    (frames / recording).mkdir(parents=True)
    (annotations / recording).mkdir(parents=True)
    Image.new("RGB", (100, 100), "black").save(frames / recording / "frame-1.jpg")
    (annotations / recording / "frame-1.txt").write_text(
        "1 0.5 0.5 0.6 0.8\n"
        "4 0.5 0.5 0.1 0.6\n"
        "5 0.5 0.6 0.1 0.2\n"
        "6 0.5 0.3 0.1 0.1\n",
        encoding="ascii",
    )
    return frames, annotations


def test_builds_locator_and_minigame_from_one_full_screen_label_set(tmp_path) -> None:
    frames, annotations = sample(tmp_path)
    locator = tmp_path / "locator"
    minigame = tmp_path / "minigame"

    assert build_locator_dataset(frames, annotations, locator) == (1, 1)
    assert build_minigame_dataset(frames, annotations, minigame, padding=0.0) == (1, 3)
    assert Image.open(minigame / "images/recording-a/frame-1.jpg").size == (60, 80)
    assert (locator / "labels/recording-a/frame-1.txt").read_text().startswith("1 ")
    local_ids = [line.split()[0] for line in (minigame / "labels/recording-a/frame-1.txt").read_text().splitlines()]
    assert local_ids == ["0", "1", "2"]


def test_split_refuses_one_recording_for_train_and_validation(tmp_path) -> None:
    frames, annotations = sample(tmp_path)
    generated = tmp_path / "locator"
    build_locator_dataset(frames, annotations, generated)

    with pytest.raises(ValueError, match="collect more recordings"):
        split_by_recording(generated, tmp_path / "dataset")


def test_split_keeps_each_recording_in_one_partition(tmp_path) -> None:
    frames, annotations = sample(tmp_path, "recording-a")
    other_frames, _ = sample(tmp_path, "recording-b")
    assert other_frames == frames
    generated = tmp_path / "locator"
    build_locator_dataset(frames, annotations, generated)

    assignments = split_by_recording(generated, tmp_path / "dataset", 0.5, 0.5, 0.0)
    assert len(assignments["train"]) == 1
    assert len(assignments["val"]) == 1
    assert set(assignments["train"]).isdisjoint(assignments["val"])


def test_unannotated_frames_are_not_treated_as_negative_samples(tmp_path) -> None:
    frames, annotations = sample(tmp_path)
    Image.new("RGB", (100, 100), "black").save(frames / "recording-a/frame-2.jpg")

    assert build_locator_dataset(frames, annotations, tmp_path / "locator") == (1, 1)


def test_annotation_audit_reports_minigame_labels_without_ui_group(tmp_path) -> None:
    frames, annotations = sample(tmp_path)
    label = annotations / "recording-a/frame-1.txt"
    label.write_text("6 0.5 0.5 0.1 0.1\n", encoding="ascii")

    report = audit_annotations(frames, annotations)

    assert report.errors == (f"{label}: minigame labels without fishing_ui_group",)


def test_locator_command_rejects_empty_reviewed_dataset(tmp_path, monkeypatch) -> None:
    frames = tmp_path / "frames"
    annotations = tmp_path / "annotations"
    (frames / "recording-a").mkdir(parents=True)
    (annotations / "recording-a").mkdir(parents=True)
    Image.new("RGB", (10, 10), "black").save(frames / "recording-a/frame.jpg")

    with pytest.raises(SystemExit):
        build_locator_main(
            [
                "--frames", str(frames),
                "--annotations", str(annotations),
                "--output", str(tmp_path / "output"),
            ]
        )
