"""Apple-inspired color tokens for CustomTkinter."""
import customtkinter as ctk

# ── color palette ──────────────────────────────────────────────────────────────
COLORS = {
    # backgrounds
    "bg":           ("#F5F5F7", "#1C1C1E"),   # main window
    "sidebar":      ("#FFFFFF", "#2C2C2E"),
    "panel":        ("#FFFFFF", "#2C2C2E"),
    "card":         ("#F2F2F7", "#3A3A3C"),
    "card_hover":   ("#E8E8ED", "#48484A"),
    "input_bg":     ("#FFFFFF", "#2C2C2E"),

    # text
    "text":         ("#1D1D1F", "#F5F5F7"),
    "text_dim":     ("#6E6E73", "#8E8E93"),
    "text_accent":  ("#0071E3", "#0A84FF"),   # Apple Blue

    # accent
    "accent":       "#0071E3",
    "accent_dark":  "#0A84FF",
    "accent_hover": "#0077ED",
    "danger":       "#FF3B30",
    "success":      "#34C759",
    "warning":      "#FF9500",

    # borders
    "border":       ("#D2D2D7", "#3A3A3C"),
    "divider":      ("#E5E5EA", "#38383A"),

    # badge
    "badge_rec":    ("#34C759", "#30D158"),   # recommended
    "badge_slow":   ("#FF9500", "#FF9F0A"),   # may be slow
}

FONT_FAMILY = "SF Pro Display" if ctk.get_appearance_mode() else "Inter"

RADIUS = {
    "card": 12,
    "button": 8,
    "input": 10,
    "badge": 6,
}


def apply_theme():
    ctk.set_appearance_mode("system")
    ctk.set_default_color_theme("blue")


def c(key: str) -> tuple | str:
    """Return color tuple (light, dark) or plain string."""
    return COLORS[key]
