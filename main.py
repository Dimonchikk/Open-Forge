"""
Open Forge — local AI launcher
Entry point: python main.py
"""
import json
import sys
import urllib.request
import webbrowser
import customtkinter as ctk

from core import settings
from core.ollama_client import is_ollama_running, list_local_models, OLLAMA_DOWNLOAD_URL
from core.settings import resource_path
from ui import bg
from ui.icons import icon, logo
from ui.theme import apply_theme, c, font, RADIUS
from ui.chat_view import ChatView
from ui.catalog_view import CatalogView
from ui.compare_view import CompareView
from ui.agent_view import AgentView
from ui.settings_view import SettingsView
from ui.hardware_panel import HardwarePanel
from version import VERSION

RELEASES_API = "https://api.github.com/repos/Dimonchikk/Open-Forge/releases/latest"

NAV = [
    ("chat", "chat", "Чат"),
    ("models", "models", "Моделі"),
    ("compare", "compare", "Порівняння"),
    ("agent", "agent", "Агент"),
]


class Sidebar(ctk.CTkFrame):
    def __init__(self, master, app):
        super().__init__(master, fg_color=c("sidebar"), corner_radius=0, width=224)
        self.grid_propagate(False)
        self.app = app
        self._buttons: dict[str, ctk.CTkButton] = {}
        self.grid_columnconfigure(0, weight=1)
        self.grid_rowconfigure(len(NAV) + 1, weight=1)

        brand = ctk.CTkFrame(self, fg_color="transparent")
        brand.grid(row=0, column=0, sticky="ew", padx=18, pady=(22, 18))
        ctk.CTkLabel(brand, text="", image=logo(30)).pack(side="left")
        ctk.CTkLabel(brand, text="Open Forge", font=font(17, "bold"),
                     text_color=c("text")).pack(side="left", padx=(10, 0))

        for i, (key, ico, label) in enumerate(NAV):
            self._add(key, ico, label).grid(row=1 + i, column=0, sticky="ew", padx=12, pady=2)

        bottom = ctk.CTkFrame(self, fg_color="transparent")
        bottom.grid(row=len(NAV) + 2, column=0, sticky="ew", padx=12, pady=(0, 16))
        bottom.grid_columnconfigure(0, weight=1)

        self.update_btn = ctk.CTkButton(
            bottom, text="", height=36, corner_radius=RADIUS["button"], anchor="w",
            fg_color=c("accent_soft"), hover_color=c("hover"), text_color=c("accent"),
            font=font(13, "bold"), image=icon("download", c("accent"), 16), compound="left")

        self.status = ctk.CTkButton(
            bottom, text="  Підключення…", height=32, anchor="w", corner_radius=RADIUS["button"],
            fg_color="transparent", hover_color=c("hover"), text_color=c("text_dim"),
            font=font(12), image=icon("dot", c("text_faint"), 10), compound="left",
            command=self._status_click)
        self.status.grid(row=1, column=0, sticky="ew", pady=(0, 4))
        self._ollama_ok = None

        self._add("settings", "settings", "Налаштування", parent=bottom).grid(
            row=2, column=0, sticky="ew")
        ctk.CTkLabel(bottom, text=f"версія {VERSION}", font=font(11),
                     text_color=c("text_faint")).grid(row=3, column=0, sticky="w",
                                                      padx=14, pady=(6, 0))

    def _add(self, key, ico, label, parent=None):
        btn = ctk.CTkButton(
            parent or self, text=f"  {label}", height=40, anchor="w",
            corner_radius=RADIUS["button"], font=font(14),
            fg_color="transparent", hover_color=c("hover"), text_color=c("text"),
            image=icon(ico, c("text_dim"), 18), compound="left",
            command=lambda: self.app.show(key))
        btn._icon = ico
        self._buttons[key] = btn
        return btn

    def select(self, key: str):
        for k, b in self._buttons.items():
            on = k == key
            b.configure(fg_color=c("accent_soft") if on else "transparent",
                        text_color=c("accent") if on else c("text"),
                        font=font(14, "bold" if on else "normal"),
                        image=icon(b._icon, c("accent") if on else c("text_dim"), 18))

    def set_ollama(self, ok: bool):
        self._ollama_ok = ok
        self.status.configure(
            text="  Ollama працює" if ok else "  Ollama не запущена",
            text_color=c("text_dim") if ok else c("warning"),
            image=icon("dot", c("success") if ok else c("warning"), 10))

    def _status_click(self):
        if self._ollama_ok is False:
            webbrowser.open(OLLAMA_DOWNLOAD_URL)
        else:
            self.app.refresh_models()

    def show_update(self, version: str, url: str):
        self.update_btn.configure(text=f"  Оновлення {version}", command=lambda: webbrowser.open(url))
        self.update_btn.grid(row=0, column=0, sticky="ew", pady=(0, 8))


