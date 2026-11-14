"""
Agent: the model works with files inside one folder through Ollama tool calling.

Permission levels:
  MANUAL – confirm every action
  AUTO   – confirm only dangerous actions (delete, run command)
  FULL   – no confirmation
"""
import json
import subprocess
import sys
import threading
from enum import Enum
from pathlib import Path
from typing import Callable

import requests

OLLAMA_BASE = "http://127.0.0.1:11434"
DANGEROUS_TOOLS = {"run_command", "delete_file"}
MAX_STEPS = 30


class PermissionLevel(str, Enum):
    MANUAL = "manual"
    AUTO = "auto"
    FULL = "full"


class ToolsUnsupported(RuntimeError):
    """The chosen model can't call tools, so it can't work as an agent."""


def _tool(name, desc, props, required=()):
    return {"type": "function", "function": {
        "name": name, "description": desc,
        "parameters": {"type": "object", "properties": props, "required": list(required)}}}


TOOLS_SCHEMA = [
    _tool("create_file", "Create or overwrite a text file in the project folder.",
          {"path": {"type": "string", "description": "Path relative to the project folder."},
           "content": {"type": "string", "description": "Full file content."}},
          ("path", "content")),
    _tool("read_file", "Read a text file from the project folder.",
          {"path": {"type": "string"}}, ("path",)),
    _tool("list_files", "List files in the project folder (or a subfolder).",
          {"subdir": {"type": "string", "description": "Optional subfolder."}}),
    _tool("run_command", "Run a shell command in the project folder (Windows cmd).",
          {"command": {"type": "string"}}, ("command",)),
    _tool("delete_file", "Delete a file from the project folder.",
          {"path": {"type": "string"}}, ("path",)),
]

SYSTEM = (
    "You are a coding agent working on the user's computer inside ONE project folder. "
    "Use the tools to actually create and edit files and run commands — do not just print code. "
    "Paths are relative to the project folder. Work step by step, check results, and when the "
    "task is finished reply with a short summary of what you did, in the user's language."
)


def describe(name: str, args: dict) -> str:
    """Human-readable step title (Ukrainian)."""
    p = args.get("path", "")
    return {
        "create_file": f"Створює файл {p}",
        "read_file": f"Читає {p}",
        "list_files": "Дивиться файли" + (f" у {args['subdir']}" if args.get("subdir") else " в папці"),
        "run_command": f"Виконує команду: {args.get('command', '')}",
        "delete_file": f"Видаляє {p}",
    }.get(name, name)


class AgentRunner:
    def __init__(self, model: str, project_folder: str, permission: PermissionLevel,
                 confirm_cb: Callable[[str, str], bool],
                 event_cb: Callable[[str, object], None],
                 stop_event: threading.Event,
                 history: list[dict] | None = None, instruction: str = ""):
        """event_cb(kind, payload): ("text", str) | ("step", title) | ("result", (ok, text))."""
        self.model = model
        self.root = Path(project_folder).expanduser().resolve()
        self.root.mkdir(parents=True, exist_ok=True)
        self.permission = permission
        self.confirm_cb = confirm_cb
        self.emit = event_cb
        self.stop_event = stop_event
        self.history = list(history or [])
        self.instruction = instruction
        self.changed: list[str] = []

    # ── safety ────────────────────────────────────────────────────────────────

    def _needs_confirm(self, name: str) -> bool:
        if self.permission == PermissionLevel.FULL:
            return False
        if self.permission == PermissionLevel.MANUAL:
            return True
        return name in DANGEROUS_TOOLS

    def _safe_path(self, rel: str) -> Path:
        p = (self.root / rel).resolve()
        try:
            p.relative_to(self.root)        # a plain string prefix check would allow "Project2"
        except ValueError:
            raise PermissionError(f"шлях «{rel}» виходить за межі папки проєкту") from None
        return p

    # ── tools ─────────────────────────────────────────────────────────────────

    def _execute(self, name: str, args: dict) -> tuple[bool, str]:
        try:
            if name == "create_file":
                p = self._safe_path(args["path"])
                p.parent.mkdir(parents=True, exist_ok=True)
                content = args.get("content", "")
                p.write_text(content, encoding="utf-8")
                self.changed.append(args["path"])
                return True, f"створено ({len(content.encode('utf-8'))} байт)"
            if name == "read_file":
                text = self._safe_path(args["path"]).read_text(encoding="utf-8", errors="replace")
                return True, text[:20000]
            if name == "list_files":
                base = self._safe_path(args["subdir"]) if args.get("subdir") else self.root
                files = [str(f.relative_to(self.root)) for f in base.rglob("*") if f.is_file()]
                return True, "\n".join(files[:500]) or "(порожньо)"
            if name == "run_command":
                kw = {"creationflags": 0x08000000} if sys.platform == "win32" else {}
                r = subprocess.run(args["command"], shell=True, cwd=self.root, capture_output=True,
                                   text=True, errors="replace", timeout=120, **kw)
                out = (r.stdout + r.stderr).strip()
                return r.returncode == 0, (out[-6000:] or "(без виводу)") + f"\n[код виходу {r.returncode}]"
            if name == "delete_file":
                self._safe_path(args["path"]).unlink()
                self.changed.append(args["path"])
                return True, "видалено"
        except subprocess.TimeoutExpired:
            return False, "команда працювала довше 2 хвилин і була зупинена"
        except Exception as e:  # noqa: BLE001
            return False, f"помилка: {e}"
        return False, "невідомий інструмент"

    # ── loop ──────────────────────────────────────────────────────────────────

    def run(self, task: str):
        system = SYSTEM + f"\nProject folder: {self.root}"
        if self.instruction:
            system += "\n\nUser instruction: " + self.instruction
        messages = [{"role": "system", "content": system}] + self.history + \
                   [{"role": "user", "content": task}]

        for _ in range(MAX_STEPS):
            if self.stop_event.is_set():
                return
            resp = requests.post(f"{OLLAMA_BASE}/api/chat", timeout=(5, 900), json={
                "model": self.model, "messages": messages, "tools": TOOLS_SCHEMA, "stream": False})
            if resp.status_code != 200:
                try:
                    err = resp.json().get("error", resp.text)
                except Exception:
                    err = resp.text
                if "does not support tools" in err:
                    raise ToolsUnsupported(err)
                raise RuntimeError(err)
            msg = resp.json().get("message", {})
            messages.append(msg)

            if msg.get("content", "").strip():
                self.emit("text", msg["content"])
            calls = msg.get("tool_calls") or []
            if not calls:
                return

            for call in calls:
                if self.stop_event.is_set():
                    return
                fn = call.get("function", {})
                name = fn.get("name", "")
                args = fn.get("arguments") or {}
                if isinstance(args, str):
                    try:
                        args = json.loads(args)
                    except json.JSONDecodeError:
                        args = {}
                title = describe(name, args)
                self.emit("step", title)
                if self._needs_confirm(name) and not self.confirm_cb(name, title):
                    ok, result = False, "користувач не дозволив цю дію"
                else:
                    ok, result = self._execute(name, args)
                self.emit("result", (ok, result))
                messages.append({"role": "tool", "content": result, "tool_name": name})
        self.emit("text", "Зупинився: досягнуто ліміту кроків.")
