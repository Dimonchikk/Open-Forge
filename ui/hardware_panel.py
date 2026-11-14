"""Right-hand system monitor: CPU, RAM, GPU, VRAM with a 60-second sparkline."""
from collections import deque
import customtkinter as ctk

from core.hardware import get_snapshot, get_system_info
from ui.theme import c, font, resolve, RADIUS

HISTORY = 60


def _blend(fg: str, bg: str, a: float) -> str:
    f = [int(fg[i:i + 2], 16) for i in (1, 3, 5)]
    b = [int(bg[i:i + 2], 16) for i in (1, 3, 5)]
    return "#" + "".join(f"{round(b[i] + (f[i] - b[i]) * a):02x}" for i in range(3))


class _Meter(ctk.CTkFrame):
    def __init__(self, master, title: str, color_key: str):
        super().__init__(master, fg_color=c("card_alt"), corner_radius=RADIUS["card"])
        self._color_key = color_key
        self._hist = deque([0.0] * HISTORY, maxlen=HISTORY)
        self.grid_columnconfigure(0, weight=1)

        ctk.CTkLabel(self, text=title, font=font(12, "bold"), text_color=c("text_dim"),
                     anchor="w").grid(row=0, column=0, sticky="w", padx=(12, 0), pady=(10, 0))
        self._value = ctk.CTkLabel(self, text="—", font=font(17, "bold"),
                                   text_color=c(color_key), anchor="e")
        self._value.grid(row=0, column=1, sticky="e", padx=(0, 12), pady=(8, 0))
        self._sub = ctk.CTkLabel(self, text="", font=font(11), text_color=c("text_faint"),
                                 anchor="w", height=16)
        self._sub.grid(row=1, column=0, columnspan=2, sticky="w", padx=12)
        self._spark = ctk.CTkCanvas(self, height=34, highlightthickness=0, bd=0)
        self._spark.grid(row=2, column=0, columnspan=2, sticky="ew", padx=10, pady=(4, 10))

    def update_value(self, pct, sub: str):
        if pct is None:
            self._value.configure(text="—")
            self._hist.append(0.0)
        else:
            self._value.configure(text=f"{pct:.0f}%")
            self._hist.append(max(0.0, min(100.0, pct)))
        self._sub.configure(text=sub)
        self._paint(pct is not None)

    def _paint(self, live: bool):
        cv = self._spark
        bg = resolve("card_alt")
        col = resolve(self._color_key)
        cv.configure(bg=bg)
        cv.delete("all")
        w = max(cv.winfo_width(), 10)
        h = int(cv.cget("height"))
        cv.create_line(0, h - 1, w, h - 1, fill=_blend(resolve("text_faint"), bg, 0.25))
        if not live:
            return
        step = w / (HISTORY - 1)
        pts = [(i * step, h - 2 - v / 100 * (h - 4)) for i, v in enumerate(self._hist)]
        area = [(0, h)] + pts + [(w, h)]
        cv.create_polygon(*[x for p in area for x in p], fill=_blend(col, bg, 0.18), outline="")
        cv.create_line(*[x for p in pts for x in p], fill=col, width=2, smooth=True)


class HardwarePanel(ctk.CTkFrame):
    def __init__(self, master, **kw):
        super().__init__(master, fg_color=c("sidebar"), corner_radius=0, width=232, **kw)
        self.grid_propagate(False)
        self.grid_columnconfigure(0, weight=1)
        info = get_system_info()
        self._info = info

        ctk.CTkLabel(self, text="Система", font=font(15, "bold"), text_color=c("text"),
                     anchor="w").grid(row=0, column=0, sticky="w", padx=16, pady=(22, 0))
        gpu = info["gpu_name"] or "Відеокарту не визначено"
        ctk.CTkLabel(self, text=gpu, font=font(11), text_color=c("text_faint"), anchor="w",
                     wraplength=200, justify="left").grid(row=1, column=0, sticky="w", padx=16)

        self._cpu = _Meter(self, "Процесор", "m_cpu")
        self._ram = _Meter(self, "Пам'ять", "m_ram")
        self._gpu = _Meter(self, "Відеокарта", "m_gpu")
        self._vram = _Meter(self, "Відеопам'ять", "m_vram")
        for i, m in enumerate((self._cpu, self._ram, self._gpu, self._vram)):
            m.grid(row=2 + i, column=0, sticky="ew", padx=12, pady=(12 if i == 0 else 8, 0))

        self._running = True
        self.after(300, self._tick)

    def _tick(self):
        if not self._running:
            return
        try:
            s = get_snapshot()
            cores = self._info["cpu_threads"]
            self._cpu.update_value(s["cpu"], f"{cores} потоків")
            self._ram.update_value(s["ram"], f"{s['ram_used_gb']:.1f} з {s['ram_total_gb']:.0f} ГБ")
            if s["gpu_util"] is None:
                self._gpu.update_value(None, "доступно лише для NVIDIA")
            else:
                self._gpu.update_value(s["gpu_util"], "навантаження")
            if s["vram"] is None:
                total = s["vram_total_gb"]
                self._vram.update_value(None, f"усього {total:.0f} ГБ" if total else "немає даних")
            else:
                self._vram.update_value(
                    s["vram"], f"{s['vram_used_gb']:.1f} з {s['vram_total_gb']:.0f} ГБ")
        except Exception:
            pass
        self.after(1000, self._tick)

    def stop(self):
        self._running = False
