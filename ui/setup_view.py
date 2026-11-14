"""First-run screen: install / start Ollama with a chosen folder for the program and models."""
import sys
import threading
import webbrowser
from pathlib import Path
from tkinter import filedialog
import customtkinter as ctk

from core import ollama_setup as osu
from core.ollama_client import OLLAMA_DOWNLOAD_URL
from ui import bg
from ui.icons import icon, logo
from ui.theme import c, font, RADIUS, primary_button, ghost_button


class SetupView(ctk.CTkFrame):
    def __init__(self, master, app, **kw):
        super().__init__(master, fg_color=c("bg"), corner_radius=0, **kw)
        self.app = app
        self._stop = threading.Event()
        self._busy = False
        self.grid_columnconfigure(0, weight=1)
        self.grid_rowconfigure(0, weight=1)
        self._card = ctk.CTkFrame(self, fg_color=c("card"), corner_radius=20, width=620,
                                  border_width=1, border_color=c("border"))
        self._card.grid(row=0, column=0)
        self._ollama_dir, self._models_dir = osu.default_dirs()

    def on_show(self):
        if not self._busy:
            bg.run(osu.status, on_done=self._render)

    # ── states ────────────────────────────────────────────────────────────────

    def _clear(self):
        for w in self._card.winfo_children():
            w.destroy()

    def _head(self, title: str, text: str):
        ctk.CTkLabel(self._card, text="", image=logo(52)).pack(pady=(28, 10))
        ctk.CTkLabel(self._card, text=title, font=font(22, "bold"),
                     text_color=c("text")).pack(padx=40)
        ctk.CTkLabel(self._card, text=text, font=font(13), text_color=c("text_dim"),
                     justify="center", wraplength=520).pack(padx=40, pady=(6, 18))

    def _render(self, state: str):
        self._clear()
        if state == osu.RUNNING:
            self._head("Ollama працює", "Усе готово. Обери модель — ми позначили ті, що швидко "
                                        "працюватимуть на твоєму ПК.")
            primary_button(self._card, "Обрати модель", width=200, height=40,
                           image=icon("models", "#FFFFFF", 16), compound="left",
                           command=lambda: self.app.show("models")).pack(pady=(0, 28))
            return

        if sys.platform != "win32":
            self._head("Потрібна Ollama", "Автоматичне встановлення працює лише у Windows. "
                                          "Встанови Ollama з офіційного сайту.")
            primary_button(self._card, "Сайт Ollama", width=180,
                           command=lambda: webbrowser.open(OLLAMA_DOWNLOAD_URL)).pack(pady=(0, 28))
            return

        if state == osu.INSTALLED:
            where = osu.find_ollama_dir()
            self._head("Ollama встановлена, але не запущена",
                       (f"Знайшли її тут: {where}\n" if where else "") +
                       "Запустимо — і можна працювати.")
            self._folder_row("Де зберігати моделі", "models",
                             "Кожна модель важить 1–20 ГБ. Зміна діє для нових завантажень.")
            self._status = ctk.CTkLabel(self._card, text="", font=font(12),
                                        text_color=c("text_dim"))
            self._status.pack(pady=(10, 0))
            self._go = primary_button(self._card, "Запустити Ollama", width=220, height=42,
                                      command=self._start_existing)
            self._go.pack(pady=(10, 28))
            return

        self._head("Налаштуймо Ollama",
                   "Ollama — безкоштовна програма, що запускає моделі на твоєму ПК. Open Forge "
                   "встановить її сам: обери, куди поставити програму і де зберігати моделі.")
        self._folder_row("Куди встановити Ollama", "ollama", "Займе близько 2 ГБ.")
        self._folder_row("Де зберігати моделі", "models",
                         "Кожна модель важить 1–20 ГБ — обери диск, де багато місця.")

        prog = ctk.CTkFrame(self._card, fg_color="transparent")
        prog.pack(fill="x", padx=40, pady=(14, 0))
        self._bar = ctk.CTkProgressBar(prog, height=8, corner_radius=4,
                                       progress_color=c("accent"), fg_color=c("card_alt"))
        self._status = ctk.CTkLabel(prog, text="", font=font(12), text_color=c("text_dim"),
                                    anchor="w")
        self._status.pack(fill="x")

        btns = ctk.CTkFrame(self._card, fg_color="transparent")
        btns.pack(pady=(12, 28))
        self._go = primary_button(btns, "Встановити Ollama", width=210, height=42,
                                  image=icon("download", "#FFFFFF", 16), compound="left",
                                  command=self._install)
        self._go.pack(side="left")
        self._alt = ghost_button(btns, "Вже є — перевірити", width=180, height=42,
                                 image=icon("refresh", c("text_dim"), 15), compound="left",
                                 command=self.on_show)
        self._alt.pack(side="left", padx=(10, 0))

    def _folder_row(self, title: str, which: str, hint: str):
        box = ctk.CTkFrame(self._card, fg_color="transparent")
        box.pack(fill="x", padx=40, pady=(6, 4))
        box.grid_columnconfigure(0, weight=1)
        ctk.CTkLabel(box, text=title, font=font(13, "bold"), text_color=c("text"),
                     anchor="w").grid(row=0, column=0, columnspan=2, sticky="w")
        var = ctk.StringVar(value=str(self._ollama_dir if which == "ollama" else self._models_dir))
        ctk.CTkEntry(box, textvariable=var, height=36, font=font(13), border_width=0,
                     fg_color=c("card_alt"), text_color=c("text"),
                     corner_radius=RADIUS["button"]).grid(row=1, column=0, sticky="ew",
                                                          pady=(4, 0))
        hint_lbl = ctk.CTkLabel(box, text="", font=font(11), text_color=c("text_faint"),
                                anchor="w")
        hint_lbl.grid(row=2, column=0, columnspan=2, sticky="w")

        def update(*_):
            p = Path(var.get().strip() or ".")
            if which == "ollama":
                self._ollama_dir = p
            else:
                self._models_dir = p
            free = osu.free_gb(p)
            hint_lbl.configure(text=hint + (f"  Вільно на диску: {free:.0f} ГБ." if free else ""))

        def browse():
            path = filedialog.askdirectory(initialdir=var.get(), title=title)
            if path:
                var.set(str(Path(path)))

        ghost_button(box, "Обрати…", width=104, height=36,
                     image=icon("folder", c("text_dim"), 15), compound="left",
                     command=browse).grid(row=1, column=1, padx=(8, 0), pady=(4, 0))
        var.trace_add("write", update)
        update()

    # ── actions ───────────────────────────────────────────────────────────────

    def _set_status(self, text: str, color="text_dim"):
        try:
            self._status.configure(text=text, text_color=c(color))
        except Exception:
            pass

    def _start_existing(self):
        self._go.configure(state="disabled")
        self._set_status("Запускаю Ollama…")
        models = self._models_dir

        def work():
            osu.set_user_env("OLLAMA_MODELS", str(models))
            models.mkdir(parents=True, exist_ok=True)
            osu.start()
            return osu.wait_running(45)

        bg.run(work, on_done=self._started, on_error=lambda e: self._failed(str(e)))

    def _install(self):
        if self._busy:
            self._stop.set()
            return
        self._busy = True
        self._stop.clear()
        target, models = self._ollama_dir, self._models_dir
        self._go.configure(text="Скасувати", image=icon("x", "#FFFFFF", 15))
        self._alt.configure(state="disabled")
        self._bar.pack(fill="x", pady=(0, 6), before=self._status)
        self._bar.set(0)
        self._set_status("Завантажую Ollama…")

        def progress(done, total, speed):
            bg.ui(self._on_progress, done, total, speed)

        def work():
            installer = osu.download_installer(progress, self._stop)
            if installer is None:
                return "cancelled"
            bg.ui(self._set_status, "Встановлюю… (з'явиться невелике вікно інсталятора)")
            bg.ui(lambda: (self._bar.configure(mode="indeterminate"), self._bar.start()))
            osu.set_user_env("OLLAMA_MODELS", str(models))
            models.mkdir(parents=True, exist_ok=True)
            code = osu.run_installer(installer, target)
            installer.unlink(missing_ok=True)
            if code != 0:
                raise RuntimeError(f"інсталятор Ollama завершився з кодом {code}")
            bg.ui(self._set_status, "Запускаю Ollama…")
            if not osu.wait_running(8):
                osu.start(target)
            return "ok" if osu.wait_running(60) else "not_running"

        bg.run(work, on_done=self._install_done, on_error=lambda e: self._failed(str(e)))

    def _on_progress(self, done, total, speed):
        if total:
            self._bar.set(done / total)
            left = (total - done) / speed if speed else 0
            eta = f" · ще ~{left / 60:.0f} хв" if left > 90 else (f" · ще ~{left:.0f} с" if left else "")
            self._set_status(f"Завантажую Ollama: {done / 1024**3:.2f} з {total / 1024**3:.2f} ГБ"
                             f" · {speed / 1024**2:.1f} МБ/с{eta}")
        else:
            self._set_status(f"Завантажую Ollama: {done / 1024**2:.0f} МБ")

    def _install_done(self, result):
        self._busy = False
        try:
            self._bar.stop()
            self._bar.configure(mode="determinate")
        except Exception:
            pass
        if result == "cancelled":
            self._render(osu.MISSING)
            self._set_status("Завантаження скасовано.")
            return
        if result == "not_running":
            self._failed("Ollama встановилась, але не запустилась. Спробуй запустити її з меню «Пуск».")
            return
        self._started(True)

    def _started(self, ok):
        if not ok:
            self._failed("Ollama не відповідає. Спробуй запустити її з меню «Пуск».")
            return
        self.app.toast("Ollama працює", kind="success")
        self.app.refresh_models()
        self._render(osu.RUNNING)

    def _failed(self, text: str):
        self._busy = False
        self._set_status(text, "danger")
        try:
            self._go.configure(state="normal", text="Спробувати ще раз",
                               image=icon("refresh", "#FFFFFF", 15))
            self._alt.configure(state="normal")
        except Exception:
            pass
