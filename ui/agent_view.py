"""Agent mode: the model creates files and runs commands inside one folder."""
import threading
from pathlib import Path
from tkinter import filedialog
import customtkinter as ctk

from agent.agent_core import AgentRunner, PermissionLevel
from core import settings
from ui import bg
from ui.dialogs import confirm
from ui.icons import icon
from ui.theme import (c, font, RADIUS, primary_button, ghost_button, option_menu, segmented,
                      page_header)

PERMS = {
    "Питати завжди": (PermissionLevel.MANUAL, "Кожну дію агента ти підтверджуєш сам.", "text_dim"),
    "Авто": (PermissionLevel.AUTO,
             "Файли створює сам, а видалення й запуск команд — лише з твого дозволу.", "text_dim"),
    "Повний доступ": (PermissionLevel.FULL,
                      "Агент діє без підтверджень, включно з командами. Вмикай лише для "
                      "папок, які не шкода.", "warning"),
}


class AgentView(ctk.CTkFrame):
    def __init__(self, master, app, **kw):
        super().__init__(master, fg_color=c("bg"), corner_radius=0, **kw)
        self.app = app
        self._stop = threading.Event()
        self._running = False
        self._perm = "Авто"
        self._build()

    def _build(self):
        self.grid_columnconfigure(0, weight=1)
        self.grid_rowconfigure(2, weight=1)
        page_header(self, "Агент",
                    "Опиши задачу — модель сама створить файли й виконає команди у вибраній папці."
                    ).grid(row=0, column=0, sticky="w", padx=28, pady=(22, 14))

        cfg = ctk.CTkFrame(self, fg_color=c("card"), corner_radius=RADIUS["card"],
                           border_width=1, border_color=c("border"))
        cfg.grid(row=1, column=0, sticky="ew", padx=28)
        cfg.grid_columnconfigure(1, weight=1)

        def label(row, text):
            ctk.CTkLabel(cfg, text=text, font=font(13, "bold"), text_color=c("text_dim"),
                         anchor="w").grid(row=row, column=0, sticky="w", padx=(18, 14),
                                          pady=(14 if row == 0 else 10, 0))

        label(0, "Модель")
        self._model = option_menu(cfg, ["—"], width=320)
        self._model.grid(row=0, column=1, sticky="w", pady=(14, 0))

        label(1, "Папка")
        frow = ctk.CTkFrame(cfg, fg_color="transparent")
        frow.grid(row=1, column=1, sticky="ew", pady=(10, 0), padx=(0, 18))
        frow.grid_columnconfigure(0, weight=1)
        default = settings.get("agent_folder") or str(Path.home() / "Desktop" / "OpenForgeProject")
        self._folder = ctk.StringVar(value=default)
        ctk.CTkEntry(frow, textvariable=self._folder, height=36, font=font(13),
                     fg_color=c("card_alt"), border_width=0, text_color=c("text"),
                     corner_radius=RADIUS["button"]).grid(row=0, column=0, sticky="ew")
        ghost_button(frow, "Обрати…", width=110, height=36,
                     image=icon("folder", c("text_dim"), 15), compound="left",
                     command=self._browse).grid(row=0, column=1, padx=(8, 0))

        label(2, "Дозволи")
        seg = segmented(cfg, list(PERMS), command=self._on_perm)
        seg.set(self._perm)
        seg.grid(row=2, column=1, sticky="w", pady=(10, 0))
        self._hint = ctk.CTkLabel(cfg, text="", font=font(12), anchor="w", justify="left")
        self._hint.grid(row=3, column=1, sticky="w", pady=(6, 16), padx=(0, 18))
        self._on_perm(self._perm)

        log_card = ctk.CTkFrame(self, fg_color=c("card"), corner_radius=RADIUS["card"],
                                border_width=1, border_color=c("border"))
        log_card.grid(row=2, column=0, sticky="nsew", padx=28, pady=(14, 0))
        log_card.grid_columnconfigure(0, weight=1)
        log_card.grid_rowconfigure(0, weight=1)
        self._log = ctk.CTkTextbox(log_card, font=font(13, mono=True), wrap="word",
                                   fg_color="transparent", text_color=c("text"), border_width=0)
        self._log.grid(row=0, column=0, sticky="nsew", padx=10, pady=10)
        self._log.insert("1.0", "Тут з'являтиметься, що робить агент: які файли створює "
                                "і які команди запускає.\n\nПриклад задачі: «Зроби сайт-візитку "
                                "з index.html і style.css».")
        self._log.configure(state="disabled", text_color=c("text_faint"))
        self._fresh = True

        bar = ctk.CTkFrame(self, fg_color=c("card"), corner_radius=20, border_width=1,
                           border_color=c("border"))
        bar.grid(row=3, column=0, sticky="ew", padx=28, pady=(14, 18))
        bar.grid_columnconfigure(0, weight=1)
        self._task = ctk.CTkEntry(bar, height=40, border_width=0, fg_color="transparent",
                                  font=font(14), text_color=c("text"),
                                  placeholder_text="Опиши задачу для агента…")
        self._task.grid(row=0, column=0, sticky="ew", padx=(14, 8), pady=6)
        self._task.bind("<Return>", lambda e: self._go())
        self._btn = primary_button(bar, "Запустити", width=140, height=36, corner_radius=18,
                                   image=icon("agent", "#FFFFFF", 15), compound="left",
                                   command=self._go)
        self._btn.grid(row=0, column=1, padx=(0, 8), pady=6)

    # ── data ──────────────────────────────────────────────────────────────────

    def set_models(self, names: list[str], ollama_ok: bool):
        vals = names or (["Ollama не запущена"] if not ollama_ok else ["Немає моделей"])
        cur = self._model.get()
        self._model.configure(values=vals, state="normal" if names else "disabled")
        self._model.set(cur if cur in names else (settings.get("model") if
                                                   settings.get("model") in names else vals[0]))

    # ── ui callbacks ──────────────────────────────────────────────────────────

    def _browse(self):
        path = filedialog.askdirectory(initialdir=self._folder.get())
        if path:
            self._folder.set(path)
            settings.set("agent_folder", path)

    def _on_perm(self, value):
        self._perm = value
        _, hint, color = PERMS[value]
        self._hint.configure(text=hint, text_color=c(color))

    def _write(self, text: str):
        self._log.configure(state="normal")
        if self._fresh:
            self._log.delete("1.0", "end")
            self._log.configure(text_color=c("text"))
            self._fresh = False
        self._log.insert("end", text)
        self._log.see("end")
        self._log.configure(state="disabled")

    # ── run ───────────────────────────────────────────────────────────────────

    def _go(self):
        if self._running:
            self._stop.set()
            return
        task = self._task.get().strip()
        model = self._model.get()
        if not task or model.startswith(("Немає", "Ollama", "—")):
            return
        folder = self._folder.get().strip()
        settings.set("agent_folder", folder)
        self._task.delete(0, "end")
        self._stop.clear()
        self._running = True
        self._btn.configure(text="Зупинити", image=icon("stop", "#FFFFFF", 15),
                            fg_color=c("text"), hover_color=c("text_dim"))
        self._write(f"\n▶ {task}\n   папка: {folder}\n\n")

        def work():
            runner = AgentRunner(model=model, project_folder=folder,
                                 permission=PERMS[self._perm][0],
                                 confirm_cb=self._ask, output_cb=lambda t: bg.ui(self._write, t),
                                 stop_event=self._stop)
            runner.run(task)

        bg.run(work, on_done=lambda _: self._done(None), on_error=lambda e: self._done(str(e)))

    def _done(self, err):
        if err:
            self._write(f"\n✖ Помилка: {err}\n")
        self._write("\n— готово —\n")
        self._running = False
        self._btn.configure(text="Запустити", image=icon("agent", "#FFFFFF", 15),
                            fg_color=c("accent"), hover_color=c("accent_hover"))

    def _ask(self, tool_name: str, description: str) -> bool:
        """Called from the agent thread: show an in-app dialog and wait for the answer."""
        answer = {"ok": False}
        ev = threading.Event()

        def show():
            def result(ok):
                answer["ok"] = ok
                ev.set()
            confirm(self, "Дозволити дію агента?", description, "Дозволити", result,
                    danger=tool_name in ("run_command", "delete_file"), cancel_text="Заборонити")

        bg.ui(show)
        while not ev.wait(0.2):
            if self._stop.is_set():
                return False
        return answer["ok"]
