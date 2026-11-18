"""Chat: model picker, message bubbles, streaming answers, instructions panel."""
import os
import re
import sys
import threading
import time
from pathlib import Path
from tkinter import filedialog
import customtkinter as ctk

from agent.agent_core import AgentRunner, PermissionLevel, ToolsUnsupported

from core import settings
from core.ollama_client import chat_stream, tokens_per_second
from ui import bg
from ui.dialogs import Dialog, confirm
from ui.icons import icon, logo
from ui.inputs import Hint
from ui.theme import (c, font, RADIUS, primary_button, ghost_button, option_menu, segmented,
                      logical, IconButton)

PERMS = {
    "Питати завжди": (PermissionLevel.MANUAL, "Кожну дію агента ти підтверджуєш сам.", "text_dim"),
    "Авто": (PermissionLevel.AUTO,
             "Файли створює сам, а видалення й запуск команд — лише з твого дозволу.", "text_dim"),
    "Повний доступ": (PermissionLevel.FULL,
                      "Агент діє без підтверджень, включно з командами. Вмикай лише для "
                      "папок, які не шкода.", "warning"),
}

SUGGESTIONS = [
    ("Поясни просто", "як працює нейромережа, на прикладі"),
    ("Напиши скрипт", "на Python для сортування фото"),
    ("Склади план", "підготовки до іспиту за тиждень"),
    ("Переклади", "«Дякую за допомогу!» англійською"),
]
MAX_COL = 820          # max width of the message column, px
_THINK_RE = re.compile(r"<think>.*?(</think>|$)", re.S)


class _Composer(ctk.CTkFrame):
    """Rounded multi-line input: [+ tools] [text] [send/stop]."""

    def __init__(self, master, on_send, on_stop, placeholder, on_plus=None):
        super().__init__(master, fg_color=c("card"), corner_radius=22,
                         border_width=1, border_color=c("border"))
        self._on_send, self._on_stop = on_send, on_stop
        self._busy = False
        self.grid_columnconfigure(1, weight=1)

        self.plus = IconButton(self, icon("plus", c("text_dim"), 18), command=on_plus,
                               border_color=c("border"))
        self.plus.grid(row=0, column=0, sticky="sw", padx=(8, 0), pady=8)
        if on_plus is None:
            self.plus.grid_remove()

        self.text = ctk.CTkTextbox(self, height=28, fg_color="transparent", wrap="word",
                                   font=font(14), text_color=c("text"), border_width=0,
                                   activate_scrollbars=False, undo=True, maxundo=-1)
        self.text.grid(row=0, column=1, sticky="ew", padx=(8, 6), pady=(10, 10))
        tb = self.text._textbox
        tb.configure(insertwidth=2)
        self._hint = Hint(tb, placeholder)

        self.btn = IconButton(self, icon("send", "#FFFFFF", 18), command=self._click,
                              fg_color=c("accent"), hover_color=c("accent_hover"))
        self.btn.grid(row=0, column=2, sticky="se", padx=(0, 8), pady=8)

        tb.bind("<Return>", self._on_return)
        tb.bind("<KeyRelease>", lambda e: self._refresh(), add="+")
        tb.bind("<<Paste>>", lambda e: self.after_idle(self._refresh), add="+")

    def set_placeholder(self, text: str):
        self._hint.set_text(text)

    def set_plus_active(self, on: bool):
        self.plus.set_style(fg_color=c("accent_soft") if on else "transparent",
                            border_color=c("accent_soft") if on else c("border"),
                            image=icon("folder" if on else "plus",
                                       c("accent") if on else c("text_dim"), 18))

    def get(self) -> str:
        return self._hint.value().strip()

    def set(self, value: str):
        self._hint.clear()
        self.text.delete("1.0", "end")
        self.text.insert("1.0", value)
        if not value:
            self._hint.show()
        self._refresh()

    def clear(self):
        self.set("")

    def set_busy(self, busy: bool):
        self._busy = busy
        # stop = inverted button (dark in light theme, light in dark theme)
        self.btn.set_style(image=icon("stop", c("card"), 18) if busy else
                           icon("send", "#FFFFFF", 18),
                           fg_color=c("text") if busy else c("accent"),
                           hover_color=c("text_dim") if busy else c("accent_hover"))

    def _click(self):
        (self._on_stop if self._busy else self._on_send)()

    def _on_return(self, event):
        if event.state & 0x0001:      # Shift+Enter → new line
            return "break" if self._hint.active else None
        if not self._busy:
            self._on_send()
        return "break"

    def _refresh(self):
        try:
            lines = int(self.text._textbox.count("1.0", "end", "displaylines")[0])
        except Exception:
            lines = 1
        self.text.configure(height=min(max(lines, 1), 8) * 21 + 7)


