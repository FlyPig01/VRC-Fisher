from pathlib import Path
import json

import pytest
from PIL import Image

from vrc_training.config import TaskConfig, load_train_config
from vrc_training.environment import TRAINING_ROOT, configure_ultralytics
from vrc_training.export import export_image_sizes
from vrc_training.preflight import preflight_task
from vrc_training.train import main as train_main


def test_default_training_config_loads() -> None:
    path = Path(__file__).resolve().parents[1] / "configs/default.toml"
    config = load_train_config(path)

    assert config.device == "0"
    assert config.locator.data.endswith("locator/data.yaml")
    assert config.locator.image_size == 960
    assert config.locator.batch == 4
    assert config.minigame.base_model == "yolo11n.pt"
    assert config.minigame.image_size == 640
    assert config.minigame.batch == 8
    assert config.minigame.run_name is None


def test_pending_training_config_uses_retained_best_weights() -> None:
    root = Path(__file__).resolve().parents[1]
    config = load_train_config(root / "configs/pending.toml")

    assert config.locator.base_model == "runs/locator-best-init/weights/best.pt"
    assert config.locator.run_name == "locator-round3"
    assert config.minigame.base_model == "runs/minigame-best-init/weights/best.pt"
    assert config.minigame.run_name == "minigame-round3"
    assert (root / config.locator.base_model).is_file()
    assert (root / config.minigame.base_model).is_file()


def test_unknown_training_setting_is_rejected(tmp_path: Path) -> None:
    path = tmp_path / "invalid.toml"
    path.write_text("[runtime]\nunknown = 1\n", encoding="ascii")

    with pytest.raises(ValueError, match="unknown runtime settings"):
        load_train_config(path)


def test_invalid_training_run_name_is_rejected(tmp_path: Path) -> None:
    path = tmp_path / "invalid-run.toml"
    path.write_text(
        "[locator]\ndata = 'datasets/locator/data.yaml'\nrun_name = '../outside'\n",
        encoding="ascii",
    )

    with pytest.raises(ValueError, match="run_name"):
        load_train_config(path)


def test_export_uses_each_models_training_size_by_default() -> None:
    path = Path(__file__).resolve().parents[1] / "configs/default.toml"
    config = load_train_config(path)

    assert export_image_sizes(config, None, None) == {
        "locator": 960,
        "minigame": 640,
    }
    assert export_image_sizes(config, 1280, 320) == {
        "locator": 1280,
        "minigame": 320,
    }
    with pytest.raises(ValueError, match="positive"):
        export_image_sizes(config, 0, None)


def write_dataset(tmp_path: Path, task: str = "locator") -> TaskConfig:
    root = tmp_path / "datasets" / task
    names = (
        ("bite_indicator", "minigame_panel")
        if task == "locator"
        else ("catch_zone", "moving_target")
    )
    (root / "data.yaml").parent.mkdir(parents=True, exist_ok=True)
    (root / "data.yaml").write_text(
        "train: images/train\n"
        "val: images/val\n"
        "names:\n"
        f"  0: {names[0]}\n"
        f"  1: {names[1]}\n",
        encoding="ascii",
    )
    assignments = {"train": ["recording-a"], "val": ["recording-b"]}
    (root / "split.json").write_text(
        json.dumps(assignments, ensure_ascii=False), encoding="utf-8"
    )
    for split, recording in (("train", "recording-a"), ("val", "recording-b")):
        image = root / "images" / split / f"{recording}--frame.jpg"
        label = root / "labels" / split / f"{recording}--frame.txt"
        image.parent.mkdir(parents=True, exist_ok=True)
        label.parent.mkdir(parents=True, exist_ok=True)
        Image.new("RGB", (16, 16), "black").save(image)
        label.write_text("0 0.5 0.5 0.2 0.2\n1 0.5 0.5 0.2 0.2\n", encoding="ascii")
    return TaskConfig(data=f"datasets/{task}/data.yaml")


def test_ultralytics_resolves_dataset_splits_from_yaml_directory(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    task = write_dataset(tmp_path)
    monkeypatch.chdir(tmp_path)
    configure_ultralytics()
    from ultralytics.data.utils import check_det_dataset

    resolved = check_det_dataset(tmp_path / task.data)

    assert Path(resolved["train"]) == (tmp_path / "datasets/locator/images/train").resolve()
    assert Path(resolved["val"]) == (tmp_path / "datasets/locator/images/val").resolve()


def test_preflight_accepts_two_recording_dataset_with_both_classes(tmp_path: Path) -> None:
    task = write_dataset(tmp_path)

    report = preflight_task("locator", task, tmp_path)

    assert report.recordings == 2
    assert report.images == 2
    assert report.boxes == (2, 2)


def test_preflight_rejects_recording_in_multiple_splits(tmp_path: Path) -> None:
    task = write_dataset(tmp_path)
    split = tmp_path / "datasets/locator/split.json"
    split.write_text(
        json.dumps({"train": ["recording-a"], "val": ["recording-a"]}),
        encoding="utf-8",
    )

    with pytest.raises(ValueError, match="multiple splits"):
        preflight_task("locator", task, tmp_path)


def test_preflight_rejects_split_missing_a_class(tmp_path: Path) -> None:
    task = write_dataset(tmp_path)
    label = tmp_path / "datasets/locator/labels/val/recording-b--frame.txt"
    label.write_text("1 0.5 0.5 0.2 0.2\n", encoding="ascii")

    with pytest.raises(ValueError, match="must contain both classes"):
        preflight_task("locator", task, tmp_path)


def test_minigame_preflight_accepts_empty_negative_labels(tmp_path: Path) -> None:
    task = write_dataset(tmp_path, "minigame")
    image = tmp_path / "datasets/minigame/images/train/recording-a--negative.jpg"
    label = tmp_path / "datasets/minigame/labels/train/recording-a--negative.txt"
    Image.new("RGB", (16, 16), "black").save(image)
    label.write_text("", encoding="ascii")

    report = preflight_task("minigame", task, tmp_path)

    assert report.images == 3
    assert report.positives == 2
    assert report.negatives == 1


def test_minigame_preflight_rejects_incomplete_positive(tmp_path: Path) -> None:
    task = write_dataset(tmp_path, "minigame")
    label = tmp_path / "datasets/minigame/labels/val/recording-b--frame.txt"
    label.write_text("1 0.5 0.5 0.2 0.2\n", encoding="ascii")

    with pytest.raises(ValueError, match="must be empty or contain"):
        preflight_task("minigame", task, tmp_path)


def test_training_requires_explicit_review_confirmation() -> None:
    with pytest.raises(SystemExit):
        train_main(["--task", "locator"])


def test_ultralytics_config_stays_in_training_directory(monkeypatch) -> None:
    monkeypatch.setenv("YOLO_CONFIG_DIR", "outside")

    configured = configure_ultralytics()

    assert configured == TRAINING_ROOT / ".ultralytics"
    assert Path(configured).is_relative_to(TRAINING_ROOT)
