"""Compare two models side-by-side on the same prompt."""
import threading
import time
import customtkinter as ctk
from ui.theme import c, RADIUS
from core.ollama_client import chat_stream, list_local_models


class CompareView(ctk.CTkFrame):
    def __init__(self, master, **kw):
        super().__init__(master, fg_color=c("bg"), **kw)
        self._stop_events = [threading.Event(), threading.Event()]
        self._build_ui()

    def _build_ui(self):
        # title
        ctk.CTkLabel(
            self,
            text="Порівняння моделей",
            font=ctk.CTkFont(family="Inter", size=20, weight="bold"),
            text_color=c("text"),
        ).pack(anchor="w", padx=20, pady=(16, 8))

        # model selectors
        sel_frame = ctk.CTkFrame(self, fg_color="transparent")
        sel_frame.pack(fill="x", padx=20, pady=(0, 8))

        local = [m["name"] for m in list_local_models()]
        if not local:
            local = ["(немає локальних моделей)"]

        self._selectors: list[ctk.CTkOptionMenu] = []
        self._tps_labels: list[ctk.CTkLabel] = []

        for i in range(2):
            col = ctk.CTkFrame(sel_frame, fg_color="transparent")
            col.pack(side="left", fill="x", expand=True, padx=(0 if i else 0, 8 if i == 0 else 0))

            opt = ctk.CTkOptionMenu(
                col,
                values=local,
                width=260,
                corner_radius=RADIUS["button"],
                font=ctk.CTkFont(family="Inter", size=12),
            )
            opt.pack(fill="x")
            self._selectors.append(opt)

            tps = ctk.CTkLabel(
                col,
                text="",
                font=ctk.CTkFont(family="Inter", size=11),
                text_color=c("text_dim"),
            )
            tps.pack(anchor="w", pady=(2, 0))
            self._tps_labels.append(tps)

        # chat boxes
        boxes_frame = ctk.CTkFrame(self, fg_color="transparent")
        boxes_frame.pack(fill="both", expand=True, padx=20)

        self._boxes: list[ctk.CTkTextbox] = []
        for _ in range(2):
            box = ctk.CTkTextbox(
                boxes_frame,
                fg_color=c("panel"),
                font=ctk.CTkFont(family="Inter", size=13),
                wrap="word",
                state="disabled",
                corner_radius=RADIUS["card"],
            )
            box.pack(side="left", fill="both", expand=True, padx=4)
            self._boxes.append(box)

        # input
        inp_frame = ctk.CTkFrame(self, fg_color="transparent")
        inp_frame.pack(fill="x", padx=20, pady=12)

        self._input = ctk.CTkEntry(
            inp_frame,
            placeholder_text="Введіть питання для обох моделей…",
            fg_color=c("input_bg"),
            border_color=c("border"),
            corner_radius=RADIUS["input"],
            font=ctk.CTkFont(family="Inter", size=13),
        )
        self._input.pack(side="left", fill="x", expand=True)
        self._input.bind("<Return>", self._ask_both)

        ctk.CTkButton(
            inp_frame,
            text="Запитати обидві",
            width=140,
            height=38,
            corner_radius=RADIUS["button"],
            command=self._ask_both,
        ).pack(side="left", padx=(8, 0))

        ctk.CTkButton(
            inp_frame,
            text="⏹",
            width=44,
            height=38,
            fg_color=c("danger"),
            hover_color="#CC2F27",
            corner_radius=RADIUS["button"],
            command=self._stop_all,
        ).pack(side="left", padx=(6, 0))

    def _ask_both(self, event=None):
        prompt = self._input.get().strip()
        if not prompt:
            return
        self._input.delete(0, "end")

        for i in range(2):
            self._stop_events[i].clear()
            self._clear_box(i)
            model = self._selectors[i].get()
            if "немає" in model:
                continue
            self._tps_labels[i].configure(text="…")
            threading.Thread(
                target=self._stream_one,
                args=(i, model, prompt),
                daemon=True,
            ).start()

    def _stream_one(self, idx: int, model: str, prompt: str):
        messages = [{"role": "user", "content": prompt}]
        t0 = time.time()
        tokens = 0
        try:
            for token in chat_stream(model, messages):
                if self._stop_events[idx].is_set():
                    break
                self.after(0, self._append_box, idx, token)
                tokens += 1
        except Exception as e:
            self.after(0, self._append_box, idx, f"\n[Помилка: {e}]")
            return
        elapsed = time.time() - t0
        tps = tokens / elapsed if elapsed > 0 else 0
        self.after(0, self._tps_labels[idx].configure, {"text": f"{tps:.1f} tok/s"})

    def _clear_box(self, idx: int):
        b = self._boxes[idx]
        b.configure(state="normal")
        b.delete("1.0", "end")
        b.configure(state="disabled")

    def _append_box(self, idx: int, text: str):
        b = self._boxes[idx]
        b.configure(state="normal")
        b.insert("end", text)
        b.see("end")
        b.configure(state="disabled")

    def _stop_all(self):
        for e in self._stop_events:
            e.set()