class ChatView(ctk.CTkFrame):
    def __init__(self, master, app, **kw):
        super().__init__(master, fg_color=c("bg"), corner_radius=0, **kw)
        self.app = app
        self._model: str = settings.get("model") or ""
        self._models: list[str] = []
        self._messages: list[dict] = []
        self._labels: list[tuple[ctk.CTkLabel, str]] = []   # (label, "user"|"ai")
        self._stop = threading.Event()
        self._streaming = False
        self._scroll_pending = False
        self._state = "loading"      # loading | no_ollama | no_models | ready
        self._build()

    # ── layout ────────────────────────────────────────────────────────────────

    def _build(self):
        self.grid_columnconfigure(0, weight=1)
        self.grid_rowconfigure(2, weight=1)

        # header
        head = ctk.CTkFrame(self, fg_color="transparent", height=64)
        head.grid(row=0, column=0, sticky="ew", padx=24, pady=(16, 0))
        head.grid_columnconfigure(1, weight=1)

        self._picker = option_menu(head, ["Завантаження…"], width=380,
                                   command=self._on_pick, font=font(14, "bold"))
        self._picker.grid(row=0, column=0, sticky="w")

        right = ctk.CTkFrame(head, fg_color="transparent")
        right.grid(row=0, column=2, sticky="e")
        self._tps = ctk.CTkLabel(right, text="", font=font(12), text_color=c("text_dim"),
                                 fg_color=c("card_alt"), corner_radius=RADIUS["badge"], height=28)
        self._sys_btn = ghost_button(right, "Інструкція", width=124, height=34,
                                     image=icon("sliders", c("text_dim"), 16), compound="left",
                                     command=self._toggle_sys)
        self._sys_btn.pack(side="left", padx=(8, 0))
        ghost_button(right, "Новий чат", width=118, height=34,
                     image=icon("plus", c("text_dim"), 16), compound="left",
                     command=self.new_chat).pack(side="left", padx=(8, 0))

        # instructions (system prompt) panel — hidden until toggled
        self._sys_panel = ctk.CTkFrame(self, fg_color=c("card"), corner_radius=RADIUS["card"],
                                       border_width=1, border_color=c("border"))
        ctk.CTkLabel(self._sys_panel, text="Інструкція для моделі", font=font(13, "bold"),
                     text_color=c("text")).pack(anchor="w", padx=16, pady=(12, 0))
        ctk.CTkLabel(self._sys_panel, font=font(12), text_color=c("text_dim"), justify="left",
                     text="Як модель має поводитися в цьому чаті. Наприклад: «Відповідай коротко "
                          "і українською» або «Ти — викладач фізики».").pack(anchor="w", padx=16)
        self._sys_text = ctk.CTkTextbox(self._sys_panel, height=64, font=font(13), wrap="word",
                                        fg_color=c("card_alt"), text_color=c("text"),
                                        corner_radius=RADIUS["input"], border_width=0,
                                        undo=True, maxundo=-1)
        self._sys_text.pack(fill="x", padx=16, pady=(8, 14))
        self._sys_text.insert("1.0", settings.get("system_prompt") or "")
        self._sys_text._textbox.bind("<KeyRelease>", lambda e: self._save_sys())
        self._sys_open = False
        self._paint_sys_btn()

        # body: empty state OR message list
        self._body = ctk.CTkFrame(self, fg_color="transparent")
        self._body.grid(row=2, column=0, sticky="nsew")
        self._body.grid_columnconfigure(0, weight=1)
        self._body.grid_rowconfigure(0, weight=1)

        self._scroll = ctk.CTkScrollableFrame(self._body, fg_color="transparent",
                                              scrollbar_button_color=c("border"))
        self._scroll.grid_columnconfigure(0, weight=1)
        self._col = ctk.CTkFrame(self._scroll, fg_color="transparent")
        self._col.grid(row=0, column=0, sticky="ew", padx=24, pady=(8, 8))
        self._col.grid_columnconfigure(0, weight=1)
        self._scroll.bind("<Configure>", self._on_resize)

        self._empty = ctk.CTkFrame(self._body, fg_color="transparent")

        # composer
        foot = ctk.CTkFrame(self, fg_color="transparent")
        foot.grid(row=3, column=0, sticky="ew", padx=24, pady=(4, 14))
        foot.grid_columnconfigure(0, weight=1)
        self._foot = foot

        # agent chip: shown while the chat works inside a folder
        self._agent_chip = ctk.CTkFrame(foot, fg_color=c("accent_soft"), corner_radius=RADIUS["button"])
        self._chip_lbl = ctk.CTkButton(
            self._agent_chip, text="", height=30, anchor="w", font=font(12, "bold"),
            fg_color="transparent", hover_color=c("accent_soft"), text_color=c("accent"),
            image=icon("folder", c("accent"), 15), compound="left", command=self._agent_dialog)
        self._chip_lbl.pack(side="left", padx=(4, 0))
        IconButton(self._agent_chip, icon("x", c("accent"), 13), command=self._agent_off,
                   size=26).pack(side="left", padx=4)

        self._composer = _Composer(foot, self._send, self._stop_now,
                                   "Напиши повідомлення…", on_plus=self._agent_dialog)
        self._composer.grid(row=1, column=0, sticky="ew")
        ctk.CTkLabel(foot, text="Enter — надіслати · Shift+Enter — новий рядок · "
                                "+ — робота з файлами в папці",
                     font=font(11), text_color=c("text_faint")).grid(row=2, column=0, pady=(6, 0))
        self._agent_folder = ""
        self._agent_perm = settings.get("agent_perm") or "Авто"
        if settings.get("agent_on") and settings.get("agent_folder"):
            self._set_agent(settings.get("agent_folder"), self._agent_perm)

        self._render_empty()

    def _on_resize(self, event):
        width = logical(self, event.width)
        side = max(16, int(width - MAX_COL) // 2)
        self._col.grid(padx=side)
        col_w = min(width - 2 * side, MAX_COL)
        for lbl, kind in self._labels:
            try:
                lbl.configure(wraplength=int(col_w * 0.72) if kind == "user" else col_w - 8)
            except Exception:
                pass
        self._foot.grid(padx=max(24, int(logical(self, self.winfo_width()) - MAX_COL) // 2))

    # ── public API used by App ────────────────────────────────────────────────

    def set_models(self, names: list[str], ollama_ok: bool):
        self._models = names
        if not ollama_ok:
            self._state = "no_ollama"
            self._picker.configure(values=["Ollama не запущена"], state="disabled")
            self._picker.set("Ollama не запущена")
        elif not names:
            self._state = "no_models"
            self._picker.configure(values=["Немає моделей"], state="disabled")
            self._picker.set("Немає моделей")
        else:
            self._state = "ready"
            self._picker.configure(values=names, state="normal")
            if self._model not in names:
                self._model = names[0]
            self._picker.set(self._model)
        if not self._messages:
            self._render_empty()

    def set_model(self, model_id: str):
        self._model = model_id
        settings.set("model", model_id)
        if model_id in self._models:
            self._picker.set(model_id)

    def new_chat(self):
        if self._streaming:
            self._stop.set()
        self._messages.clear()
        self._labels.clear()
        for w in self._col.winfo_children():
            w.destroy()
        self._tps.pack_forget()
        self._render_empty()
        self._composer.text.focus_set()

    # ── empty state ───────────────────────────────────────────────────────────

    def _render_empty(self):
        for w in self._empty.winfo_children():
            w.destroy()
        self._scroll.grid_forget()
        self._empty.grid(row=0, column=0, sticky="nsew")

        box = ctk.CTkFrame(self._empty, fg_color="transparent")
        box.place(relx=0.5, rely=0.46, anchor="center")

        ctk.CTkLabel(box, text="", image=logo(56)).pack(pady=(0, 14))

        if self._state == "no_ollama":
            title = "Ollama не запущена"
            text = ("Open Forge запускає моделі через Ollama — безкоштовну програму.\n"
                    "Встанови її за хвилину — з вибором папки, — і чат запрацює.")
        elif self._state == "no_models":
            title = "Ще немає жодної моделі"
            text = ("Встанови першу модель з каталогу.\n"
                    "Ми позначили ті, що швидко працюватимуть на твоєму ПК.")
        elif self._state == "loading":
            title, text = "Open Forge", "Підключаюся до Ollama…"
        else:
            title = "Чим можу допомогти?"
            text = "Модель працює локально — нічого не йде в інтернет."

        ctk.CTkLabel(box, text=title, font=font(26, "bold"), text_color=c("text")).pack()
        ctk.CTkLabel(box, text=text, font=font(14), text_color=c("text_dim"),
                     justify="center").pack(pady=(6, 20))

        if self._state == "no_ollama":
            row = ctk.CTkFrame(box, fg_color="transparent")
            row.pack()
            primary_button(row, "Встановити Ollama", width=190, height=40,
                           image=icon("download", "#FFFFFF", 16), compound="left",
                           command=lambda: self.app.show("setup")).pack(side="left")
            ghost_button(row, "Перевірити знову", width=170, height=40,
                         image=icon("refresh", c("text_dim"), 16), compound="left",
                         command=self.app.refresh_models).pack(side="left", padx=(10, 0))
        elif self._state == "no_models":
            primary_button(box, "Відкрити каталог моделей", width=240, height=40,
                           image=icon("models", "#FFFFFF", 16), compound="left",
                           command=lambda: self.app.show("models")).pack()
        elif self._state == "ready":
            grid = ctk.CTkFrame(box, fg_color="transparent")
            grid.pack()
            for i, (head, tail) in enumerate(SUGGESTIONS):
                self._chip(grid, head, tail).grid(row=i // 2, column=i % 2, padx=6, pady=6,
                                                  sticky="nsew")

    def _chip(self, parent, head: str, tail: str) -> ctk.CTkFrame:
        chip = ctk.CTkFrame(parent, width=310, height=72, fg_color=c("card"),
                            corner_radius=RADIUS["card"], border_width=1,
                            border_color=c("border"), cursor="hand2")
        chip.grid_propagate(False)
        chip.grid_columnconfigure(0, weight=1)
        a = ctk.CTkLabel(chip, text=head, font=font(13, "bold"), text_color=c("text"),
                         anchor="w", cursor="hand2")
        a.grid(row=0, column=0, sticky="w", padx=16, pady=(12, 0))
        b = ctk.CTkLabel(chip, text=tail, font=font(13), text_color=c("text_dim"), anchor="w",
                         justify="left", wraplength=280, cursor="hand2")
        b.grid(row=1, column=0, sticky="w", padx=16)
        go = lambda e: self._use_suggestion(f"{head} {tail}")   # noqa: E731
        hover = lambda on: chip.configure(fg_color=c("hover") if on else c("card"))  # noqa: E731
        for w in (chip, a, b):
            w.bind("<Button-1>", go)
            w.bind("<Enter>", lambda e: hover(True))
            w.bind("<Leave>", lambda e: hover(False))
        return chip

    def _use_suggestion(self, text: str):
        self._composer.set(text)
        self._send()

    def _show_messages(self):
        if self._empty.winfo_ismapped():
            self._empty.grid_forget()
            self._scroll.grid(row=0, column=0, sticky="nsew")

    # ── instructions panel ────────────────────────────────────────────────────

    def _toggle_sys(self):
        self._sys_open = not self._sys_open
        if self._sys_open:
            self._sys_panel.grid(row=1, column=0, sticky="ew", padx=24, pady=(12, 0))
            self._sys_text.focus_set()
        else:
            self._sys_panel.grid_forget()
        self._paint_sys_btn()

    def _save_sys(self):
        settings.set("system_prompt", self._sys_text.get("1.0", "end").strip())
        self._paint_sys_btn()

    def _paint_sys_btn(self):
        active = bool(self._sys_text.get("1.0", "end").strip()) or self._sys_open
        self._sys_btn.configure(
            fg_color=c("accent_soft") if active else "transparent",
            text_color=c("accent") if active else c("text"),
            border_color=c("accent_soft") if active else c("border"),
            image=icon("sliders", c("accent") if active else c("text_dim"), 16))

    # ── messages ──────────────────────────────────────────────────────────────

    def _col_width(self) -> int:
        w = logical(self, self._scroll.winfo_width()) if self._scroll.winfo_width() > 1 else 800
        side = max(16, int(w - MAX_COL) // 2)
        return min(w - 2 * side, MAX_COL)

    def _add_user(self, text: str):
        row = ctk.CTkFrame(self._col, fg_color="transparent")
        row.grid(sticky="ew", pady=(10, 4))
        bubble = ctk.CTkFrame(row, fg_color=c("accent"), corner_radius=18)
        bubble.pack(side="right")
        lbl = ctk.CTkLabel(bubble, text=text, font=font(14), text_color="#FFFFFF",
                           justify="left", anchor="w", wraplength=int(self._col_width() * 0.72))
        lbl.pack(padx=14, pady=9)
        self._labels.append((lbl, "user"))

    def _add_ai(self) -> dict:
        row = ctk.CTkFrame(self._col, fg_color="transparent")
        row.grid(sticky="ew", pady=(10, 6))
        row.grid_columnconfigure(0, weight=1)
        head = ctk.CTkFrame(row, fg_color="transparent")
        head.grid(row=0, column=0, sticky="w")
        ctk.CTkLabel(head, text="", image=icon("agent", c("accent"), 16)).pack(side="left")
        ctk.CTkLabel(head, text=self._model, font=font(12, "bold"),
                     text_color=c("text_dim")).pack(side="left", padx=(6, 0))
        lbl = ctk.CTkLabel(row, text="Думає…", font=font(14), text_color=c("text_faint"),
                           justify="left", anchor="w", wraplength=self._col_width() - 8)
        lbl.grid(row=1, column=0, sticky="w", pady=(4, 0))
        self._labels.append((lbl, "ai"))
        foot = ctk.CTkFrame(row, fg_color="transparent")
        foot.grid(row=2, column=0, sticky="w", pady=(4, 0))
        return {"row": row, "label": lbl, "foot": foot, "buf": [], "thinking": 0}

    def _scroll_end(self):
        # Coalesce scrolling: never force a synchronous full relayout here (that was
        # O(n) per streamed token and froze the window on long answers). Tk lays the
        # text out on idle; we just ride to the bottom once afterwards.
        if self._scroll_pending:
            return
        self._scroll_pending = True
        self.after_idle(self._do_scroll)

    def _do_scroll(self):
        self._scroll_pending = False
        try:
            self._scroll._parent_canvas.yview_moveto(1.0)
        except Exception:
            pass

    # ── send / stream ─────────────────────────────────────────────────────────

    def _on_pick(self, name: str):
        self.set_model(name)

    def _send(self):
        text = self._composer.get()
        if not text or self._streaming:
            return
        if self._state != "ready":
            self.app.show("models" if self._state == "no_models" else "chat")
            return
        self._composer.clear()
        self._show_messages()
        self._messages.append({"role": "user", "content": text})
        self._add_user(text)
        msg = self._add_ai()
        self._scroll_end()

        self._stop.clear()
        self._streaming = True
        self._composer.set_busy(True)
        system = self._sys_text.get("1.0", "end").strip()
        if self._agent_folder:
            self._run_agent(msg, text, system)
            return
        threading.Thread(target=self._worker, args=(msg, list(self._messages), system),
                         daemon=True).start()
        self._render_loop(msg)

    def _worker(self, msg: dict, history: list, system: str):
        stats: dict = {}
        t0 = time.time()
        err = None
        try:
            for tok in chat_stream(self._model, history, system=system, stats=stats,
                                   on_thinking=lambda t: msg.__setitem__("thinking",
                                                                         msg["thinking"] + len(t))):
                if self._stop.is_set():
                    break
                msg["buf"].append(tok)
        except Exception as e:  # noqa: BLE001
            err = str(e)
        stats.setdefault("wall", time.time() - t0)
        bg.ui(self._finish, msg, stats, err)

    def _render_loop(self, msg: dict):
        if not self._streaming:
            return
        n = len(msg["buf"])
        if n != msg.get("shown_n"):          # only repaint when new tokens arrived
            msg["shown_n"] = n
            raw = "".join(msg["buf"])
            text = (_THINK_RE.sub("", raw) if "<think>" in raw else raw).lstrip()
            if text:
                msg["label"].configure(text=text, text_color=c("text"))
                self._scroll_end()
            elif msg["thinking"]:
                msg["label"].configure(text=f"Міркує… ({msg['thinking']} символів думок)")
        self.after(120, self._render_loop, msg)

    def _finish(self, msg: dict, stats: dict, err):
        self._streaming = False
        self._composer.set_busy(False)
        text = _THINK_RE.sub("", "".join(msg["buf"])).strip()
        lbl = msg["label"]

        if err and not text:
            hint = ""
            if "not found" in err.lower():
                hint = "\nМодель не знайдено — встанови її в розділі «Моделі»."
            elif "connection" in err.lower() or "refused" in err.lower():
                hint = "\nСхоже, Ollama не запущена."
            lbl.configure(text=f"Не вдалося отримати відповідь: {err}{hint}",
                          text_color=c("danger"))
            self._messages.pop()   # drop the unanswered user message from history
            return

        if self._stop.is_set() and not text:
            text = "(зупинено)"
        lbl.configure(text=text, text_color=c("text"))
        self._messages.append({"role": "assistant", "content": text})

        tps = tokens_per_second(stats)
        meta = f"{tps:.1f} ток/с · {stats.get('eval_count', 0)} токенів" if tps else ""
        if self._stop.is_set():
            meta = (meta + " · зупинено").lstrip(" ·")
        if meta:
            ctk.CTkLabel(msg["foot"], text=meta, font=font(11),
                         text_color=c("text_faint")).pack(side="left")
            self._tps.configure(text=f"  {tps:.1f} ток/с  ")
            if tps:
                self._tps.pack(side="left", before=self._sys_btn)
        copy = ctk.CTkButton(msg["foot"], text="Копіювати", width=96, height=24,
                             font=font(11), image=icon("copy", c("text_faint"), 13),
                             compound="left", fg_color="transparent", hover_color=c("hover"),
                             text_color=c("text_faint"), corner_radius=8)
        copy.configure(command=lambda t=text, b=copy: self._copy(t, b))
        copy.pack(side="left", padx=(8, 0))
        self._scroll_end()

    def _copy(self, text: str, btn):
        self.clipboard_clear()
        self.clipboard_append(text)
        btn.configure(text="Скопійовано", image=icon("check", c("success"), 13))
        self.after(1500, lambda: btn.configure(text="Копіювати",
                                               image=icon("copy", c("text_faint"), 13)))

    def _stop_now(self):
        self._stop.set()

    # ── agent mode (work with files in a folder) ──────────────────────────────

    def _set_agent(self, folder: str, perm: str):
        self._agent_folder = folder
        self._agent_perm = perm
        name = Path(folder).name or folder
        self._chip_lbl.configure(text=f"  Агент працює в папці «{name}» · {perm}")
        self._agent_chip.grid(row=0, column=0, sticky="w", pady=(0, 8))
        self._composer.set_plus_active(True)
        self._composer.set_placeholder("Опиши, що зробити в папці…")
        settings.set("agent_folder", folder)
        settings.set("agent_perm", perm)
        settings.set("agent_on", True)

    def _agent_off(self):
        self._agent_folder = ""
        self._agent_chip.grid_forget()
        self._composer.set_plus_active(False)
        self._composer.set_placeholder("Напиши повідомлення…")
        settings.set("agent_on", False)

    def _agent_dialog(self):
        d = Dialog(self, "Робота з файлами", width=520)
        d.heading("Робота з файлами в папці",
                  "Модель сама створюватиме й змінюватиме файли, а за потреби запускатиме "
                  "команди — лише всередині вибраної папки. Без папки це звичайний чат.")
        folder = ctk.StringVar(value=self._agent_folder or settings.get("agent_folder")
                               or str(desktop_dir() / "OpenForgeProject"))
        ctk.CTkLabel(d.body, text="Папка", font=font(13, "bold"), text_color=c("text_dim"),
                     anchor="w").pack(anchor="w", pady=(16, 4))
        row = ctk.CTkFrame(d.body, fg_color="transparent")
        row.pack(fill="x")
        row.grid_columnconfigure(0, weight=1)
        ctk.CTkEntry(row, textvariable=folder, height=36, font=font(13), border_width=0,
                     fg_color=c("card_alt"), text_color=c("text"),
                     corner_radius=RADIUS["button"]).grid(row=0, column=0, sticky="ew")

        def browse():
            path = filedialog.askdirectory(initialdir=folder.get(), parent=d,
                                           title="Обери папку для агента")
            if path:
                folder.set(str(Path(path)))

        ghost_button(row, "Обрати…", width=104, height=36,
                     image=icon("folder", c("text_dim"), 15), compound="left",
                     command=browse).grid(row=0, column=1, padx=(8, 0))
        ctk.CTkLabel(d.body, text="Якщо такої папки ще немає — її буде створено.",
                     font=font(11), text_color=c("text_faint"), anchor="w").pack(anchor="w",
                                                                                  pady=(4, 0))

        ctk.CTkLabel(d.body, text="Дозволи", font=font(13, "bold"), text_color=c("text_dim"),
                     anchor="w").pack(anchor="w", pady=(14, 4))
        hint = ctk.CTkLabel(d.body, text="", font=font(12), anchor="w", justify="left",
                            wraplength=470)

        def on_perm(v):
            perm["v"] = v
            hint.configure(text=PERMS[v][1], text_color=c(PERMS[v][2]))

        perm = {"v": self._agent_perm if self._agent_perm in PERMS else "Авто"}
        seg = segmented(d.body, list(PERMS), command=on_perm)
        seg.set(perm["v"])
        seg.pack(anchor="w")
        hint.pack(anchor="w", pady=(6, 0))
        on_perm(perm["v"])

        def enable():
            path = folder.get().strip()
            if not path:
                return
            try:
                Path(path).expanduser().mkdir(parents=True, exist_ok=True)
            except OSError as e:
                hint.configure(text=f"Не вдалося створити папку: {e}", text_color=c("danger"))
                return
            self._set_agent(str(Path(path).expanduser()), perm["v"])
            d.close()

        primary_button(d.buttons, "Увімкнути", width=130, command=enable).pack(side="right")
        if self._agent_folder:
            ghost_button(d.buttons, "Вимкнути агента", width=150,
                         command=lambda: (self._agent_off(), d.close())).pack(side="right",
                                                                                padx=(0, 8))
        else:
            ghost_button(d.buttons, "Скасувати", width=110,
                         command=d.close).pack(side="right", padx=(0, 8))
        d.show()

    def _run_agent(self, msg: dict, task: str, instruction: str):
        lbl, foot, row = msg["label"], msg["foot"], msg["row"]
        steps = ctk.CTkFrame(row, fg_color=c("card"), corner_radius=RADIUS["card"],
                             border_width=1, border_color=c("border"))
        steps.grid_columnconfigure(1, weight=1)
        msg.update(steps=steps, n_steps=0, texts=[], last_step=None)
        lbl.grid_configure(row=2)
        foot.grid_configure(row=3)
        lbl.configure(text="Думає…")
        folder = self._agent_folder
        history = [m for m in self._messages[:-1] if m["role"] in ("user", "assistant")]

        def event(kind, payload):
            bg.ui(self._agent_event, msg, kind, payload)

        def work():
            runner = AgentRunner(model=self._model, project_folder=folder,
                                 permission=PERMS[self._agent_perm][0], confirm_cb=self._ask,
                                 event_cb=event, stop_event=self._stop, history=history,
                                 instruction=instruction)
            runner.run(task)
            return runner.changed

        bg.run(work, on_done=lambda changed: self._agent_done(msg, folder, changed, None),
               on_error=lambda e: self._agent_done(msg, folder, [], e))

    def _agent_event(self, msg: dict, kind: str, payload):
        steps = msg["steps"]
        if kind == "text":
            msg["texts"].append(payload.strip())
            msg["label"].configure(text="\n\n".join(msg["texts"]), text_color=c("text"))
        elif kind == "step":
            if not steps.winfo_ismapped():
                steps.grid(row=1, column=0, sticky="ew", pady=(6, 4))
            r = msg["n_steps"]
            msg["n_steps"] += 1
            ic = ctk.CTkLabel(steps, text="", image=icon("dot", c("text_faint"), 12), width=20)
            ic.grid(row=r * 2, column=0, sticky="nw", padx=(12, 4), pady=(9 if r == 0 else 4, 0))
            t = ctk.CTkLabel(steps, text=payload, font=font(13, mono=True), text_color=c("text"),
                             anchor="w", justify="left", wraplength=self._col_width() - 80)
            t.grid(row=r * 2, column=1, sticky="w", padx=(0, 12), pady=(8 if r == 0 else 3, 0))
            msg["last_step"] = (ic, r, payload)
            if not msg["texts"]:
                msg["label"].configure(text="Працює…", text_color=c("text_faint"))
        elif kind == "result" and msg.get("last_step"):
            ok, text = payload
            ic, r, title = msg["last_step"]
            ic.configure(image=icon("check" if ok else "x", c("success") if ok else c("danger"), 14))
            first = text.strip().splitlines()[0] if text.strip() else ""
            if not ok or title.startswith("Виконує команду"):
                short = (first[:140] + "…") if len(first) > 140 else first
                ctk.CTkLabel(msg["steps"], text=short, font=font(11, mono=True),
                             text_color=c("danger") if not ok else c("text_faint"), anchor="w",
                             justify="left", wraplength=self._col_width() - 80
                             ).grid(row=r * 2 + 1, column=1, sticky="w", padx=(0, 12))
        self._scroll_end()

    def _agent_done(self, msg: dict, folder: str, changed: list, err):
        self._streaming = False
        self._composer.set_busy(False)
        if msg.get("n_steps"):
            ctk.CTkFrame(msg["steps"], width=1, height=8, fg_color="transparent").grid(
                row=msg["n_steps"] * 2 + 1, column=0, columnspan=2)
        if isinstance(err, ToolsUnsupported):
            msg["label"].configure(
                text=f"Модель «{self._model}» не вміє працювати з файлами.\n"
                     "Для агента обери модель з позначкою «інструменти» — наприклад Qwen 3 8B "
                     "або Qwen 2.5 Coder 7B.", text_color=c("danger"))
            ghost_button(msg["foot"], "Відкрити моделі", width=150, height=30,
                         command=lambda: self.app.show("models")).pack(side="left")
            self._messages.pop()
            return
        if err:
            msg["label"].configure(text=f"Агент зупинився з помилкою: {err}",
                                   text_color=c("danger"))
            self._messages.pop()
            return
        summary = "\n\n".join(msg["texts"]) or ("Зупинено." if self._stop.is_set() else "Готово.")
        msg["label"].configure(text=summary, text_color=c("text"))
        self._messages.append({"role": "assistant", "content": summary})
        info = f"Змінено файлів: {len(set(changed))}" if changed else "Файли не змінювались"
        ctk.CTkLabel(msg["foot"], text=info, font=font(11),
                     text_color=c("text_faint")).pack(side="left")
        ctk.CTkButton(msg["foot"], text="Відкрити папку", width=120, height=24, font=font(11),
                      image=icon("folder", c("text_faint"), 13), compound="left",
                      fg_color="transparent", hover_color=c("hover"), text_color=c("text_faint"),
                      corner_radius=8, command=lambda: open_folder(folder)
                      ).pack(side="left", padx=(8, 0))
        self._scroll_end()

    def _ask(self, tool_name: str, description: str) -> bool:
        """Called from the agent thread: in-app dialog on the UI thread, wait for the answer."""
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


# ── helpers ───────────────────────────────────────────────────────────────────

def desktop_dir() -> Path:
    """Real Desktop folder (may be inside OneDrive on Windows)."""
    if sys.platform == "win32":
        try:
            import winreg
            key = winreg.OpenKey(winreg.HKEY_CURRENT_USER,
                                 r"Software\Microsoft\Windows\CurrentVersion\Explorer\User Shell Folders")
            return Path(os.path.expandvars(winreg.QueryValueEx(key, "Desktop")[0]))
        except OSError:
            pass
    return Path.home() / "Desktop"


def open_folder(path: str):
    try:
        if sys.platform == "win32":
            os.startfile(path)  # noqa: S606
        else:
            import subprocess
            subprocess.Popen(["xdg-open", path])
    except Exception:
        pass
