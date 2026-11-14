"""Remove Open Forge from this PC.

Installed copy  → runs the Inno Setup uninstaller (unins000.exe) next to the
                  exe: removes program files, Start-menu and desktop shortcuts
                  and the "Apps & features" entry.
Portable copy   → deletes the exe itself once the app has closed.
Optionally also removes settings (%APPDATA%\\OpenForge) and downloaded models.
"""
import shutil
import subprocess
import sys
from pathlib import Path

from core.settings import DATA_DIR
from core.ollama_client import delete_model, list_local_models

DETACHED_PROCESS = 0x00000008
CREATE_NO_WINDOW = 0x08000000


def is_frozen() -> bool:
    return bool(getattr(sys, "frozen", False))


def install_dir() -> Path | None:
    return Path(sys.executable).resolve().parent if is_frozen() else None


def uninstaller() -> Path | None:
    d = install_dir()
    if not d:
        return None
    found = sorted(d.glob("unins*.exe"))
    return found[0] if found else None


def describe() -> str:
    if not is_frozen():
        return "Запуск із вихідного коду — файли програми не чіпатимуться."
    if uninstaller():
        return f"Встановлено в {install_dir()}"
    return f"Портативна версія: {Path(sys.executable).resolve()}"


def models_summary() -> tuple[int, float]:
    models = list_local_models()
    return len(models), sum(m.get("size", 0) for m in models) / 1024 ** 3


def remove_models(progress=None) -> int:
    removed = 0
    models = list_local_models()
    for i, m in enumerate(models):
        if progress:
            progress(i, len(models), m["name"])
        if delete_model(m["name"]):
            removed += 1
    return removed


def remove_settings():
    shutil.rmtree(DATA_DIR, ignore_errors=True)


def schedule_program_removal() -> bool:
    """Start a detached helper that removes the program after this process exits."""
    if not is_frozen() or sys.platform != "win32":
        return False
    wait = "ping 127.0.0.1 -n 3 >nul"   # ~2 s, lets this process exit and unlock the exe
    unins = uninstaller()
    if unins:
        cmd = f'{wait} & "{unins}" /VERYSILENT /SUPPRESSMSGBOXES /NORESTART'
    else:
        cmd = f'{wait} & del /f /q "{Path(sys.executable).resolve()}"'
    subprocess.Popen(["cmd.exe", "/c", cmd], creationflags=DETACHED_PROCESS | CREATE_NO_WINDOW,
                     close_fds=True, cwd=str(Path.home()))
    return True