class App(ctk.CTk):
    def __init__(self):
        apply_theme(settings.get("theme"))
        super().__init__()
        self.title("Open Forge")
        self.geometry("1320x820")
        self.minsize(1040, 640)
        self.configure(fg_color=c("bg"))
        self._set_icon()
        bg.start(self)

        self.grid_columnconfigure(1, weight=1)
        self.grid_rowconfigure(0, weight=1)

        self.sidebar = Sidebar(self, self)
        self.sidebar.grid(row=0, column=0, sticky="nsw")
        ctk.CTkFrame(self, width=1, fg_color=c("border"), corner_radius=0).grid(
            row=0, column=0, sticky="nse")

        self._content = ctk.CTkFrame(self, fg_color=c("bg"), corner_radius=0)
        self._content.grid(row=0, column=1, sticky="nsew")
        self._content.grid_rowconfigure(0, weight=1)
        self._content.grid_columnconfigure(0, weight=1)

        self.hw = HardwarePanel(self)
        self._hw_line = ctk.CTkFrame(self, width=1, fg_color=c("border"), corner_radius=0)
        self.set_hw_panel(settings.get("show_hw_panel"))
        self.bind("<Configure>", self._layout_hw, add="+")

        self.views = {
            "chat": ChatView(self._content, self),
            "models": CatalogView(self._content, self),
            "compare": CompareView(self._content, self),
            "agent": AgentView(self._content, self),
            "settings": SettingsView(self._content, self),
        }
        for v in self.views.values():
            v.grid(row=0, column=0, sticky="nsew")
        self._toast = None

        self.show("chat")
        self.protocol("WM_DELETE_WINDOW", self.quit_now)
        self.refresh_models()
        self.after(5000, self._poll_ollama)
        self.after(3000, self.check_updates)

    # ── navigation ────────────────────────────────────────────────────────────

    def show(self, key: str):
        self.views[key].tkraise()
        self.sidebar.select(key)
        if hasattr(self.views[key], "on_show"):
            self.views[key].on_show()

    def open_in_chat(self, model_name: str):
        self.views["chat"].set_model(model_name)
        self.show("chat")

    HW_MIN_WIDTH = 1200   # below this window width the system panel hides itself

    def set_hw_panel(self, show: bool):
        self._hw_wanted = show
        self._layout_hw()

    def _layout_hw(self, event=None):
        if event is not None and event.widget is not self:
            return
        width = self.winfo_width() if self.winfo_width() > 1 else 1320
        visible = self._hw_wanted and width / self._get_window_scaling() >= self.HW_MIN_WIDTH
        if visible == self.hw.winfo_ismapped():
            return
        if visible:
            self.hw.grid(row=0, column=2, sticky="nse")
            self._hw_line.grid(row=0, column=2, sticky="nsw")
        else:
            self.hw.grid_forget()
            self._hw_line.grid_forget()

    # ── Ollama / models ───────────────────────────────────────────────────────

    def refresh_models(self):
        def fetch():
            ok = is_ollama_running()
            return ok, (list_local_models() if ok else [])
        bg.run(fetch, on_done=self._apply_models)

    def _apply_models(self, res):
        ok, models = res
        self.sidebar.set_ollama(ok)
        names = [m["name"] for m in models]
        self.views["chat"].set_models(names, ok)
        self.views["compare"].set_models(names, ok)
        self.views["agent"].set_models(names, ok)
        self.views["models"].set_models(models, ok)
        self.views["settings"].set_ollama(ok, len(models))

    def _poll_ollama(self):
        def check():
            return is_ollama_running()

        def got(ok):
            if ok != self.sidebar._ollama_ok:
                self.refresh_models()
            self.after(5000, self._poll_ollama)

        bg.run(check, on_done=got, on_error=lambda e: self.after(5000, self._poll_ollama))

    # ── updates ───────────────────────────────────────────────────────────────

    def check_updates(self, manual=False, done=None):
        def fetch():
            req = urllib.request.Request(RELEASES_API, headers={"User-Agent": "OpenForge/" + VERSION})
            with urllib.request.urlopen(req, timeout=8) as r:
                data = json.loads(r.read())
            return data.get("tag_name", "").lstrip("vV"), data.get("html_url", "")

        def ok(res):
            latest, url = res
            if latest and _newer(latest, VERSION):
                self.sidebar.show_update(latest, url)
            if done:
                done(latest or None)

        bg.run(fetch, on_done=ok, on_error=lambda e: done(None) if done else None)

    # ── toast ─────────────────────────────────────────────────────────────────

    def toast(self, text: str, kind: str = "accent"):
        if self._toast:
            self._toast.destroy()
        t = ctk.CTkFrame(self._content, fg_color=c("card"), corner_radius=RADIUS["card"],
                         border_width=1, border_color=c("border"))
        ico = {"success": "check", "danger": "x"}.get(kind, "dot")
        ctk.CTkLabel(t, text=f"  {text}", font=font(13), text_color=c("text"),
                     image=icon(ico, c(kind), 16), compound="left").pack(padx=16, pady=10)
        t.place(relx=0.5, rely=0.0, anchor="n", y=18)
        self._toast = t
        self.after(4000, lambda: t.destroy() if t.winfo_exists() else None)

    # ── misc ──────────────────────────────────────────────────────────────────

    def _set_icon(self):
        try:
            if sys.platform == "win32":
                ico = str(resource_path("assets/icon.ico"))
                self.iconbitmap(ico)
                # CustomTkinter resets the icon ~200 ms after start; set it again after that
                self.after(300, lambda: self.iconbitmap(ico))
            else:
                from PIL import ImageTk, Image
                self._icon_img = ImageTk.PhotoImage(Image.open(resource_path("assets/icon.png")))
                self.iconphoto(True, self._icon_img)
        except Exception:
            pass

    def quit_now(self):
        self.hw.stop()
        self.destroy()


def _newer(a: str, b: str) -> bool:
    def parts(v):
        return [int(x) if x.isdigit() else 0 for x in v.split(".")]
    return parts(a) > parts(b)


if __name__ == "__main__":
    if sys.platform == "win32":
        try:  # crisp text on high-DPI screens and a proper taskbar icon
            import ctypes
            ctypes.windll.shell32.SetCurrentProcessExplicitAppUserModelID("Dimonchikk.OpenForge")
        except Exception:
            pass
    App().mainloop()
