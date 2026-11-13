"""Right-side hardware monitor: CPU, RAM, GPU util, GPU mem."""
import threading
import time
import customtkinter as ctk
from ui.theme import c, RADIUS
from core.hardware import get_snapshot


class RingMeter(ctk.CTkCanvas):
    """Thin arc ring showing a percentage."""

    SIZE = 68
    THICKNESS = 7

    def __init__(self, master, label: str, color: str, **kw):
        super().__init__(
            master,
            width=self.SIZE,
            height=self.SIZE + 20,
            bg=master.cget("fg_color")[1] if isinstance(master.cget("fg_color"), (list, tuple)) else master.cget("fg_color"),
            highlightthickness=0,
            **kw,
        )
        self._label = label
        self._color = color
        self._pct = 0.0
        self._draw()

    def set_value(self, pct: float):
        self._pct = max(0.0, min(100.0, pct))
        self._draw()

    def _draw(self):
        self.delete("all")
        s = self.SIZE
        t = self.THICKNESS
        pad = 4

        # background ring
        self.create_arc(
            pad, pad, s - pad, s - pad,
            start=90, extent=360,
            outline="#E5E5EA", width=t, style="arc",
        )
        # value ring
        extent = -self._pct / 100 * 359.9
        self.create_arc(
            pad, pad, s - pad, s - pad,
            start=90, extent=extent,
            outline=self._color, width=t, style="arc",
        )
        # percentage text
        self.create_text(
            s // 2, s // 2,
            text=f"{int(self._pct)}%",
            font=("Inter", 11, "bold"),
            fill=self._color,
        )
        # label below
        self.create_text(
            s // 2, s + 10,
            text=self._label,
            font=("Inter", 10),
            fill="#8E8E93",
        )


class HardwarePanel(ctk.CTkFrame):
    def __init__(self, master, **kw):
        super().__init__(master, fg_color=c("sidebar"), corner_radius=0, width=140, **kw)
        self.pack_propagate(False)

        title = ctk.CTkLabel(
            self,
            text="Система",
            font=ctk.CTkFont(family="Inter", size=13, weight="bold"),
            text_color=c("text"),
        )
        title.pack(pady=(16, 10))

        self._cpu_ring  = RingMeter(self, "CPU",     "#0A84FF")
        self._ram_ring  = RingMeter(self, "RAM",     "#34C759")
        self._gpu_ring  = RingMeter(self, "GPU",     "#FF9F0A")
        self._gmem_ring = RingMeter(self, "VRAM",    "#FF375F")

        for r in (self._cpu_ring, self._ram_ring, self._gpu_ring, self._gmem_ring):
            r.pack(pady=8)

        self._running = True
        self._thread = threading.Thread(target=self._poll, daemon=True)
        self._thread.start()

    def _poll(self):
        while self._running:
            snap = get_snapshot()
            self.after(0, self._update, snap)
            time.sleep(1)

    def _update(self, snap: dict):
        self._cpu_ring.set_value(snap["cpu"])
        self._ram_ring.set_value(snap["ram"])
        self._gpu_ring.set_value(snap["gpu_util"])
        self._gmem_ring.set_value(snap["gpu_mem"])

    def stop(self):
        self._running = False
