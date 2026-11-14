"""Find, install and start Ollama on Windows.

Install = download OllamaSetup.exe (official GitHub release), run it silently
with /DIR=<chosen folder>, store the models folder in the user's OLLAMA_MODELS
environment variable, then start Ollama.
"""
import os
import shutil
import subprocess
import sys
import tempfile
import time
from pathlib import Path

import requests

from core.ollama_client import is_ollama_running

INSTALLER_URL = "https://github.com/ollama/ollama/releases/latest/download/OllamaSetup.exe"
CREATE_NO_WINDOW = 0x08000000
DETACHED_PROCESS = 0x00000008

RUNNING, INSTALLED, MISSING = "running", "installed", "missing"


# ── user environment variables (persisted in HKCU\Environment) ────────────────

def get_user_env(name: str) -> str:
    if sys.platform != "win32":
        return os.environ.get(name, "")
    import winreg
    try:
        with winreg.OpenKey(winreg.HKEY_CURRENT_USER, "Environment") as k:
            return str(winreg.QueryValueEx(k, name)[0])
    except OSError:
        return ""


def set_user_env(name: str, value: str):
    os.environ[name] = value              # child processes started by us see it right away
    if sys.platform != "win32":
        return
    import winreg
    import ctypes
    with winreg.OpenKey(winreg.HKEY_CURRENT_USER, "Environment", 0, winreg.KEY_SET_VALUE) as k:
        winreg.SetValueEx(k, name, 0, winreg.REG_EXPAND_SZ, value)
    # tell Explorer & co. that the environment changed (so new programs pick it up)
    result = ctypes.c_ulong()
    ctypes.windll.user32.SendMessageTimeoutW(0xFFFF, 0x001A, 0, "Environment", 0x0002, 3000,
                                             ctypes.byref(result))


# ── detection ─────────────────────────────────────────────────────────────────

def _registry_locations() -> list[Path]:
    if sys.platform != "win32":
        return []
    import winreg
    found = []
    roots = [(winreg.HKEY_CURRENT_USER, r"Software\Microsoft\Windows\CurrentVersion\Uninstall"),
             (winreg.HKEY_LOCAL_MACHINE, r"Software\Microsoft\Windows\CurrentVersion\Uninstall")]
    for hive, path in roots:
        try:
            root = winreg.OpenKey(hive, path)
        except OSError:
            continue
        for i in range(2000):
            try:
                sub = winreg.EnumKey(root, i)
            except OSError:
                break
            try:
                with winreg.OpenKey(root, sub) as k:
                    if "ollama" in str(winreg.QueryValueEx(k, "DisplayName")[0]).lower():
                        found.append(Path(winreg.QueryValueEx(k, "InstallLocation")[0]))
            except OSError:
                continue
    return found


def find_ollama_dir() -> Path | None:
    """Folder that contains ollama.exe, or None."""
    exe = shutil.which("ollama")
    if exe:
        return Path(exe).resolve().parent
    candidates = _registry_locations()
    local = os.environ.get("LOCALAPPDATA")
    if local:
        candidates.append(Path(local) / "Programs" / "Ollama")
    for d in candidates:
        if (d / "ollama.exe").is_file():
            return d
    return None


def status() -> str:
    if is_ollama_running():
        return RUNNING
    return INSTALLED if find_ollama_dir() else MISSING


def default_dirs() -> tuple[Path, Path]:
    """Suggested (ollama_dir, models_dir): a second drive D: if present, else the defaults."""
    models_env = get_user_env("OLLAMA_MODELS")
    if sys.platform == "win32" and Path("D:/").exists():
        base = Path("D:/AI")
        return base / "Ollama", Path(models_env) if models_env else base / "ollama-models"
    local = Path(os.environ.get("LOCALAPPDATA", Path.home()))
    return (local / "Programs" / "Ollama",
            Path(models_env) if models_env else Path.home() / ".ollama" / "models")


def free_gb(path: Path) -> float | None:
    p = Path(path)
    while not p.exists() and p.parent != p:
        p = p.parent
    try:
        return shutil.disk_usage(p).free / 1024 ** 3
    except OSError:
        return None


# ── install & start ───────────────────────────────────────────────────────────

def download_installer(progress, stop) -> Path | None:
    """progress(done_bytes, total_bytes, bytes_per_sec). Returns the file, or None if cancelled."""
    dest = Path(tempfile.gettempdir()) / "OllamaSetup.exe"
    t0, done = time.time(), 0
    with requests.get(INSTALLER_URL, stream=True, timeout=(10, 120)) as r:
        r.raise_for_status()
        total = int(r.headers.get("Content-Length", 0))
        with open(dest, "wb") as f:
            for chunk in r.iter_content(1024 * 1024):
                if stop.is_set():
                    f.close()
                    dest.unlink(missing_ok=True)
                    return None
                f.write(chunk)
                done += len(chunk)
                progress(done, total, done / max(time.time() - t0, 0.001))
    return dest


def run_installer(installer: Path, target_dir: Path) -> int:
    """Silent install into target_dir (shows only a small progress window)."""
    target_dir.mkdir(parents=True, exist_ok=True)
    cmd = f'"{installer}" /SILENT /SUPPRESSMSGBOXES /NORESTART /DIR="{target_dir}"'
    return subprocess.run(cmd).returncode


def start(ollama_dir: Path | None = None) -> bool:
    d = ollama_dir or find_ollama_dir()
    if not d:
        return False
    app = d / "ollama app.exe"
    flags = DETACHED_PROCESS | CREATE_NO_WINDOW if sys.platform == "win32" else 0
    if app.is_file():
        subprocess.Popen([str(app)], creationflags=flags, close_fds=True, env=os.environ.copy())
    else:
        subprocess.Popen([str(d / "ollama.exe"), "serve"], creationflags=flags, close_fds=True,
                         env=os.environ.copy())
    return True


def wait_running(timeout: float = 60) -> bool:
    end = time.time() + timeout
    while time.time() < end:
        if is_ollama_running():
            return True
        time.sleep(1)
    return False
