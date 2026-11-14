"""Text-input helpers: placeholders, layout-independent shortcuts, right-click menu.

Placeholder — drawn as grey text *inside* the field (cursor stays visible at the
start, hint stays until the first character is typed). A label laid over the
field used to hide the blinking cursor.

Shortcuts — Tk matches Ctrl+C/V/... by the *letter*, so with the Ukrainian
layout (Ctrl+С, Ctrl+М) nothing happened. Here they are matched by the
physical key (keycode), which is the same in every layout.
"""
import sys
import tkinter as tk

from ui.theme import resolve, FONT

CTRL = 0x0004
# physical key codes: Windows virtual-key codes / X11 keycodes
_KEYS = ({65: "a", 67: "c", 86: "v", 88: "x", 90: "z", 89: "y"} if sys.platform == "win32"
         else {38: "a", 54: "c", 55: "v", 53: "x", 52: "z", 29: "y"})
_VIRTUAL = {"c": "<<Copy>>", "v": "<<Paste>>", "x": "<<Cut>>", "z": "<<Undo>>", "y": "<<Redo>>"}
_NAV = {"Left", "Right", "Up", "Down", "Home", "End", "Prior", "Next", "BackSpace", "Delete"}
_MODS = {"Shift_L", "Shift_R", "Control_L", "Control_R", "Alt_L", "Alt_R", "Caps_Lock",
         "Win_L", "Win_R", "Super_L", "Super_R", "Num_Lock", "Escape", "Tab", "ISO_Next_Group"}


def _is_text(w) -> bool:
    return isinstance(w, tk.Text)


def select_all(w):
    if _is_text(w):
        w.tag_add("sel", "1.0", "end-1c")
        w.mark_set("insert", "end-1c")
    else:
        w.select_range(0, "end")
        w.icursor("end")


class Hint:
    """Grey placeholder text inside a tk.Entry or tk.Text."""

    def __init__(self, widget, text: str):
        self.w = widget
        self.text = text
        self.active = False
        if _is_text(widget):
            widget.tag_configure("hint", foreground=resolve("text_faint"))
        for seq, fn in (("<KeyPress>", self._key), ("<KeyRelease>", self._release),
                        ("<<Paste>>", self._before_paste), ("<Button-1>", self._click),
                        ("<FocusIn>", self._click), ("<B1-Motion>", self._block),
                        ("<Double-Button-1>", self._block), ("<Triple-Button-1>", self._block),
                        ("<<Copy>>", self._block), ("<<Cut>>", self._block),
                        ("<<SelectAll>>", self._block)):
            widget.bind(seq, fn, add="+")
        self.show()

    # ── public ────────────────────────────────────────────────────────────────

    def value(self) -> str:
        if self.active:
            return ""
        return self.w.get("1.0", "end-1c") if _is_text(self.w) else self.w.get()

    def set_text(self, text: str):
        self.text = text
        if self.active:
            self.active = False
            self._delete_all()
            self.show()

    def show(self):
        """Show the placeholder if the field is empty."""
        if self.active or self.value():
            return
        self.active = True
        if _is_text(self.w):
            self.w.tag_configure("hint", foreground=resolve("text_faint"))
            self.w.insert("1.0", self.text, "hint")
            self.w.mark_set("insert", "1.0")
            self._reset_undo()
        else:
            self.w.insert(0, self.text)
            self.w.configure(fg=resolve("text_faint"))
            self.w.icursor(0)

    def clear(self):
        """Remove the placeholder (before real text is inserted)."""
        if not self.active:
            return
        self.active = False
        self._delete_all()
        if not _is_text(self.w):
            self.w.configure(fg=resolve("text"))
        else:
            self._reset_undo()

    # ── internals ─────────────────────────────────────────────────────────────

    def _delete_all(self):
        if _is_text(self.w):
            self.w.delete("1.0", "end")
        else:
            self.w.delete(0, "end")

    def _reset_undo(self):
        try:
            self.w.edit_reset()
        except tk.TclError:
            pass

    def _to_start(self):
        if not self.active:
            return
        if _is_text(self.w):
            self.w.mark_set("insert", "1.0")
            self.w.tag_remove("sel", "1.0", "end")
        else:
            self.w.icursor(0)
            self.w.select_clear()

    def _key(self, e):
        if not self.active:
            return None
        if e.keysym in _NAV:
            return "break"              # nothing to move over or delete yet
        if e.keysym in _MODS or (e.state & CTRL):
            return None                 # shortcuts are handled elsewhere
        if e.char and e.char.isprintable():
            self.clear()                # the class binding then inserts the char
        return None

    def _release(self, _e=None):
        if not self.active and not self.value():
            self.show()

    def _before_paste(self, _e=None):
        self.clear()

    def _click(self, _e=None):
        if self.active:
            self.w.after_idle(self._to_start)

    def _block(self, _e=None):
        return "break" if self.active else None


# ── app-wide shortcuts and context menu ───────────────────────────────────────

def _on_ctrl_key(e):
    key = _KEYS.get(e.keycode)
    if not key:
        return None
    w = e.widget
    if key == "a":
        select_all(w)
        return "break"
    if key in ("z", "y") and not _is_text(w):
        return "break"                  # tk.Entry has no undo
    w.event_generate(_VIRTUAL[key])
    return "break"


def _context_menu(e):
    w = e.widget
    readonly = str(w.cget("state")) in ("disabled", "readonly")
    try:
        w.focus_set()
    except tk.TclError:
        pass
    m = tk.Menu(w, tearoff=0, font=(FONT, 10))
    has_undo = _is_text(w) and str(w.cget("undo")) in ("1", "true", "True")
    if has_undo and not readonly:
        m.add_command(label="Скасувати", accelerator="Ctrl+Z",
                      command=lambda: w.event_generate("<<Undo>>"))
        m.add_command(label="Повторити", accelerator="Ctrl+Y",
                      command=lambda: w.event_generate("<<Redo>>"))
        m.add_separator()
    if not readonly:
        m.add_command(label="Вирізати", accelerator="Ctrl+X",
                      command=lambda: w.event_generate("<<Cut>>"))
    m.add_command(label="Копіювати", accelerator="Ctrl+C",
                  command=lambda: w.event_generate("<<Copy>>"))
    if not readonly:
        m.add_command(label="Вставити", accelerator="Ctrl+V",
                      command=lambda: w.event_generate("<<Paste>>"))
    m.add_separator()
    m.add_command(label="Виділити все", accelerator="Ctrl+A", command=lambda: select_all(w))
    try:
        m.tk_popup(e.x_root, e.y_root)
    finally:
        m.grab_release()
    return "break"


def install(root):
    """Enable Ctrl+A/C/V/X/Z/Y on any keyboard layout and a right-click menu."""
    def _select_all(e):
        select_all(e.widget)
        return "break"

    for cls in ("Entry", "Text"):
        root.bind_class(cls, "<Control-KeyPress>", _on_ctrl_key)
        # Tk's own Ctrl+A means "go to line start"; make it "select all" like everywhere else
        root.bind_class(cls, "<Control-a>", _select_all)
        root.bind_class(cls, "<Control-A>", _select_all)
        root.bind_class(cls, "<Button-3>", _context_menu)
        if sys.platform == "darwin":
            root.bind_class(cls, "<Button-2>", _context_menu)
