"""Design tokens for Open Forge: colours, fonts, radii.

Every colour is a (light, dark) pair so CustomTkinter switches it with the
appearance mode. Plain-Tk widgets (Canvas, tk.Text) can't do that, so they
call `resolve()` to get the value for the current mode.
"""
import sys
import customtkinter as ctk

COLORS = {
    # surfaces
    "bg":          ("#F6F6F7", "#111113"),   # window background
    "sidebar":     ("#FFFFFF", "#18181B"),
    "card":        ("#FFFFFF", "#1C1C20"),
    "card_alt":    ("#F1F1F3", "#232328"),   # inputs, chips, AI-free areas
    "hover":       ("#ECECEF", "#2A2A30"),
    "border":      ("#E4E4E7", "#2C2C33"),

    # text
    "text":        ("#18181B", "#F4F4F5"),
    "text_dim":    ("#52525B", "#A1A1AA"),
    "text_faint":  ("#8E8E96", "#6B6B74"),

    # accent (Apple blue)
    "accent":      ("#007AFF", "#0A84FF"),
    "accent_hover":("#0062CC", "#3B9BFF"),
    "accent_soft": ("#E6F0FF", "#16243A"),   # selected nav item, soft badges
    "on_accent":   ("#FFFFFF", "#FFFFFF"),

    # status
    "success":     ("#16A34A", "#30D158"),
    "success_soft":("#E7F7EC", "#13291B"),
    "warning":     ("#D97706", "#FF9F0A"),
    "warning_soft":("#FEF3E2", "#2E2210"),
    "danger":      ("#DC2626", "#FF453A"),
    "danger_hover":("#B91C1C", "#FF6961"),
    "danger_soft": ("#FDECEC", "#2E1414"),

    # hardware meters
    "m_cpu":       ("#007AFF", "#0A84FF"),
    "m_ram":       ("#16A34A", "#30D158"),
    "m_gpu":       ("#EA7A00", "#FF9F0A"),
    "m_vram":      ("#E0245E", "#FF375F"),
}

RADIUS = {"card": 14, "button": 10, "input": 12, "badge": 8, "pill": 999}

if sys.platform == "win32":
    FONT = "Segoe UI"
    MONO = "Consolas"
elif sys.platform == "darwin":
    FONT = "SF Pro Text"
    MONO = "Menlo"
else:
    FONT = "DejaVu Sans"
    MONO = "DejaVu Sans Mono"


def c(key: str):
    """Colour pair for CTk widgets."""
    return COLORS[key]


def is_dark() -> bool:
    return ctk.get_appearance_mode() == "Dark"


def resolve(key_or_pair) -> str:
    """Single colour string for the current mode (for plain Tk widgets)."""
    pair = COLORS[key_or_pair] if isinstance(key_or_pair, str) else key_or_pair
    if isinstance(pair, str):
        return pair
    return pair[1] if is_dark() else pair[0]


def logical(widget, px: float) -> float:
    """Physical pixels (event.width, winfo_width) → CustomTkinter's logical units.

    CTk multiplies wraplength / padx / sizes by the display scaling (125 %, 150 % …),
    so anything derived from a measured width must be divided by it first.
    """
    try:
        return px / widget._get_widget_scaling()
    except Exception:
        return px


def font(size: int = 13, weight: str = "normal", mono: bool = False) -> ctk.CTkFont:
    return ctk.CTkFont(family=MONO if mono else FONT, size=size, weight=weight)


def apply_theme(mode: str = "system"):
    ctk.set_appearance_mode({"light": "light", "dark": "dark"}.get(mode, "system"))
    ctk.set_default_color_theme("blue")


# ── shared widget presets ─────────────────────────────────────────────────────

def primary_button(master, text="", **kw) -> ctk.CTkButton:
    opts = dict(
        text=text, height=38, corner_radius=RADIUS["button"],
        fg_color=c("accent"), hover_color=c("accent_hover"),
        text_color=c("on_accent"), font=font(13, "bold"),
    )
    opts.update(kw)
    return ctk.CTkButton(master, **opts)


def ghost_button(master, text="", **kw) -> ctk.CTkButton:
    opts = dict(
        text=text, height=38, corner_radius=RADIUS["button"],
        fg_color="transparent", hover_color=c("hover"),
        text_color=c("text"), border_width=1, border_color=c("border"),
        font=font(13),
    )
    opts.update(kw)
    return ctk.CTkButton(master, **opts)


