"""
Open Forge — local AI launcher
Entry point: python main.py
"""
import sys
import tkinter as tk
import customtkinter as ctk

from ui.theme import apply_theme, c, RADIUS
from ui.chat_view import ChatView
from ui.catalog_view import CatalogView
from ui.compare_view import CompareView
from ui.agent_view import AgentView
from ui.hardware_panel import HardwarePanel
from core.ollama_client import is_ollama_running


# ── sidebar navigation ────────────────────────────────────────────────────────

NAV_ITEMS = [
    ("💬", "Чат"),
    ("📦", "Моделі"),
    ("⚡", "Порівняння"),
    ("🤖", "Агент"),
]


class Sidebar(ctk.CTkFrame):
    def __init__(self, master, on_select, **kw):
        super().__init__(master, fg_color=c("sidebar"), corner_radius=0, width=200, **kw)
        self.pack_propagate(False)
        self._on_select = on_select
        self._buttons: list[ctk.CTkButton] = []
        self._active = 0

        # logo
        logo = ctk.CTkLabel(
            self,
            text="⚒ Open Forge",
            font=ctk.CTkFont(family="Inter", size=16, weight="bold"),
            text_color=c("text"),
        )
        logo.pack(pady=(24, 20), padx=16, anchor="w")

        for i, (icon, label) in enumerate(NAV_ITEMS):
            btn = ctk.CTkButton(
                self,
                text=f"  {icon}  {label}",
                anchor="w",
                fg_color="transparent",
                text_color=c("text"),
                hover_color=c("card_hover"),
                corner_radius=RADIUS["button"],
                height=40,
                font=ctk.CTkFont(family="Inter", size=13),
                command=lambda idx=i: self._select(idx),
            )
            btn.pack(fill="x", padx=8, pady=2)
            self._buttons.append(btn)

        self._select(0)

    def _select(self, idx: int):
        self._active = idx
        for i, btn in enumerate(self._buttons):
            if i == idx:
                btn.configure(
                    fg_color=c("accent"),
                    text_color="#FFFFFF",
                    hover_color=c("accent_hover"),
                )
            else:
                btn.configure(
                    fg_color="transparent",
                    text_color=c("text"),
                    hover_color=c("card_hover"),
                )
        self._on_select(idx)


# ── main window ───────────────────────────────────────────────────────────────

class App(ctk.CTk):
    def __init__(self):
        super().__init__()
        apply_theme()

        self.title("Open Forge")
        self.geometry("1200x760")
        self.minsize(900, 600)

        # ── check Ollama ──────────────────────────────────────────────────────
        if not is_ollama_running():
            self._show_ollama_warning()

        # ── layout ────────────────────────────────────────────────────────────
        self.grid_columnconfigure(1, weight=1)
        self.grid_rowconfigure(0, weight=1)

        # sidebar (left)
        self._sidebar = Sidebar(self, on_select=self._show_view)
        self._sidebar.grid(row=0, column=0, sticky="nsw")

        # content area
        self._content = ctk.CTkFrame(self, fg_color=c("bg"), corner_radius=0)
        self._content.grid(row=0, column=1, sticky="nsew")
        self._content.grid_rowconfigure(0, weight=1)
        self._content.grid_columnconfigure(0, weight=1)

        # hardware panel (right)
        self._hw_panel = HardwarePanel(self)
        self._hw_panel.grid(row=0, column=2, sticky="nse")

        # ── views ─────────────────────────────────────────────────────────────
        self._chat_view    = ChatView(self._content)
        self._catalog_view = CatalogView(self._content, on_model_select=self._on_model_selected)
        self._compare_view = CompareView(self._content)
        self._agent_view   = AgentView(self._content)

        self._views = [
            self._chat_view,
            self._catalog_view,
            self._compare_view,
            self._agent_view,
        ]
        for v in self._views:
            v.grid(row=0, column=0, sticky="nsew")

        self._show_view(0)

        self.protocol("WM_DELETE_WINDOW", self._on_close)

    def _show_view(self, idx: int):
        for i, v in enumerate(self._views):
            if i == idx:
                v.tkraise()

    def _on_model_selected(self, model_id: str):
        self._chat_view.set_model(model_id)
        self._show_view(0)
        self._sidebar._select(0)

    def _show_ollama_warning(self):
        win = ctk.CTkToplevel(self)
        win.title("Ollama не запущена")
        win.geometry("420x180")
        win.grab_set()
        ctk.CTkLabel(
            win,
            text="⚠️  Ollama не знайдена на цьому комп'ютері.\n\n"
                 "Будь ласка, встанови та запусти Ollama:\nhttps://ollama.com",
            font=ctk.CTkFont(family="Inter", size=13),
            justify="center",
        ).pack(expand=True, pady=20)
        ctk.CTkButton(win, text="OK", command=win.destroy).pack(pady=(0, 16))

    def _on_close(self):
        self._hw_panel.stop()
        self.destroy()


# ── run ───────────────────────────────────────────────────────────────────────

if __name__ == "__main__":
    app = App()
    app.mainloop()
