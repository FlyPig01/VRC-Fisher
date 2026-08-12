from pathlib import Path

from vrc_fisher.resources import resource_root, software_root, user_data_root


def test_source_resource_root_contains_default_config() -> None:
    root = resource_root()

    assert isinstance(root, Path)
    assert (root / "config/default.toml").is_file()
    assert software_root() == root
    assert user_data_root() == root