def danger_button(master, text="", **kw) -> ctk.CTkButton:
    opts = dict(
        text=text, height=38, corner_radius=RADIUS["button"],
        fg_color=c("danger"), hover_color=c("danger_hover"),
        text_color="#FFFFFF", font=font(13, "bold"),
    )
    opts.update(kw)
    return ctk.CTkButton(master, **opts)


def badge(master, text: str, kind: str = "accent") -> ctk.CTkLabel:
    """Small soft-coloured pill: kind in accent/success/warning/danger."""
    return ctk.CTkLabel(
        master, text=f"  {text}  ", height=22, corner_radius=RADIUS["badge"],
        fg_color=c(f"{kind}_soft"), text_color=c(kind), font=font(11, "bold"),
    )


def option_menu(master, values, **kw) -> ctk.CTkOptionMenu:
    opts = dict(
        values=values or ["—"], height=36, corner_radius=RADIUS["button"],
        fg_color=c("card_alt"), button_color=c("card_alt"),
        button_hover_color=c("hover"), text_color=c("text"),
        dropdown_fg_color=c("card"), dropdown_hover_color=c("hover"),
        dropdown_text_color=c("text"), font=font(13), dropdown_font=font(13),
        dynamic_resizing=False,
    )
    opts.update(kw)
    return ctk.CTkOptionMenu(master, **opts)


def segmented(master, values, **kw) -> ctk.CTkSegmentedButton:
    opts = dict(
        values=values, height=34, corner_radius=RADIUS["button"],
        fg_color=c("card_alt"), unselected_color=c("card_alt"),
        unselected_hover_color=c("hover"), selected_color=c("card"),
        selected_hover_color=c("card"), text_color=c("text"), font=font(12, "bold"),
    )
    opts.update(kw)
    return ctk.CTkSegmentedButton(master, **opts)


def page_header(master, title: str, subtitle: str = "") -> ctk.CTkFrame:
    head = ctk.CTkFrame(master, fg_color="transparent")
    ctk.CTkLabel(head, text=title, font=font(22, "bold"), text_color=c("text"),
                 anchor="w").pack(anchor="w")
    if subtitle:
        ctk.CTkLabel(head, text=subtitle, font=font(13), text_color=c("text_dim"),
                     anchor="w", justify="left").pack(anchor="w", pady=(2, 0))
    return head


class IconButton(ctk.CTkFrame):
    """Exactly round (or rounded-square) icon button.

    CTkButton reserves `corner_radius` px on both sides, so a 36 px button with
    radius 18 grew into a ~54 px oval. This draws a fixed-size frame instead.
    """

    def __init__(self, master, image, command=None, size: int = 36, radius: int | None = None,
                 fg_color="transparent", hover_color=None, border_color=None):
        super().__init__(master, width=size, height=size,
                         corner_radius=size // 2 if radius is None else radius,
                         fg_color=fg_color, border_width=1 if border_color else 0,
                         border_color=border_color or c("border"))
        self.grid_propagate(False)
        self.pack_propagate(False)
        self._command = command
        self._fg = fg_color
        self._hover = hover_color or c("hover")
        self._label = ctk.CTkLabel(self, text="", image=image, width=size - 8, height=size - 8,
                                   fg_color="transparent", cursor="hand2")
        self._label.place(relx=0.5, rely=0.5, anchor="center")
        self.configure(cursor="hand2")
        for w in (self, self._label):
            w.bind("<Button-1>", self._click)
            w.bind("<Enter>", lambda e: super(IconButton, self).configure(fg_color=self._hover))
            w.bind("<Leave>", lambda e: super(IconButton, self).configure(fg_color=self._fg))

    def _click(self, _e=None):
        if self._command:
            self._command()

    def set_style(self, image=None, fg_color=None, hover_color=None, border_color=None):
        if fg_color is not None:
            self._fg = fg_color
            super().configure(fg_color=fg_color)
        if hover_color is not None:
            self._hover = hover_color
        if border_color is not None:
            super().configure(border_color=border_color)
        if image is not None:
            self._label.configure(image=image)

    def set_command(self, command):
        self._command = command
