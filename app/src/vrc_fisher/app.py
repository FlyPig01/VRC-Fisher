"""Command-line entry point."""

from __future__ import annotations

import argparse
from dataclasses import replace
import json
import logging
from pathlib import Path
import sys

from vrc_fisher.automation import FishingAutomation
from vrc_fisher.config import AppConfig, load_config
from vrc_fisher.input.mouse import MouseInputSink, NoopInputSink
from vrc_fisher.models import ModelManager, ModelManagerError
from vrc_fisher.resources import user_data_root
from vrc_fisher.window.win32 import WindowNotFoundError


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description="VRChat fishing automation")
    subcommands = parser.add_subparsers(dest="command")
    models = subcommands.add_parser("models", help="manage separately released ONNX models")
    model_commands = models.add_subparsers(dest="model_command", required=True)
    for command in ("status", "install", "remove"):
        model_command = model_commands.add_parser(command)
        model_command.add_argument(
            "--repository",
            help="GitHub owner/name override for source-development testing",
        )

    mode = parser.add_mutually_exclusive_group()
    mode.add_argument(
        "--live",
        action="store_true",
        help="send mouse input; without this flag the program only observes",
    )
    mode.add_argument("--observe", action="store_true", help="explicit observe-only mode")
    parser.add_argument("--config", type=Path, help="TOML configuration path")
    parser.add_argument("--monitor", type=int, help="mss monitor index, starting at 1")
    parser.add_argument("--window", help="window title fragment")
    parser.add_argument(
        "--device",
        choices=("auto", "cpu", "gpu"),
        help="inference device; auto prefers DirectML when packaged",
    )
    parser.add_argument("--max-seconds", type=float, help="stop automatically after this duration")
    parser.add_argument("--artifacts", type=Path)
    parser.add_argument("--log-file", type=Path)
    return parser


def _override_config(config: AppConfig, args: argparse.Namespace) -> AppConfig:
    capture = config.capture
    window = config.window
    vision = config.vision
    if args.monitor is not None:
        capture = replace(capture, monitor=args.monitor)
    if args.window is not None:
        window = replace(window, title_contains=args.window)
    if args.device is not None:
        vision = replace(vision, device=args.device)
    return replace(config, capture=capture, window=window, vision=vision)


def _configure_logging(level: str, path: Path) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    logging.basicConfig(
        level=getattr(logging, level.upper(), logging.INFO),
        format="%(asctime)s %(levelname)s %(message)s",
        handlers=[
            logging.StreamHandler(sys.stdout),
            logging.FileHandler(path, encoding="utf-8"),
        ],
        force=True,
    )


def main(argv: list[str] | None = None) -> int:
    parser = build_parser()
    args = parser.parse_args(argv)
    if args.command == "models":
        return _manage_models(args)
    if args.max_seconds is not None and args.max_seconds <= 0:
        parser.error("--max-seconds must be positive")
    try:
        config = _override_config(load_config(args.config), args)
        data_root = user_data_root()
        artifacts = args.artifacts or data_root / "artifacts"
        log_file = args.log_file or data_root / "logs" / "vrc-fisher.log"
        _configure_logging(config.debug.log_level, log_file)
        sink = (
            MouseInputSink(config.control.click_duration_seconds)
            if args.live
            else NoopInputSink()
        )
        automation = FishingAutomation(config, sink, args.live, artifacts)
        automation.run(args.max_seconds)
        return 0
    except (OSError, RuntimeError, ValueError, WindowNotFoundError) as error:
        logging.getLogger("vrc_fisher").error("startup/runtime failure: %s", error)
        return 1
    except KeyboardInterrupt:
        logging.getLogger("vrc_fisher").warning("interrupted")
        return 130


def _manage_models(args: argparse.Namespace) -> int:
    try:
        manager = ModelManager(repository=args.repository)
        if args.model_command == "status":
            print(json.dumps(manager.status(), indent=2, ensure_ascii=False))
            return 0
        if args.model_command == "install":
            release = manager.install()
            print(f"installed model version {release.version} in {manager.model_dir}")
            return 0
        removed = manager.remove()
        print(f"removed {len(removed)} model files from {manager.model_dir}")
        return 0
    except (ModelManagerError, OSError, RuntimeError) as error:
        print(f"model operation failed: {error}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
