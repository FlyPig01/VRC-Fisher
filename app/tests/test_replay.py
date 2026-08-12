import json

from PIL import Image

from vrc_fisher.config import AppConfig
from vrc_fisher.contracts import Observation
from vrc_fisher.replay import replay_frames


class EmptyDetector:
    def observe(self, frame):
        return Observation(frame.sequence, frame.captured_at_ns)


def test_replay_reads_manifest_and_emits_state_events(tmp_path) -> None:
    frames = tmp_path / "frames"
    frames.mkdir()
    Image.new("RGB", (8, 8), "black").save(frames / "frame.jpg")
    manifest = tmp_path / "manifest.jsonl"
    manifest.write_text(
        json.dumps({"timestamp_seconds": 0.0, "image": "frame.jpg"}) + "\n",
        encoding="utf-8",
    )

    result = replay_frames(
        manifest,
        frames,
        AppConfig(),
        tmp_path / "events.jsonl",
        EmptyDetector(),
    )

    assert result["frames"] == 1
    assert result["events"][0]["reason"] == "cast"
