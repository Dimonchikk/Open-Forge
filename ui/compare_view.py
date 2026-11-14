"""Compare two models side by side on the same prompt."""
import re
import threading
import time
import customtkinter as ctk

from core.ollama_client import chat_stream, tokens_per_second
from ui import bg
from ui.icons import icon
from ui.theme import c, font, RADIUS, primary_button, option_menu, page_header

_THINK_RE = re.compile(r"<think>.*?(</think>|$)", re.S)


class _Side(ctk.CTkFrame):
    def __init__(self, master, letter: str):
        super().__init__(master, fg_color=c("card"), corner_radius=RADIUS["card"],
                         border_width=1, border_color=c("border"))
        self.grid_columnconfigure(0, weight=1)
        self.grid_rowconfigure(1, weight=1)
        head = ctk.CTkFrame(self, fg_color="transparent")
        head.grid(row=0, column=0, sticky="ew", padx=14, pady=(14, 8))
        head.grid_columnconfigure(1, weight=1)
        ctk.CTkLabel(head, text=letter, width=26, height=26, corner_radius=13,
                     fg_color=c("accent_soft"), text_color=c("accent"),
                     font=font(12, "bold")).grid(row=0, column=0)
        self.picker = option_menu(head, ["—"], width=220)
        self.picker.grid(row=0, column=1, sticky="ew", padx=(10, 10))
        self.stat = ctk.CTkLabel(head, text="", font=font(12, "bold"), text_color=c("text_dim"))
        self.stat.grid(row=0, column=2)

        self.box = ctk.CTkTextbox(self, fg_color="transparent", font=font(14), wrap="word",
                                  text_color=c("text"), border_width=0)
        self.box.grid(row=1, column=0, sticky="nsew", padx=10, pady=(0, 10))
        self.box.configure(state="disabled")
        self.buf: list[str] = []
        self.stats: dict = {}
        self.running = False
        self.error = None

    def set_text(self, text: str, color=None):
        self.box.configure(state="normal")
        self.box.delete("1.0", "end")
        self.box.insert("1.0", text)
        self.box.configure(state="disabled", text_color=color or c("text"))
        self.box.see("end")


class CompareView(ctk.CTkFrame):
    def __init__(self, master, app, **kw):
        super().__init__(master, fg_color=c("bg"), corner_radius=0, **kw)
        self.app = app
        self._stop = threading.Event()
        self._build()

    def _build(self):
        self.grid_columnconfigure((0, 1), weight=1, uniform="side")
        self.grid_rowconfigure(1, weight=1)
        page_header(self, "Порівняння",
                    "Одне питання — дві моделі поруч. Видно, яка розумніша і швидша саме для тебе."
                    ).grid(row=0, column=0, columnspan=2, sticky="w", padx=28, pady=(22, 14))

        self._sides = [_Side(self, "A"), _Side(self, "B")]
        self._sides[0].grid(row=1, column=0, sticky="nsew", padx=(28, 8))
        self._sides[1].grid(row=1, column=1, sticky="nsew", padx=(8, 28))

        bar = ctk.CTkFrame(self, fg_color=c("card"), corner_radius=20, border_width=1,
                           border_color=c("border"))
        bar.grid(row=2, column=0, columnspan=2, sticky="ew", padx=28, pady=(14, 18))
        bar.grid_columnconfigure(0, weight=1)
        self._input = ctk.CTkEntry(bar, height=40, border_width=0, fg_color="transparent",
                                   font=font(14), text_color=c("text"),
                                   placeholder_text="Питання для обох моделей…")
        self._input.grid(row=0, column=0, sticky="ew", padx=(14, 8), pady=6)
        self._input.bind("<Return>", lambda e: self._go())
        self._btn = primary_button(bar, "Порівняти", width=140, height=36, corner_radius=18,
                                   image=icon("compare", "#FFFFFF", 15), compound="left",
                                   command=self._go)
        self._btn.grid(row=0, column=1, padx=(0, 8), pady=6)

    def set_models(self, names: list[str], ollama_ok: bool):
        vals = names or (["Ollama не запущена"] if not ollama_ok else ["Немає моделей"])
        for i, s in enumerate(self._sides):
            cur = s.picker.get()
            s.picker.configure(values=vals, state="normal" if names else "disabled")
            if cur in names:
                s.picker.set(cur)
            else:
                s.picker.set(vals[min(i, len(vals) - 1)])

    # ── run ───────────────────────────────────────────────────────────────────

    def _busy(self) -> bool:
        return any(s.running for s in self._sides)

    def _go(self):
        if self._busy():
            self._stop.set()
            return
        prompt = self._input.get().strip()
        models = [s.picker.get() for s in self._sides]
        if not prompt or any(m.startswith(("Немає", "Ollama", "—")) for m in models):
            return
        self._stop.clear()
        for s, model in zip(self._sides, models):
            s.buf, s.stats, s.error, s.running = [], {}, None, True
            s.set_text("Думає…", c("text_faint"))
            s.stat.configure(text="")
            threading.Thread(target=self._worker, args=(s, model, prompt), daemon=True).start()
        self._btn.configure(text="Зупинити", image=icon("stop", "#FFFFFF", 15),
                            fg_color=c("text"), hover_color=c("text_dim"))
        self._loop()

    def _worker(self, side: _Side, model: str, prompt: str):
        t0 = time.time()
        try:
            for tok in chat_stream(model, [{"role": "user", "content": prompt}],
                                   stats=side.stats):
                if self._stop.is_set():
                    break
                side.buf.append(tok)
        except Exception as e:  # noqa: BLE001
            side.error = str(e)
        side.stats["wall"] = time.time() - t0
        side.running = False

    def _loop(self):
        for s in self._sides:
            text = _THINK_RE.sub("", "".join(s.buf)).lstrip()
            if s.error and not text:
                s.set_text(f"Помилка: {s.error}", c("danger"))
            elif text:
                s.set_text(text)
            if not s.running:
                tps = tokens_per_second(s.stats)
                if tps:
                    s.stat.configure(text=f"{tps:.1f} ток/с")
        if self._busy():
            self.after(80, self._loop)
        else:
            self._btn.configure(text="Порівняти", image=icon("compare", "#FFFFFF", 15),
                                fg_color=c("accent"), hover_color=c("accent_hover"))
