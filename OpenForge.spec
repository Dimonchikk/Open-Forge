# -*- mode: python ; coding: utf-8 -*-
# Build: pyinstaller OpenForge.spec  ->  dist/OpenForge.exe
from PyInstaller.utils.hooks import collect_submodules, collect_data_files

hiddenimports = (
    collect_submodules('ui') +
    collect_submodules('core') +
    collect_submodules('agent') +
    collect_submodules('models_db') +
    collect_submodules('customtkinter') +
    ['pynvml', 'psutil', 'requests', 'PIL', 'PIL._tkinter_finder', 'PIL.ImageTk', 'winreg']
)

datas = collect_data_files('customtkinter') + [('assets', 'assets')]

a = Analysis(
    ['main.py'],
    pathex=['.'],
    binaries=[],
    datas=datas,
    hiddenimports=hiddenimports,
    hookspath=[],
    hooksconfig={},
    runtime_hooks=[],
    excludes=['ollama', 'GPUtil', 'numpy', 'matplotlib'],
    noarchive=False,
)

pyz = PYZ(a.pure)

exe = EXE(
    pyz,
    a.scripts,
    a.binaries,
    a.datas,
    [],
    name='OpenForge',
    icon='assets/icon.ico',
    version=None,
    debug=False,
    bootloader_ignore_signals=False,
    strip=False,
    upx=False,
    runtime_tmpdir=None,
    console=False,
    disable_windowed_traceback=False,
    argv_emulation=False,
    target_arch=None,
    codesign_identity=None,
    entitlements_file=None,
)
