"""Small JSON settings store in %APPDATA%\\OpenForge (or ~/.config/OpenForge)."""
import json
import os
import sys
from pathlib import Path

if sys.platform == "win32" and os.environ.get("APPDATA"):
    DATA_DIR = Path(os.environ["APPDATA"]) / "OpenForge"
else:
    DATA_DIR = Path.home() / ".config" / "OpenForge"

_FILE = DATA_DIR / "settings.json"
_DEFAULTS = {
    "theme": "system",          # system | light | dark
    "model": "",                # last chat model
    "system_prompt": "",
    "agent_folder": "",
    "show_hw_panel": True,
}
_data: dict = {}


def _load():
    global _data
    try:
        _data = json.loads(_FILE.read_text(encoding="utf-8"))
    except Exception:
        _data = {}


def get(key: str):
    if not _data:
        _load()
    return _data.get(key, _DEFAULTS.get(key))


def set(key: str, value):  # noqa: A001 - mirrors dict API on purpose
    if not _data:
        _load()
    _data[key] = value
    try:
        DATA_DIR.mkdir(parents=True, exist_ok=True)
        _FILE.write_text(json.dumps(_data, ensure_ascii=False, indent=2), encoding="utf-8")
    except OSError:
        pass  # read-only profile etc. — settings just won't persist


def resource_path(rel: str) -> Path:
    """Path to a bundled file, both in dev and inside the PyInstaller exe."""
    base = Path(getattr(sys, "_MEIPASS", Path(__file__).resolve().parent.parent))
    return base / rel
