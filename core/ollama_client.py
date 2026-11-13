"""Ollama API client — all communication with the local Ollama server."""
import json
import threading
import time
from typing import Callable, Generator
import requests

OLLAMA_BASE = "http://127.0.0.1:11434"


def is_ollama_running() -> bool:
    try:
        r = requests.get(f"{OLLAMA_BASE}/api/tags", timeout=2)
        return r.status_code == 200
    except Exception:
        return False


def list_local_models() -> list[dict]:
    """Return models already downloaded on this machine."""
    try:
        r = requests.get(f"{OLLAMA_BASE}/api/tags", timeout=5)
        r.raise_for_status()
        return r.json().get("models", [])
    except Exception:
        return []


def chat_stream(model: str, messages: list[dict], system: str = "") -> Generator[str, None, dict]:
    """
    Stream a chat response token by token.
    Yields str tokens; final StopIteration value is stats dict
    with keys: eval_count, eval_duration → tokens/sec computed by caller.
    """
    payload = {
        "model": model,
        "messages": messages,
        "stream": True,
    }
    if system:
        payload["system"] = system

    with requests.post(
        f"{OLLAMA_BASE}/api/chat",
        json=payload,
        stream=True,
        timeout=120,
    ) as resp:
        resp.raise_for_status()
        stats = {}
        for line in resp.iter_lines():
            if not line:
                continue
            data = json.loads(line)
            if data.get("done"):
                stats = {
                    "eval_count": data.get("eval_count", 0),
                    "eval_duration": data.get("eval_duration", 1),
                }
                return stats
            token = data.get("message", {}).get("content", "")
            if token:
                yield token
    return stats


def pull_model(model_name: str, progress_cb: Callable[[str, float], None], stop_event: threading.Event):
    """
    Download a model from Ollama library.
    progress_cb(status_str, percent_0_to_100) is called repeatedly.
    """
    with requests.post(
        f"{OLLAMA_BASE}/api/pull",
        json={"name": model_name, "stream": True},
        stream=True,
        timeout=3600,
    ) as resp:
        resp.raise_for_status()
        for line in resp.iter_lines():
            if stop_event.is_set():
                break
            if not line:
                continue
            data = json.loads(line)
            status = data.get("status", "")
            total = data.get("total", 0)
            completed = data.get("completed", 0)
            pct = (completed / total * 100) if total else 0
            progress_cb(status, pct)


def delete_model(model_name: str) -> bool:
    try:
        r = requests.delete(f"{OLLAMA_BASE}/api/delete", json={"name": model_name}, timeout=10)
        return r.status_code == 200
    except Exception:
        return False
