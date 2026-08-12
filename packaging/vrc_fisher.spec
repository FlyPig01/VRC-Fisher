from pathlib import Path

from PyInstaller.utils.hooks import collect_dynamic_libs


project_root = Path(SPECPATH).parent
app_root = project_root / "app"

analysis = Analysis(
    [str(project_root / "packaging" / "entrypoint.py")],
    pathex=[str(app_root / "src")],
    binaries=collect_dynamic_libs("onnxruntime"),
    datas=[],
    hiddenimports=[],
    hookspath=[],
    hooksconfig={},
    runtime_hooks=[],
    excludes=["cv2", "torch", "torchvision", "ultralytics"],
    noarchive=False,
)
pyz = PYZ(analysis.pure)

exe = EXE(
    pyz,
    analysis.scripts,
    [],
    exclude_binaries=True,
    name="vrc-fisher",
    debug=False,
    bootloader_ignore_signals=False,
    strip=False,
    upx=True,
    console=True,
)

collect = COLLECT(
    exe,
    analysis.binaries,
    analysis.datas,
    strip=False,
    upx=True,
    name="vrc-fisher",
)
