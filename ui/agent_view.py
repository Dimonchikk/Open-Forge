"""Agent mode tab: pick model, set project folder, choose permission level."""
import threading
import tkinter as tk
from pathlib import Path
import customtkinter as ctk
from tkinter import filedialog, messagebox
from ui.theme import c, RADIUS
from core.ollama_client import list_local_models
from agent.agent_core import AgentRunner, PermissionLevel


class AgentView(ctk.CTkFrame):
    def __init__(self, master, **kw):
        super().__init__(master, fg_color=c("bg"), **kw)
        self._runner: AgentRunner | None = None
        self._stop_event = threading.Event()
        self._build_ui()

    def _build_ui(self):
        # title
        ctk.CTkLabel(
            self,
            text="Агент",
            font=ctk.CTkFont(family="Inter", size=20, weight="bold"),
            text_color=c("text"),
        ).pack(anchor="w", padx=20, pady=(16, 8))

        config = ctk.CTkFrame(self, fg_color=c("card"), corner_radius=RADIUS["card"])
        config.pack(fill="x", padx=20, pady=(0, 10))

        # model selector
        row1 = ctk.CTkFrame(config, fg_color="transparent")
        row1.pack(fill="x", padx=16, pady=(12, 0))
        ctk.CTkLabel(row1, text="Модель:", font=ctk.CTkFont(family="Inter", size=12),
                     text_color=c("text_dim")).pack(side="left")
        local = [m["name"] for m in list_local_models()]
        if not local:
            local = ["(немає локальних моделей)"]
        self._model_sel = ctk.CTkOptionMenu(
            row1, values=local, width=260,
            corner_radius=RADIUS["button"],
            font=ctk.CTkFont(family="Inter", size=12),
        )
        self._model_sel.pack(side="left", padx=(10, 0))

        # project folder
        row2 = ctk.CTkFrame(config, fg_color="transparent")
        row2.pack(fill="x", padx=16, pady=(10, 0))
        ctk.CTkLabel(row2, text="Папка:", font=ctk.CTkFont(family="Inter", size=12),
                     text_color=c("text_dim")).pack(side="left")
        self._folder_var = ctk.StringVar(value=str(Path.home() / "Desktop" / "OpenForgeProject"))
        folder_entry = ctk.CTkEntry(
            row2, textvariable=self._folder_var, width=280,
            fg_color=c("input_bg"), border_color=c("border"),
            corner_radius=RADIUS["input"], font=ctk.CTkFont(family="Inter", size=12),
        )
        folder_entry.pack(side="left", padx=(10, 0))
        ctk.CTkButton(
            row2, text="…", width=36,
            corner_radius=RADIUS["button"],
            command=self._browse_folder,
        ).pack(side="left", padx=(6, 0))

        # permission level
        row3 = ctk.CTkFrame(config, fg_color="transparent")
        row3.pack(fill="x", padx=16, pady=(10, 12))
        ctk.CTkLabel(row3, text="Дозволи:", font=ctk.CTkFont(family="Inter", size=12),
                     text_color=c("text_dim")).pack(side="left")
        self._perm_var = ctk.StringVar(value="auto")
        seg = ctk.CTkSegmentedButton(
            row3,
            values=["Ручний", "Авто", "Повний доступ"],
            variable=ctk.StringVar(value="Авто"),
            command=self._on_perm_change,
            width=320,
            corner_radius=RADIUS["button"],
        )
        seg.pack(side="left", padx=(10, 0))

        self._perm_hint = ctk.CTkLabel(
            config,
            text="Авто: підтверджує лише видалення й запуск команд.",
            font=ctk.CTkFont(family="Inter", size=11),
            text_color=c("text_dim"),
        )
        self._perm_hint.pack(anchor="w", padx=16, pady=(0, 12))

        # danger badge for FULL
        self._danger_badge = ctk.CTkLabel(
            config,
            text="⚠️  Повний доступ — модель діє без підтвердження",
            font=ctk.CTkFont(family="Inter", size=11),
            fg_color=c("warning"),
            text_color="#FFFFFF",
            corner_radius=RADIUS["badge"],
        )

        # output box
        self._output = ctk.CTkTextbox(
            self,
            fg_color=c("panel"),
            font=ctk.CTkFont(family="Courier New", size=12),
            wrap="word",
            state="disabled",
            corner_radius=RADIUS["card"],
        )
        self._output.pack(fill="both", expand=True, padx=20, pady=(0, 0))

        # input + run
        inp_frame = ctk.CTkFrame(self, fg_color="transparent")
        inp_frame.pack(fill="x", padx=20, pady=12)

        self._task_input = ctk.CTkEntry(
            inp_frame,
            placeholder_text="Опишіть завдання… наприклад: Зроби Flask-застосунок hello world",
            fg_color=c("input_bg"),
            border_color=c("border"),
            corner_radius=RADIUS["input"],
            font=ctk.CTkFont(family="Inter", size=13),
        )
        self._task_input.pack(side="left", fill="x", expand=True)
        self._task_input.bind("<Return>", self._run_agent)

        ctk.CTkButton(
            inp_frame,
            text="Запустити",
            width=110,
            height=38,
            corner_radius=RADIUS["button"],
            command=self._run_agent,
        ).pack(side="left", padx=(8, 0))

        ctk.CTkButton(
            inp_frame,
            text="⏹ Зупинити",
            width=100,
            height=38,
            fg_color=c("danger"),
            hover_color="#CC2F27",
            corner_radius=RADIUS["button"],
            command=self._stop_agent,
        ).pack(side="left", padx=(6, 0))

    # ── callbacks ─────────────────────────────────────────────────────────────

    def _browse_folder(self):
        path = filedialog.askdirectory()
        if path:
            self._folder_var.set(path)

    _PERM_MAP = {
        "Ручний": PermissionLevel.MANUAL,
        "Авто": PermissionLevel.AUTO,
        "Повний доступ": PermissionLevel.FULL,
    }
    _PERM_HINTS = {
        "Ручний": "Ручний: підтверджує кожну дію.",
        "Авто": "Авто: підтверджує лише видалення й запуск команд.",
        "Повний доступ": "Повний доступ: без підтвердження.",
    }

    def _on_perm_change(self, value: str):
        self._perm_var.set(self._PERM_MAP[value].value)
        self._perm_hint.configure(text=self._PERM_HINTS[value])
        if value == "Повний доступ":
            self._danger_badge.pack(fill="x", padx=16, pady=(0, 12))
        else:
            self._danger_badge.pack_forget()

    def _run_agent(self, event=None):
        task = self._task_input.get().strip()
        if not task:
            return
        model = self._model_sel.get()
        if "немає" in model:
            return
        folder = self._folder_var.get()
        perm = PermissionLevel(self._perm_var.get())

        self._task_input.delete(0, "end")
        self._stop_event.clear()
        self._append_output(f"▶ Завдання: {task}\n📁 Папка: {folder}\n\n")

        self._runner = AgentRunner(
            model=model,
            project_folder=folder,
            permission=perm,
            confirm_cb=self._confirm_action,
            output_cb=lambda t: self.after(0, self._append_output, t),
            stop_event=self._stop_event,
        )
        threading.Thread(target=self._runner.run, args=(task,), daemon=True).start()

    def _stop_agent(self):
        self._stop_event.set()

    def _confirm_action(self, tool_name: str, description: str) -> bool:
        return messagebox.askyesno(
            "Підтвердження",
            f"Дозволити дію?\n\n{description}",
            parent=self,
        )

    def _append_output(self, text: str):
        self._output.configure(state="normal")
        self._output.insert("end", text)
        self._output.see("end")
        self._output.configure(state="disabled")
