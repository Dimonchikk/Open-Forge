"""One-click update: download OpenForge-setup.exe from the latest release and run it.

Installed copy → the installer runs silently, reuses the previous install
folder (Inno Setup remembers it by AppId) and relaunches Open Forge.
Portable/dev copy → the installer opens normally so the user can pick a folder.
"""
import json
import subprocess
import sys
import tempfile
import time
import urllib.request
from pathlib import Path

import requests

from core.uninstall import uninstaller, is_frozen

RELEASES_API = "https://api.github.com/repos/Dimonchikk/Open-Forge/releases/latest"
ASSET_NAME = "OpenForge-setup.exe"
DETACHED_PROCESS = 0x00000008


def latest_release(current_version: str) -> dict:
    req = urllib.request.Request(RELEASES_API, headers={"User-Agent": "OpenForge/" + current_version})
    with urllib.request.urlopen(req, timeout=8) as r:
        data = json.loads(r.read())
    asset = next((a for a in data.get("assets", []) if a.get("name") == ASSET_NAME), None)
    return {
        "version": data.get("tag_name", "").lstrip("vV"),
        "url": data.get("html_url", ""),
        "notes": data.get("body", "") or "",
        "asset_url": asset.get("browser_download_url") if asset else None,
        "asset_size": asset.get("size", 0) if asset else 0,
    }


def newer(a: str, b: str) -> bool:
    def parts(v):
        return [int(x) if x.isdigit() else 0 for x in v.split(".")]
    return parts(a) > parts(b)


def can_auto_update(info: dict) -> bool:
    return sys.platform == "win32" and bool(info.get("asset_url"))


def download(info: dict, progress, stop) -> Path | None:
    dest = Path(tempfile.gettempdir()) / f"OpenForge-setup-{info['version']}.exe"
    t0, done = time.time(), 0
    with requests.get(info["asset_url"], stream=True, timeout=(10, 120)) as r:
        r.raise_for_status()
        total = int(r.headers.get("Content-Length", 0)) or info.get("asset_size", 0)
        with open(dest, "wb") as f:
            for chunk in r.iter_content(256 * 1024):
                if stop.is_set():
                    f.close()
                    dest.unlink(missing_ok=True)
                    return None
                f.write(chunk)
                done += len(chunk)
                progress(done, total, done / max(time.time() - t0, 0.001))
    return dest


def launch_installer(installer: Path) -> bool:
    """Start the installer detached. Returns True if it runs silently (app should quit now)."""
    silent = is_frozen() and uninstaller() is not None
    args = f'"{installer}"'
    if silent:
        args += " /SILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS"
    subprocess.Popen(args, creationflags=DETACHED_PROCESS, close_fds=True)
    return silent
