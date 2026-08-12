from pathlib import Path
import xml.etree.ElementTree as ET


def test_packaging_inputs_resolve_inside_repository() -> None:
    repository = Path(__file__).resolve().parents[2]

    assert (repository / "packaging/build-installer.ps1").is_file()
    assert (repository / "packaging/installer.iss").is_file()
    assert (repository / "app/src/VrcFisher.Desktop/VrcFisher.Desktop.csproj").is_file()
    assert not (repository / "packaging/entrypoint.py").exists()
    assert not (repository / "packaging/vrc_fisher.spec").exists()


def test_installer_keeps_languages_embedded_and_models_optional() -> None:
    repository = Path(__file__).resolve().parents[2]
    installer = (repository / "packaging/installer.iss").read_text(encoding="utf-8")

    assert 'MessagesFile: "compiler:Default.isl"' in installer
    assert 'MessagesFile: "{#SourcePath}\\languages\\ChineseSimplified.isl"' in installer
    assert (repository / "packaging/languages/ChineseSimplified.isl").is_file()
    assert "installer-language.ini" in installer
    assert "--download-models --non-interactive" in installer


def test_application_languages_have_identical_resource_keys() -> None:
    repository = Path(__file__).resolve().parents[2]
    strings = repository / "app/src/VrcFisher.Desktop/Strings"

    def keys(language: str) -> set[str]:
        root = ET.parse(strings / language / "Resources.resw").getroot()
        return {item.attrib["name"] for item in root.findall("data")}

    assert keys("zh-CN") == keys("en-US")


def test_unpacked_app_uses_windows_app_sdk_localization() -> None:
    repository = Path(__file__).resolve().parents[2]
    desktop = repository / "app/src/VrcFisher.Desktop"
    source = "\n".join(
        path.read_text(encoding="utf-8")
        for path in desktop.glob("*.cs")
    )
    build_script = (repository / "packaging/build-installer.ps1").read_text(encoding="utf-8")

    assert "Microsoft.Windows.ApplicationModel.Resources" in source
    assert "Microsoft.Windows.Globalization.ApplicationLanguages" in source
    assert "Windows.Globalization" not in source.replace("Microsoft.Windows.Globalization", "")
    assert "Windows.ApplicationModel.Resources.ResourceLoader" not in source
    assert 'Programs\\Inno Setup 6\\ISCC.exe' in build_script
