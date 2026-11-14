"""Main chat view with streaming response, tokens/sec, system prompt."""
import threading
import time
import customtkinter as ctk
from ui.theme import c, RADIUS
from core.ollama_client import chat_stream


class ChatView(ctk.CTkFrame):
    def __init__(self, master, **kw):
        super().__init__(master, fg_color=c("bg"), **kw)
        self._model: str = ""
        self._messages: list[dict] = []
        self._system_prompt: str = ""
        self._stop_event = threading.Event()
        self._build_ui()

    # ── UI ────────────────────────────────────────────────────────────────────

    def _build_ui(self):
        # top bar
        top = ctk.CTkFrame(self, fg_color="transparent", height=48)
        top.pack(fill="x", padx=16, pady=(10, 0))
        top.pack_propagate(False)

        self._model_label = ctk.CTkLabel(
            top,
            text="Модель не вибрана",
            font=ctk.CTkFont(family="Inter", size=13, weight="bold"),
            text_color=c("text"),
            anchor="w",
        )
        self._model_label.pack(side="left")

        self._tps_label = ctk.CTkLabel(
            top,
            text="",
            font=ctk.CTkFont(family="Inter", size=12),
            text_color=c("text_dim"),
        )
        self._tps_label.pack(side="right", padx=(0, 8))

        # system prompt collapsible
        sys_frame = ctk.CTkFrame(self, fg_color=c("card"), corner_radius=RADIUS["input"])
        sys_frame.pack(fill="x", padx=16, pady=(8, 0))

        ctk.CTkLabel(
            sys_frame,
            text="Системний промпт (опціонально)",
            font=ctk.CTkFont(family="Inter", size=11),
            text_color=c("text_dim"),
        ).pack(anchor="w", padx=12, pady=(6, 0))

        self._sys_entry = ctk.CTkEntry(
            sys_frame,
            placeholder_text="Наприклад: Ти корисний асистент. Відповідай українською.",
            fg_color=c("input_bg"),
            border_color=c("border"),
            corner_radius=RADIUS["input"],
            font=ctk.CTkFont(family="Inter", size=12),
        )
        self._sys_entry.pack(fill="x", padx=12, pady=(4, 10))

        # chat history
        self._chat_box = ctk.CTkTextbox(
            self,
            fg_color=c("bg"),
            font=ctk.CTkFont(family="Inter", size=13),
            wrap="word",
            state="disabled",
        )
        self._chat_box.pack(fill="both", expand=True, padx=16, pady=(10, 0))

        # configure text tags
        # CTkTextbox.tag_config forbids `font`, so configure the inner tk.Text directly.
        tb = self._chat_box._textbox
        tb.tag_configure("user", foreground=c("text_accent")[1], font=("Inter", 13, "bold"))
        tb.tag_configure("ai",   foreground=c("text")[1])
        tb.tag_configure("meta", foreground=c("text_dim")[1], font=("Inter", 11))

        # input row
        input_frame = ctk.CTkFrame(self, fg_color="transparent")
        input_frame.pack(fill="x", padx=16, pady=12)

        self._input = ctk.CTkEntry(
            input_frame,
            placeholder_text="Введіть повідомлення…",
            fg_color=c("input_bg"),
            border_color=c("border"),
            corner_radius=RADIUS["input"],
            font=ctk.CTkFont(family="Inter", size=13),
        )
        self._input.pack(side="left", fill="x", expand=True)
        self._input.bind("<Return>", self._send)

        self._send_btn = ctk.CTkButton(
            input_frame,
            text="↑",
            width=44,
            height=38,
            corner_radius=RADIUS["button"],
            command=self._send,
        )
        self._send_btn.pack(side="left", padx=(8, 0))

        self._stop_btn = ctk.CTkButton(
            input_frame,
            text="⏹",
            width=44,
            height=38,
            fg_color=c("danger"),
            hover_color="#CC2F27",
            corner_radius=RADIUS["button"],
            command=self._stop,
        )
        self._stop_btn.pack(side="left", padx=(6, 0))

        # clear button
        ctk.CTkButton(
            input_frame,
            text="Очистити",
            width=80,
            height=38,
            fg_color=c("card"),
            text_color=c("text"),
            hover_color=c("card_hover"),
            corner_radius=RADIUS["button"],
            command=self._clear,
        ).pack(side="left", padx=(6, 0))

    # ── public ────────────────────────────────────────────────────────────────

    def set_model(self, model_id: str):
        self._model = model_id
        self._model_label.configure(text=f"● {model_id}")

    # ── actions ───────────────────────────────────────────────────────────────

    def _send(self, event=None):
        text = self._input.get().strip()
        if not text or not self._model:
            return
        self._input.delete(0, "end")
        sys_prompt = self._sys_entry.get().strip()

        self._messages.append({"role": "user", "content": text})
        self._append(f"Ти: {text}\n", "user")
        self._append("\n", "ai")

        self._stop_event.clear()
        self._tps_label.configure(text="…")

        threading.Thread(
            target=self._stream_response,
            args=(sys_prompt,),
            daemon=True,
        ).start()

    def _stream_response(self, system: str):
        t0 = time.time()
        total_tokens = 0
        ai_start = self._chat_box.index("end-1c")

        try:
            gen = chat_stream(self._model, self._messages, system=system)
            for token in gen:
                if self._stop_event.is_set():
                    break
                self.after(0, self._append, token, "ai")
                total_tokens += 1
        except Exception as e:
            self.after(0, self._append, f"\n[Помилка: {e}]\n", "meta")
            return

        elapsed = time.time() - t0
        tps = total_tokens / elapsed if elapsed > 0 else 0
        self.after(0, self._tps_label.configure, {"text": f"{tps:.1f} tok/s"})

        # save assistant message
        content = self._chat_box.get(ai_start, "end-1c").strip()
        self._messages.append({"role": "assistant", "content": content})
        self.after(0, self._append, "\n\n", "ai")

    def _stop(self):
        self._stop_event.set()

    def _clear(self):
        self._messages.clear()
        self._chat_box.configure(state="normal")
        self._chat_box.delete("1.0", "end")
        self._chat_box.configure(state="disabled")
        self._tps_label.configure(text="")

    def _append(self, text: str, tag: str = "ai"):
        self._chat_box.configure(state="normal")
        self._chat_box.insert("end", text, tag)
        self._chat_box.see("end")
        self._chat_box.configure(state="disabled")
