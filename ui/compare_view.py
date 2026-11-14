"""Compare 2–3 models side by side on the same prompt."""
import re
import threading
import time
import customtkinter as ctk

from core.ollama_client import chat_stream, tokens_per_second
from ui.icons import icon
from ui.theme import (c, font, RADIUS, primary_button, ghost_button, option_menu, page_header,
                      badge)

_THINK_RE = re.compile(r"<think>.*?(</think>|$)", re.S)
LETTERS = "ABC"
MAX_SIDES = 3


class _Side(ctk.CTkFrame):
    def __init__(self, master, view, idx: int):
        super().__init__(master, fg_color=c("card"), corner_radius=RADIUS["card"],
                         border_width=1, border_color=c("border"))
        self.view = view
        self.grid_columnconfigure(0, weight=1)
        self.grid_rowconfigure(1, weight=1)

        head = ctk.CTkFrame(self, fg_color="transparent")
        head.grid(row=0, column=0, sticky="ew", padx=14, pady=(14, 6))
        head.grid_columnconfigure(1, weight=1)
        self.letter = ctk.CTkLabel(head, text=LETTERS[idx], width=26, height=26, corner_radius=13,
                                   fg_color=c("accent_soft"), text_color=c("accent"),
                                   font=font(12, "bold"))
        self.letter.grid(row=0, column=0)
        self.picker = option_menu(head, ["—"], width=180)
        self.picker.grid(row=0, column=1, sticky="ew", padx=(10, 0))
        self.close = ctk.CTkButton(head, text="", width=30, height=30, corner_radius=15,
                                   image=icon("x", c("text_faint"), 14), fg_color="transparent",
                                   hover_color=c("hover"), command=lambda: view.remove_side(self))

        self.scroll = ctk.CTkScrollableFrame(self, fg_color="transparent",
                                             scrollbar_button_color=c("border"))
        self.scroll.grid(row=1, column=0, sticky="nsew", padx=4)
        self.scroll.grid_columnconfigure(0, weight=1)
        self.answer = ctk.CTkLabel(self.scroll, text="", font=font(14), text_color=c("text_faint"),
                                   justify="left", anchor="nw", wraplength=300)
        self.answer.grid(row=0, column=0, sticky="nw", padx=10, pady=4)
        self.bind("<Configure>", lambda e: self.answer.configure(wraplength=max(e.width - 48, 160)))

        self.foot = ctk.CTkFrame(self, fg_color="transparent", height=34)
        self.foot.grid(row=2, column=0, sticky="ew", padx=14, pady=(4, 12))
        self.stat = ctk.CTkLabel(self.foot, text="", font=font(12), text_color=c("text_dim"),
                                 anchor="w")
        self.stat.grid(row=0, column=0, sticky="w")
        self.win = badge(self.foot, "Найшвидша", "success")
        self.reset()

    def reset(self):
        self.buf, self.stats, self.error, self.running = [], {}, None, False
        self.t0 = self.t_first = self.t_end = None
        self.stat.configure(text="")
        self.win.grid_forget()

    def show_close(self, on: bool):
        if on:
            self.close.grid(row=0, column=2, padx=(6, 0))
        else:
            self.close.grid_forget()

    def set_answer(self, text: str, color="text"):
        self.answer.configure(text=text, text_color=c(color))


