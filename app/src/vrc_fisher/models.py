"""Download, verify, inspect, and remove separately released ONNX models."""

from __future__ import annotations

from dataclasses import dataclass
import hashlib
import json
from pathlib import Path
import shutil
from typing import Any
from urllib.error import HTTPError, URLError
from urllib.request import Request, urlopen

from vrc_fisher.resources import release_metadata_path, user_data_root


MODEL_FILENAMES = ("locator.onnx", "minigame.onnx")
MANIFEST_ASSET = "model-manifest.json"
MODEL_RELEASE_PREFIX = "models-v"
MODEL_SCHEMA_VERSION = 1
RUNTIME_MODEL_API = 1


class ModelManagerError(RuntimeError):
    pass


@dataclass(frozen=True, slots=True)
class ModelAsset:
    filename: str
    size: int
    sha256: str
    url: str


@dataclass(frozen=True, slots=True)
class ModelRelease:
    version: str
    assets: tuple[ModelAsset, ...]


class ModelManager:
    def __init__(
        self,
        repository: str | None = None,
        data_root: Path | None = None,
        api_base: str = "https://api.github.com",
    ) -> None:
        self.repository = repository or _configured_repository(required=False)
        if self.repository is not None:
            _validate_repository(self.repository)
        self.data_root = data_root or user_data_root()
        self.model_dir = self.data_root / "models"
        self.api_base = api_base.rstrip("/")

    def status(self) -> dict[str, Any]:
        metadata = _read_json(self.model_dir / "installed-models.json", required=False)
        models = {}
        for filename in MODEL_FILENAMES:
            path = self.model_dir / filename
            models[filename] = {
                "installed": path.is_file(),
                "size": path.stat().st_size if path.is_file() else 0,
            }
        return {
            "repository": self.repository,
            "model_dir": str(self.model_dir),
            "version": metadata.get("version") if metadata else None,
            "models": models,
        }

    def install(self) -> ModelRelease:
        if self.repository is None:
            raise ModelManagerError(
                "release repository is not configured; use --repository owner/name in source development"
            )
        release_json = self._latest_model_release()
        assets_by_name = {
            asset["name"]: asset["browser_download_url"]
            for asset in release_json.get("assets", [])
            if isinstance(asset, dict)
            and isinstance(asset.get("name"), str)
            and isinstance(asset.get("browser_download_url"), str)
        }
        manifest_url = assets_by_name.get(MANIFEST_ASSET)
        if not manifest_url:
            raise ModelManagerError(
                f"release {release_json.get('tag_name')} has no {MANIFEST_ASSET} asset"
            )
        manifest = _download_json(manifest_url)
        model_release = _parse_manifest(manifest, assets_by_name)

        staging_dir = self.data_root / ".models-staging"
        backup_dir = self.data_root / ".models-backup"
        self.data_root.mkdir(parents=True, exist_ok=True)
        _remove_directory(staging_dir)
        if backup_dir.exists() and not self.model_dir.exists():
            backup_dir.replace(self.model_dir)
        else:
            _remove_directory(backup_dir)
        staging_dir.mkdir()
        try:
            for asset in model_release.assets:
                temporary = staging_dir / f"{asset.filename}.part"
                _download_file(asset, temporary)
                temporary.replace(staging_dir / asset.filename)
            metadata_path = staging_dir / "installed-models.json"
            metadata_path.write_text(
                json.dumps(
                    {
                        "schema_version": MODEL_SCHEMA_VERSION,
                        "runtime_api": RUNTIME_MODEL_API,
                        "version": model_release.version,
                        "models": [asset.filename for asset in model_release.assets],
                    },
                    indent=2,
                ),
                encoding="utf-8",
            )
            if self.model_dir.exists():
                self.model_dir.replace(backup_dir)
            try:
                staging_dir.replace(self.model_dir)
            except OSError:
                if backup_dir.exists() and not self.model_dir.exists():
                    backup_dir.replace(self.model_dir)
                raise
            _remove_directory(backup_dir)
        finally:
            _remove_directory(staging_dir)
        return model_release

    def remove(self) -> list[Path]:
        removed: list[Path] = []
        for filename in (*MODEL_FILENAMES, "installed-models.json"):
            path = self.model_dir / filename
            if path.is_file():
                path.unlink()
                removed.append(path)
        try:
            self.model_dir.rmdir()
        except OSError:
            pass
        return removed

    def _latest_model_release(self) -> dict[str, Any]:
        assert self.repository is not None
        url = f"{self.api_base}/repos/{self.repository}/releases?per_page=100"
        releases = _download_json(url)
        if not isinstance(releases, list):
            raise ModelManagerError("GitHub releases response is not a list")
        for release in releases:
            if not isinstance(release, dict):
                continue
            tag = release.get("tag_name")
            if (
                isinstance(tag, str)
                and tag.startswith(MODEL_RELEASE_PREFIX)
                and not release.get("draft")
                and not release.get("prerelease")
            ):
                return release
        raise ModelManagerError(
            f"repository {self.repository} has no published {MODEL_RELEASE_PREFIX}* release"
        )


