"""Transactional replacement for generated dataset directories."""

from __future__ import annotations

from contextlib import contextmanager
import filecmp
from pathlib import Path
import shutil
import tempfile
from typing import Iterator
from uuid import uuid4


@contextmanager
def staged_output(destination: Path) -> Iterator[Path]:
    destination.parent.mkdir(parents=True, exist_ok=True)
    staging = Path(tempfile.mkdtemp(prefix=f".{destination.name}-build-", dir=destination.parent))
    try:
        yield staging
        (staging / ".gitkeep").write_text("\n", encoding="ascii")
        _sync_tree(staging, destination)
    finally:
        if staging.exists():
            shutil.rmtree(staging)


def _sync_tree(source: Path, destination: Path) -> None:
    destination.mkdir(parents=True, exist_ok=True)
    expected_directories = {
        path.relative_to(source)
        for path in source.rglob("*")
        if path.is_dir()
    }
    for relative in sorted(expected_directories):
        (destination / relative).mkdir(parents=True, exist_ok=True)
    expected = {
        path.relative_to(source)
        for path in source.rglob("*")
        if path.is_file()
    }
    for relative in sorted(expected):
        source_file = source / relative
        destination_file = destination / relative
        destination_file.parent.mkdir(parents=True, exist_ok=True)
        if destination_file.is_file() and filecmp.cmp(source_file, destination_file, shallow=False):
            continue
        temporary = destination_file.with_name(f".{destination_file.name}.{uuid4().hex}.tmp")
        try:
            shutil.copy2(source_file, temporary)
            temporary.replace(destination_file)
        finally:
            if temporary.exists():
                temporary.unlink()
    existing = {
        path.relative_to(destination)
        for path in destination.rglob("*")
        if path.is_file()
    }
    for relative in sorted(existing - expected, reverse=True):
        (destination / relative).unlink()
    for directory in sorted(
        (path for path in destination.rglob("*") if path.is_dir()),
        key=lambda path: len(path.parts),
        reverse=True,
    ):
        if directory.relative_to(destination) not in expected_directories and not any(directory.iterdir()):
            directory.rmdir()