class CompareView(ctk.CTkFrame):
    def __init__(self, master, app, **kw):
        super().__init__(master, fg_color=c("bg"), corner_radius=0, **kw)
        self.app = app
        self._stop = threading.Event()
        self._names: list[str] = []
        self._ok = True
        self._sides: list[_Side] = []
        self._build()

    def _build(self):
        self.grid_columnconfigure(0, weight=1)
        self.grid_rowconfigure(1, weight=1)
        top = ctk.CTkFrame(self, fg_color="transparent")
        top.grid(row=0, column=0, sticky="ew", padx=28, pady=(22, 14))
        top.grid_columnconfigure(0, weight=1)
        page_header(top, "Порівняння", "Одне питання — кілька моделей поруч. Видно, яка "
                                        "розумніша і швидша саме на твоєму ПК."
                    ).grid(row=0, column=0, sticky="w")
        self._add_btn = ghost_button(top, "Додати модель", width=150, height=34,
                                     image=icon("plus", c("text_dim"), 15), compound="left",
                                     command=self.add_side)
        self._add_btn.grid(row=0, column=1, sticky="e")

        self._grid = ctk.CTkFrame(self, fg_color="transparent")
        self._grid.grid(row=1, column=0, sticky="nsew", padx=20)
        self._grid.grid_rowconfigure(0, weight=1)

        self._empty = ctk.CTkFrame(self, fg_color="transparent")

        self._bar = ctk.CTkFrame(self, fg_color=c("card"), corner_radius=22, border_width=1,
                                 border_color=c("border"))
        self._bar.grid(row=2, column=0, sticky="ew", padx=28, pady=(14, 18))
        self._bar.grid_columnconfigure(0, weight=1)
        self._input = ctk.CTkEntry(self._bar, height=40, border_width=0, fg_color="transparent",
                                   font=font(14), text_color=c("text"),
                                   placeholder_text="Питання для всіх моделей…")
        self._input.grid(row=0, column=0, sticky="ew", padx=(16, 8), pady=6)
        self._input.bind("<Return>", lambda e: self._go())
        self._btn = primary_button(self._bar, "Порівняти", width=140, height=36, corner_radius=18,
                                   image=icon("compare", "#FFFFFF", 15), compound="left",
                                   command=self._go)
        self._btn.grid(row=0, column=1, padx=(0, 8), pady=6)

        self.add_side()
        self.add_side()

    # ── sides ─────────────────────────────────────────────────────────────────

    def add_side(self):
        if len(self._sides) >= MAX_SIDES or self._busy():
            return
        s = _Side(self._grid, self, len(self._sides))
        self._sides.append(s)
        self._fill_picker(s, len(self._sides) - 1)
        self._layout()

    def remove_side(self, side: _Side):
        if len(self._sides) <= 2 or self._busy():
            return
        self._sides.remove(side)
        side.destroy()
        self._layout()

    def _layout(self):
        n = len(self._sides)
        for i in range(MAX_SIDES):
            self._grid.grid_columnconfigure(i, weight=1 if i < n else 0, uniform="side" if i < n else "")
        for i, s in enumerate(self._sides):
            s.grid(row=0, column=i, sticky="nsew", padx=8)
            s.letter.configure(text=LETTERS[i])
            s.show_close(n > 2)
        if n >= MAX_SIDES or len(self._names) <= n:
            self._add_btn.grid_remove()
        else:
            self._add_btn.grid()

    def _fill_picker(self, s: _Side, i: int):
        names = self._names or ["—"]
        cur = s.picker.get()
        s.picker.configure(values=names, state="normal" if self._names else "disabled")
        s.picker.set(cur if cur in self._names else names[min(i, len(names) - 1)])

    # ── data from App ─────────────────────────────────────────────────────────

    def set_models(self, names: list[str], ollama_ok: bool):
        self._names, self._ok = names, ollama_ok
        for i, s in enumerate(self._sides):
            self._fill_picker(s, i)
        self._layout()
        self._render_empty()

    def _render_empty(self):
        enough = self._ok and len(self._names) >= 2
        if enough:
            self._empty.grid_forget()
            self._grid.grid(row=1, column=0, sticky="nsew", padx=20)
            self._bar.grid()
            return
        self._grid.grid_forget()
        self._bar.grid_remove()
        self._add_btn.grid_remove()
        for w in self._empty.winfo_children():
            w.destroy()
        self._empty.grid(row=1, column=0, sticky="nsew")
        box = ctk.CTkFrame(self._empty, fg_color="transparent")
        box.place(relx=0.5, rely=0.42, anchor="center")
        ctk.CTkLabel(box, text="", image=icon("compare", c("accent"), 44)).pack(pady=(0, 12))
        if not self._ok:
            title = "Ollama не запущена"
            text = "Порівняння запрацює, щойно Ollama буде запущена."
            btn = ("Налаштувати Ollama", "setup")
        else:
            n = len(self._names)
            title = "Потрібні хоча б дві моделі"
            text = (f"Зараз встановлено: {n}. Додай ще одну — наприклад, звичайну й без цензури "
                    "однакового розміру, щоб побачити різницю.")
            btn = ("Відкрити каталог моделей", "models")
        ctk.CTkLabel(box, text=title, font=font(22, "bold"), text_color=c("text")).pack()
        ctk.CTkLabel(box, text=text, font=font(14), text_color=c("text_dim"), wraplength=460,
                     justify="center").pack(pady=(6, 18))
        primary_button(box, btn[0], width=240, height=40,
                       command=lambda: self.app.show(btn[1])).pack()

    # ── run ───────────────────────────────────────────────────────────────────

    def _busy(self) -> bool:
        return any(s.running for s in self._sides)

    def _go(self):
        if self._busy():
            self._stop.set()
            return
        prompt = self._input.get().strip()
        if not prompt or len(self._names) < 1:
            return
        self._stop.clear()
        for s in self._sides:
            s.reset()
            s.running = True
            s.set_answer("Думає…", "text_faint")
            threading.Thread(target=self._worker, args=(s, s.picker.get(), prompt),
                             daemon=True).start()
        self._btn.configure(text="Зупинити", image=icon("stop", c("card"), 15), text_color=c("card"),
                            fg_color=c("text"), hover_color=c("text_dim"))
        self._add_btn.configure(state="disabled")
        self._loop()

    def _worker(self, side: _Side, model: str, prompt: str):
        side.t0 = time.time()
        try:
            for tok in chat_stream(model, [{"role": "user", "content": prompt}], stats=side.stats):
                if self._stop.is_set():
                    break
                if side.t_first is None:
                    side.t_first = time.time()
                side.buf.append(tok)
        except Exception as e:  # noqa: BLE001
            side.error = str(e)
        side.t_end = time.time()
        side.running = False

    def _loop(self):
        for s in self._sides:
            text = _THINK_RE.sub("", "".join(s.buf)).strip()
            if s.error and not text:
                s.set_answer(f"Помилка: {s.error}", "danger")
            elif text:
                s.set_answer(text)
            if not s.running and s.t_end and not s.stat.cget("text"):
                tps = tokens_per_second(s.stats)
                parts = []
                if tps:
                    parts.append(f"{tps:.1f} ток/с")
                parts.append(f"{s.t_end - s.t0:.1f} с")
                if s.stats.get("eval_count"):
                    parts.append(f"{s.stats['eval_count']} токенів")
                s.stat.configure(text=" · ".join(parts))
        if self._busy():
            self.after(80, self._loop)
            return
        self._btn.configure(text="Порівняти", image=icon("compare", "#FFFFFF", 15),
                            text_color="#FFFFFF", fg_color=c("accent"), hover_color=c("accent_hover"))
        self._add_btn.configure(state="normal")
        speeds = [(tokens_per_second(s.stats), s) for s in self._sides if not s.error]
        speeds = [x for x in speeds if x[0]]
        if len(speeds) >= 2:
            max(speeds, key=lambda x: x[0])[1].win.grid(row=1, column=0, sticky="w", pady=(4, 0))
