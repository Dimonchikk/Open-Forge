"""In-app modal dialogs (replace tkinter.messagebox, which looks out of place)."""
import customtkinter as ctk
from ui.theme import c, font, RADIUS, primary_button, ghost_button, danger_button


class Dialog(ctk.CTkToplevel):
    def __init__(self, parent, title: str, width: int = 440):
        super().__init__(parent)
        self.withdraw()
        self.title(title)
        self.configure(fg_color=c("card"))
        self.resizable(False, False)
        self.transient(parent.winfo_toplevel())
        self._width = width
        self.body = ctk.CTkFrame(self, fg_color="transparent")
        self.body.pack(fill="both", expand=True, padx=24, pady=(22, 10))
        self.buttons = ctk.CTkFrame(self, fg_color="transparent")
        self.buttons.pack(fill="x", padx=24, pady=(6, 20))
        self.protocol("WM_DELETE_WINDOW", self.close)
        self.bind("<Escape>", lambda e: self.close())

    def heading(self, text: str, sub: str = ""):
        ctk.CTkLabel(self.body, text=text, font=font(17, "bold"), text_color=c("text"),
                     anchor="w", justify="left", wraplength=self._width - 48).pack(anchor="w")
        if sub:
            ctk.CTkLabel(self.body, text=sub, font=font(13), text_color=c("text_dim"),
                         anchor="w", justify="left", wraplength=self._width - 48
                         ).pack(anchor="w", pady=(6, 0))

    def show(self):
        top = self.master.winfo_toplevel()
        self.update_idletasks()
        scale = self._get_window_scaling()
        h = round(self.winfo_reqheight() / scale)          # geometry() takes logical units
        x = top.winfo_rootx() + (top.winfo_width() - round(self._width * scale)) // 2
        y = top.winfo_rooty() + (top.winfo_height() - round(h * scale)) // 3
        self.geometry(f"{self._width}x{h}+{max(x, 0)}+{max(y, 0)}")
        self.deiconify()
        self.lift()
        self.after(50, self._grab)

    def _grab(self):
        try:
            self.grab_set()
            self.focus_force()
        except Exception:
            pass

    def close(self):
        try:
            self.grab_release()
        except Exception:
            pass
        self.destroy()


def confirm(parent, title: str, text: str, ok_text: str, on_result, danger: bool = False,
            cancel_text: str = "Скасувати"):
    """Non-blocking confirm. on_result(True|False) is called exactly once."""
    d = Dialog(parent, title)
    d.heading(title, text)
    done = {"v": False}

    def finish(value: bool):
        if done["v"]:
            return
        done["v"] = True
        d.close()
        on_result(value)

    d.protocol("WM_DELETE_WINDOW", lambda: finish(False))
    d.bind("<Escape>", lambda e: finish(False))
    make = danger_button if danger else primary_button
    make(d.buttons, ok_text, width=150, command=lambda: finish(True)).pack(side="right")
    ghost_button(d.buttons, cancel_text, width=120,
                 command=lambda: finish(False)).pack(side="right", padx=(0, 8))
    d.show()
    return d
