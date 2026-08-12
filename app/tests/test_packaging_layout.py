from pathlib import Path


def test_packaging_inputs_resolve_inside_repository() -> None:
    repository = Path(__file__).resolve().parents[2]

    assert (repository / "packaging/entrypoint.py").is_file()
    assert (repository / "packaging/vrc_fisher.spec").is_file()
    assert (repository / "app/config/default.toml").is_file()
