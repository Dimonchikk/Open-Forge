"""
Agent mode: lets the model create/read files and run commands.
Permission levels:
  MANUAL   – confirm every action
  AUTO     – confirm only dangerous actions (delete, run)
  FULL     – no confirmation needed
"""
import os
import subprocess
import threading
from enum import Enum
from pathlib import Path
from typing import Callable

import requests

OLLAMA_BASE = "http://127.0.0.1:11434"

DANGEROUS_TOOLS = {"run_command", "delete_file"}


class PermissionLevel(str, Enum):
    MANUAL = "manual"
    AUTO = "auto"
    FULL = "full"


TOOLS_SCHEMA = [
    {
        "type": "function",
        "function": {
            "name": "create_file",
            "description": "Create or overwrite a file with given content.",
            "parameters": {
                "type": "object",
                "properties": {
                    "path": {"type": "string", "description": "Relative path inside the project folder."},
                    "content": {"type": "string", "description": "File content."},
                },
                "required": ["path", "content"],
            },
        },
    },
    {
        "type": "function",
        "function": {
            "name": "read_file",
            "description": "Read the content of a file.",
            "parameters": {
                "type": "object",
                "properties": {
                    "path": {"type": "string"},
                },
                "required": ["path"],
            },
        },
    },
    {
        "type": "function",
        "function": {
            "name": "list_files",
            "description": "List files in the project folder.",
            "parameters": {
                "type": "object",
                "properties": {
                    "subdir": {"type": "string", "description": "Subdirectory to list (optional)."},
                },
            },
        },
    },
    {
        "type": "function",
        "function": {
            "name": "run_command",
            "description": "Run a shell command inside the project folder. DANGEROUS.",
            "parameters": {
                "type": "object",
                "properties": {
                    "command": {"type": "string"},
                },
                "required": ["command"],
            },
        },
    },
    {
        "type": "function",
        "function": {
            "name": "delete_file",
            "description": "Delete a file from the project folder. DANGEROUS.",
            "parameters": {
                "type": "object",
                "properties": {
                    "path": {"type": "string"},
                },
                "required": ["path"],
            },
        },
    },
]


class AgentRunner:
    def __init__(
        self,
        model: str,
        project_folder: str,
        permission: PermissionLevel,
        confirm_cb: Callable[[str, str], bool],   # (tool_name, description) → True/False
        output_cb: Callable[[str], None],          # streamed text
        stop_event: threading.Event,
    ):
        self.model = model
        self.project_folder = Path(project_folder)
        self.project_folder.mkdir(parents=True, exist_ok=True)
        self.permission = permission
        self.confirm_cb = confirm_cb
        self.output_cb = output_cb
        self.stop_event = stop_event
        self.messages: list[dict] = []

    # ── permission check ──────────────────────────────────────────────────────

    def _needs_confirm(self, tool_name: str) -> bool:
        if self.permission == PermissionLevel.FULL:
            return False
        if self.permission == PermissionLevel.MANUAL:
            return True
        # AUTO: only dangerous tools need confirm
        return tool_name in DANGEROUS_TOOLS

    # ── tool execution ────────────────────────────────────────────────────────

    def _safe_path(self, rel_path: str) -> Path:
        """Resolve path and ensure it stays inside project_folder."""
        p = (self.project_folder / rel_path).resolve()
        if not str(p).startswith(str(self.project_folder.resolve())):
            raise PermissionError(f"Path '{rel_path}' escapes project folder.")
        return p

    def _execute_tool(self, name: str, args: dict) -> str:
        try:
            if name == "create_file":
                p = self._safe_path(args["path"])
                p.parent.mkdir(parents=True, exist_ok=True)
                p.write_text(args["content"], encoding="utf-8")
                return f"✅ Файл створено: {args['path']}"

            elif name == "read_file":
                p = self._safe_path(args["path"])
                return p.read_text(encoding="utf-8")

            elif name == "list_files":
                subdir = args.get("subdir", "")
                base = self._safe_path(subdir) if subdir else self.project_folder
                files = [str(f.relative_to(self.project_folder)) for f in base.rglob("*") if f.is_file()]
                return "\n".join(files) or "(порожньо)"

            elif name == "run_command":
                result = subprocess.run(
                    args["command"],
                    shell=True,
                    cwd=self.project_folder,
                    capture_output=True,
                    text=True,
                    timeout=60,
                )
                out = result.stdout + result.stderr
                return out or "(без виводу)"

            elif name == "delete_file":
                p = self._safe_path(args["path"])
                p.unlink()
                return f"🗑️ Видалено: {args['path']}"

        except Exception as e:
            return f"❌ Помилка: {e}"

        return "❓ Невідомий інструмент"

    # ── main loop ─────────────────────────────────────────────────────────────

    def run(self, task: str):
        self.messages = [{"role": "user", "content": task}]

        system = (
            "You are a software-building agent. "
            "Use the provided tools to create files and run commands inside the project folder. "
            "Think step by step. When done, say DONE."
        )

        for _ in range(30):  # max 30 iterations
            if self.stop_event.is_set():
                self.output_cb("\n⛔ Зупинено користувачем.")
                return

            payload = {
                "model": self.model,
                "messages": self.messages,
                "tools": TOOLS_SCHEMA,
                "stream": False,
            }
            if system:
                payload["system"] = system
                system = ""  # only first turn

            resp = requests.post(f"{OLLAMA_BASE}/api/chat", json=payload, timeout=120)
            resp.raise_for_status()
            data = resp.json()
            msg = data.get("message", {})
            self.messages.append(msg)

            content = msg.get("content", "")
            tool_calls = msg.get("tool_calls", [])

            if content:
                self.output_cb(content)

            if not tool_calls:
                return  # model finished

            # process tool calls
            for call in tool_calls:
                if self.stop_event.is_set():
                    return
                name = call["function"]["name"]
                args = call["function"].get("arguments", {})
                if isinstance(args, str):
                    import json
                    args = json.loads(args)

                desc = f"{name}({', '.join(f'{k}={v!r}' for k, v in args.items())})"
                self.output_cb(f"\n🔧 Інструмент: `{desc}`")

                if self._needs_confirm(name):
                    allowed = self.confirm_cb(name, desc)
                    if not allowed:
                        result = "❌ Відхилено користувачем."
                    else:
                        result = self._execute_tool(name, args)
                else:
                    result = self._execute_tool(name, args)

                self.output_cb(f"\n→ {result}\n")
                self.messages.append({
                    "role": "tool",
                    "content": result,
                })
