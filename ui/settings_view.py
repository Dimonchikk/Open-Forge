"""Settings: appearance, Ollama, about/updates, uninstall."""
import webbrowser
import customtkinter as ctk

from core import settings, uninstall
from core.ollama_client import OLLAMA_BASE, OLLAMA_DOWNLOAD_URL
from core.settings import DATA_DIR
from ui import bg
from ui.dialogs import Dialog
from ui.icons import icon, logo
from ui.theme import (c, font, RADIUS, apply_theme, ghost_button, danger_button, segmented,
                      page_header)
from version import VERSION

REPO_URL = "https://github.com/Dimonchikk/Open-Forge"
SITE_URL = "https://dimonchikk.github.io/Open-Forge/"
THEMES = {"Як у системі": "system", "Світла": "light", "Темна": "dark"}


class SettingsView(ctk.CTkFrame):
    def __init__(self, master, app, **kw):
        super().__init__(master, fg_color=c("bg"), corner_radius=0, **kw)
        self.app = app
        self._build()

    def _section(self, parent, title: str, danger=False) -> ctk.CTkFrame:
        ctk.CTkLabel(parent, text=title, font=font(13, "bold"),
                     text_color=c("danger") if danger else c("text_dim"), anchor="w"
                     ).pack(anchor="w", padx=4, pady=(18, 6))
        card = ctk.CTkFrame(parent, fg_color=c("card"), corner_radius=RADIUS["card"],
                            border_width=1, border_color=c("danger_soft") if danger else c("border"))
        card.pack(fill="x")
        card.grid_columnconfigure(0, weight=1)
        return card

    def _row(self, card, row, title, sub="", widget=None):
        box = ctk.CTkFrame(card, fg_color="transparent")
        box.grid(row=row, column=0, sticky="ew", padx=18, pady=14)
        ctk.CTkLabel(box, text=title, font=font(14), text_color=c("text"), anchor="w"
                     ).pack(anchor="w")
        lbl = None
        if sub:
            lbl = ctk.CTkLabel(box, text=sub, font=font(12), text_color=c("text_dim"),
                               anchor="w", justify="left", wraplength=520)
            lbl.pack(anchor="w")
        if widget is not None:
            widget.grid(row=row, column=1, sticky="e", padx=18)
        if row:
            ctk.CTkFrame(card, height=1, fg_color=c("border")).grid(
                row=row, column=0, columnspan=2, sticky="new", padx=18)
        return lbl

    def _build(self):
        scroll = ctk.CTkScrollableFrame(self, fg_color="transparent",
                                        scrollbar_button_color=c("border"))
        scroll.pack(fill="both", expand=True)
        col = ctk.CTkFrame(scroll, fg_color="transparent", width=720)
        col.pack(fill="x", padx=28, pady=(22, 28))
        page_header(col, "Налаштування").pack(anchor="w")

        # appearance
        card = self._section(col, "ВИГЛЯД")
        theme = segmented(card, list(THEMES), command=self._on_theme)
        current = settings.get("theme")
        theme.set(next((k for k, v in THEMES.items() if v == current), "Як у системі"))
        self._row(card, 0, "Тема", "Світла, темна або як налаштовано у Windows.", theme)
        hw = ctk.CTkSwitch(card, text="", command=self._toggle_hw, progress_color=c("accent"))
        if settings.get("show_hw_panel"):
            hw.select()
        self._row(card, 1, "Панель «Система» справа",
                  "Навантаження процесора, пам'яті й відеокарти в реальному часі.", hw)

        # ollama
        card = self._section(col, "OLLAMA")
        self._ollama_lbl = self._row(card, 0, "Стан", "Перевіряю…",
                                     ghost_button(card, "Сайт Ollama", width=140, height=34,
                                                  image=icon("external", c("text_dim"), 14),
                                                  compound="left",
                                                  command=lambda: webbrowser.open(
                                                      OLLAMA_DOWNLOAD_URL)))
        self._row(card, 1, "Адреса", OLLAMA_BASE)

        # about
        card = self._section(col, "ПРО ПРОГРАМУ")
        head = ctk.CTkFrame(card, fg_color="transparent")
        head.grid(row=0, column=0, sticky="w", padx=18, pady=16)
        ctk.CTkLabel(head, text="", image=logo(44)).pack(side="left")
        txt = ctk.CTkFrame(head, fg_color="transparent")
        txt.pack(side="left", padx=12)
        ctk.CTkLabel(txt, text="Open Forge", font=font(16, "bold"), text_color=c("text"),
                     anchor="w").pack(anchor="w")
        self._ver_lbl = ctk.CTkLabel(txt, text=f"Версія {VERSION}", font=font(12),
                                     text_color=c("text_dim"), anchor="w")
        self._ver_lbl.pack(anchor="w")
        btns = ctk.CTkFrame(card, fg_color="transparent")
        btns.grid(row=0, column=1, sticky="e", padx=18)
        ghost_button(btns, "Перевірити оновлення", width=190, height=34,
                     image=icon("refresh", c("text_dim"), 14), compound="left",
                     command=self._check_updates).pack(side="left")
        ghost_button(btns, "GitHub", width=96, height=34,
                     command=lambda: webbrowser.open(REPO_URL)).pack(side="left", padx=(8, 0))
        self._row(card, 1, "Де зберігаються налаштування", str(DATA_DIR))

        # uninstall
        card = self._section(col, "ВИДАЛЕННЯ", danger=True)
        self._row(card, 0, "Видалити Open Forge з цього ПК",
                  "Прибере програму, ярлики й за бажанням — налаштування та завантажені моделі.",
                  danger_button(card, "Видалити…", width=130, height=34,
                                image=icon("trash", "#FFFFFF", 14), compound="left",
                                command=self._uninstall_dialog))

    # ── callbacks ─────────────────────────────────────────────────────────────

    def set_ollama(self, ok: bool, n_models: int):
        if self._ollama_lbl:
            self._ollama_lbl.configure(
                text=f"Працює · встановлено моделей: {n_models}" if ok else
                "Не запущена. Встанови або запусти Ollama, щоб працювали моделі.",
                text_color=c("success") if ok else c("warning"))

    def _on_theme(self, value):
        mode = THEMES[value]
        settings.set("theme", mode)
        apply_theme(mode)

    def _toggle_hw(self):
        show = not settings.get("show_hw_panel")
        settings.set("show_hw_panel", show)
        self.app.set_hw_panel(show)

    def _check_updates(self):
        self._ver_lbl.configure(text=f"Версія {VERSION} · перевіряю…")
        self.app.check_updates(manual=True, done=self._update_result)

    def _update_result(self, latest):
        if latest is None:
            self._ver_lbl.configure(text=f"Версія {VERSION} · не вдалося перевірити")
        elif latest == VERSION:
            self._ver_lbl.configure(text=f"Версія {VERSION} · це найновіша версія")
        else:
            self._ver_lbl.configure(text=f"Версія {VERSION} · доступна {latest}")

    # ── uninstall ─────────────────────────────────────────────────────────────

    def _uninstall_dialog(self):
        d = Dialog(self, "Видалити Open Forge", width=500)
        d.heading("Видалити Open Forge?", "Обери, що прибрати з комп'ютера.")

        opts = ctk.CTkFrame(d.body, fg_color="transparent")
        opts.pack(fill="x", pady=(16, 0))

        def option(title, sub, checked, enabled=True):
            row = ctk.CTkFrame(opts, fg_color=c("card_alt"), corner_radius=RADIUS["button"])
            row.pack(fill="x", pady=4)
            var = ctk.BooleanVar(value=checked)
            cb = ctk.CTkCheckBox(row, text=title, variable=var, font=font(14),
                                 text_color=c("text"), fg_color=c("danger"),
                                 hover_color=c("danger_hover"), corner_radius=6,
                                 state="normal" if enabled else "disabled")
            cb.pack(anchor="w", padx=14, pady=(12, 0))
            lbl = ctk.CTkLabel(row, text=sub, font=font(12), text_color=c("text_dim"),
                               anchor="w", justify="left", wraplength=400)
            lbl.pack(anchor="w", padx=(46, 14), pady=(0, 12))
            return var, lbl

        option("Програма Open Forge", uninstall.describe() + "\nРазом з ярликами в меню «Пуск» "
               "і на робочому столі.", True, enabled=False)
        v_set, _ = option("Налаштування", str(DATA_DIR), True)
        v_mod, mod_lbl = option("Завантажені моделі", "Рахую…", False)
        ctk.CTkLabel(d.body, text="Ollama залишиться встановленою — це окрема програма.",
                     font=font(12), text_color=c("text_faint"), anchor="w"
                     ).pack(anchor="w", pady=(10, 0))

        def got_models(res):
            n, gb = res
            try:
                mod_lbl.configure(text=f"{n} шт., {gb:.1f} ГБ. Їх також бачать інші програми "
                                       "на Ollama (наприклад, OpenCode) — вони теж їх втратять."
                                  if n else "Моделей немає.")
            except Exception:
                pass

        bg.run(uninstall.models_summary, on_done=got_models,
               on_error=lambda e: got_models((0, 0)))

        status = ctk.CTkLabel(d.buttons, text="", font=font(12), text_color=c("text_dim"))
        status.pack(side="left")

        def go():
            go_btn.configure(state="disabled")
            cancel_btn.configure(state="disabled")

            def work():
                if v_mod.get():
                    uninstall.remove_models(lambda i, n, name: bg.ui(
                        lambda t: status.configure(text=t), f"Видаляю моделі {i + 1}/{n}…"))
                if v_set.get():
                    uninstall.remove_settings()
                return uninstall.schedule_program_removal()

            def finished(scheduled):
                if scheduled:
                    self.app.quit_now()
                else:
                    status.configure(text="Готово. (Запуск з коду: файли програми не видалено.)")
                    cancel_btn.configure(state="normal", text="Закрити")

            bg.run(work, on_done=finished,
                   on_error=lambda e: status.configure(text=f"Помилка: {e}"))

        go_btn = danger_button(d.buttons, "Видалити", width=130, command=go)
        go_btn.pack(side="right")
        cancel_btn = ghost_button(d.buttons, "Скасувати", width=120, command=d.close)
        cancel_btn.pack(side="right", padx=(0, 8))
        d.show()
