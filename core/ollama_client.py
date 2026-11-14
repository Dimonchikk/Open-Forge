"""Ollama HTTP client — all communication with the local Ollama server."""
import json
import threading
from typing import Callable, Generator
import requests

OLLAMA_BASE = "http://127.0.0.1:11434"
OLLAMA_DOWNLOAD_URL = "https://ollama.com/download"


def is_ollama_running() -> bool:
    try:
        return requests.get(f"{OLLAMA_BASE}/api/tags", timeout=2).status_code == 200
    except Exception:
        return False


def list_local_models() -> list[dict]:
    """Models already downloaded: [{name, size (bytes), ...}]."""
    try:
        r = requests.get(f"{OLLAMA_BASE}/api/tags", timeout=5)
        r.raise_for_status()
        models = r.json().get("models", [])
        return sorted(models, key=lambda m: m.get("name", "").lower())
    except Exception:
        return []


def normalize(name: str) -> str:
    """'qwen3:latest' and 'qwen3' are the same model; compare case-insensitively."""
    name = name.strip().lower()
    return name[:-7] if name.endswith(":latest") else name


def _error_text(resp: requests.Response) -> str:
    try:
        return resp.json().get("error") or resp.text
    except Exception:
        return resp.text or f"HTTP {resp.status_code}"


def chat_stream(model: str, messages: list[dict], system: str = "",
                stats: dict | None = None,
                on_thinking: Callable[[str], None] | None = None) -> Generator[str, None, None]:
    """Yield answer tokens. Fills `stats` with eval_count / eval_duration (ns) at the end.

    Ollama's /api/chat has no `system` field — the system prompt must be the
    first message with role "system".
    """
    msgs = ([{"role": "system", "content": system}] if system else []) + messages
    payload = {"model": model, "messages": msgs, "stream": True}

    # connect fast; allow long gaps while a big model loads into memory
    with requests.post(f"{OLLAMA_BASE}/api/chat", json=payload, stream=True,
                       timeout=(5, 900)) as resp:
        if resp.status_code != 200:
            raise RuntimeError(_error_text(resp))
        for line in resp.iter_lines():
            if not line:
                continue
            data = json.loads(line)
            if data.get("error"):
                raise RuntimeError(data["error"])
            msg = data.get("message", {})
            if msg.get("thinking") and on_thinking:
                on_thinking(msg["thinking"])
            if msg.get("content"):
                yield msg["content"]
            if data.get("done"):
                if stats is not None:
                    stats["eval_count"] = data.get("eval_count", 0)
                    stats["eval_duration"] = data.get("eval_duration", 0)
                return


def tokens_per_second(stats: dict) -> float:
    dur = stats.get("eval_duration") or 0
    return stats.get("eval_count", 0) / (dur / 1e9) if dur else 0.0


def pull_model(model_name: str, progress_cb: Callable[[str, int, int], None],
               stop_event: threading.Event):
    """Download a model. progress_cb(status, completed_bytes, total_bytes)."""
    with requests.post(f"{OLLAMA_BASE}/api/pull", json={"model": model_name, "stream": True},
                       stream=True, timeout=(5, 3600)) as resp:
        if resp.status_code != 200:
            raise RuntimeError(_error_text(resp))
        for line in resp.iter_lines():
            if stop_event.is_set():
                return False
            if not line:
                continue
            data = json.loads(line)
            if data.get("error"):
                raise RuntimeError(data["error"])
            progress_cb(data.get("status", ""), data.get("completed", 0), data.get("total", 0))
    return True


def delete_model(model_name: str) -> bool:
    try:
        r = requests.delete(f"{OLLAMA_BASE}/api/delete", json={"model": model_name}, timeout=30)
        return r.status_code == 200
    except Exception:
        return False