def resolve_model_path(configured: str | Path) -> Path:
    path = Path(configured)
    if path.is_absolute():
        return path
    user_path = user_data_root() / path
    if user_path.is_file():
        return user_path
    from vrc_fisher.resources import resource_root

    return resource_root() / path


def _remove_directory(path: Path) -> None:
    if path.is_dir():
        shutil.rmtree(path)
    elif path.exists():
        path.unlink()


def _configured_repository(required: bool = True) -> str | None:
    metadata = _read_json(release_metadata_path(), required=False)
    repository = metadata.get("repository") if metadata else None
    if not isinstance(repository, str) or not repository:
        if not required:
            return None
        raise ModelManagerError(
            "release repository is not configured; set it when building the installer"
        )
    return repository


def _validate_repository(repository: str) -> None:
    parts = repository.split("/")
    if len(parts) != 2 or not all(parts) or any(part in {".", ".."} for part in parts):
        raise ModelManagerError("repository must use the owner/name form")
    allowed = set("abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-_.")
    if any(set(part) - allowed for part in parts):
        raise ModelManagerError("repository contains unsupported characters")


def _parse_manifest(
    manifest: Any,
    release_assets: dict[str, str],
) -> ModelRelease:
    if not isinstance(manifest, dict):
        raise ModelManagerError("model manifest is not an object")
    if manifest.get("schema_version") != MODEL_SCHEMA_VERSION:
        raise ModelManagerError("unsupported model manifest schema")
    if manifest.get("runtime_api") != RUNTIME_MODEL_API:
        raise ModelManagerError("model release is incompatible with this application")
    version = manifest.get("version")
    raw_models = manifest.get("models")
    if not isinstance(version, str) or not version or not isinstance(raw_models, list):
        raise ModelManagerError("model manifest is missing version or models")

    parsed: list[ModelAsset] = []
    for filename in MODEL_FILENAMES:
        matches = [item for item in raw_models if isinstance(item, dict) and item.get("filename") == filename]
        if len(matches) != 1:
            raise ModelManagerError(f"model manifest must contain exactly one {filename}")
        item = matches[0]
        size = item.get("size")
        sha256 = item.get("sha256")
        url = release_assets.get(filename)
        if not isinstance(size, int) or size <= 0:
            raise ModelManagerError(f"invalid size for {filename}")
        if (
            not isinstance(sha256, str)
            or len(sha256) != 64
            or any(character not in "0123456789abcdef" for character in sha256)
        ):
            raise ModelManagerError(f"invalid SHA-256 for {filename}")
        if not url:
            raise ModelManagerError(f"release asset is missing: {filename}")
        parsed.append(ModelAsset(filename, size, sha256, url))
    return ModelRelease(version, tuple(parsed))


def _download_json(url: str) -> Any:
    try:
        request = Request(url, headers={"Accept": "application/vnd.github+json", "User-Agent": "VRC-Fisher"})
        with urlopen(request, timeout=30) as response:
            return json.load(response)
    except (HTTPError, URLError, TimeoutError, json.JSONDecodeError) as error:
        raise ModelManagerError(f"cannot download JSON from {url}: {error}") from error


def _download_file(asset: ModelAsset, destination: Path) -> None:
    digest = hashlib.sha256()
    size = 0
    try:
        request = Request(asset.url, headers={"User-Agent": "VRC-Fisher"})
        with urlopen(request, timeout=60) as response, destination.open("wb") as output:
            while chunk := response.read(1024 * 1024):
                output.write(chunk)
                digest.update(chunk)
                size += len(chunk)
    except (HTTPError, URLError, TimeoutError, OSError) as error:
        destination.unlink(missing_ok=True)
        raise ModelManagerError(f"cannot download {asset.filename}: {error}") from error
    if size != asset.size or digest.hexdigest() != asset.sha256:
        destination.unlink(missing_ok=True)
        raise ModelManagerError(f"integrity check failed for {asset.filename}")


def _read_json(path: Path, required: bool) -> dict[str, Any]:
    if not path.is_file():
        if required:
            raise ModelManagerError(f"JSON file not found: {path}")
        return {}
    try:
        value = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as error:
        if required:
            raise ModelManagerError(f"cannot read JSON file {path}: {error}") from error
        return {}
    return value if isinstance(value, dict) else {}
