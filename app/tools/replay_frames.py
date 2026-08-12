"""Replay an extracted-frame manifest through the production pipeline."""

from __future__ import annotations

import argparse
import json
from pathlib import Path

from vrc_fisher.config import load_config
from vrc_fisher.replay import replay_frames


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--manifest", type=Path, required=True)
    parser.add_argument("--frames-root", type=Path, required=True)
    parser.add_argument("--config", type=Path)
    parser.add_argument("--events", type=Path, default=Path("artifacts/replay-events.jsonl"))
    parser.add_argument("--summary", type=Path, default=Path("artifacts/replay-summary.json"))
    args = parser.parse_args()
    result = replay_frames(
        args.manifest,
        args.frames_root,
        load_config(args.config),
        args.events,
    )
    args.summary.parent.mkdir(parents=True, exist_ok=True)
    args.summary.write_text(
        json.dumps(result, ensure_ascii=True, indent=2) + "\n",
        encoding="utf-8",
    )
    print(json.dumps(result["metrics"], indent=2))
    print(f"events={len(result['events'])} summary={args.summary}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
