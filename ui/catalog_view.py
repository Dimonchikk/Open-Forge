"""Model catalog: every model (normal + uncensored), install / delete, fit to this PC."""
import threading
import customtkinter as ctk

from core.hardware import get_system_info
from core.ollama_client import pull_model, delete_model, normalize
from models_db.catalog import CATALOG, CATEGORIES, fit, FIT_FAST, FIT_SLOW, FIT_NO
from ui import bg
from ui.dialogs import confirm
from ui.icons import icon
from ui.theme import (c, font, RADIUS, primary_button, ghost_button, badge, segmented,
                      page_header)

FIT_BADGE = {
    FIT_FAST: ("Рекомендовано", "success"),
    FIT_SLOW: ("Піде повільніше", "warning"),
    FIT_NO: ("Не вистачить пам'яті", "danger"),
}
FIT_RANK = {FIT_FAST: 0, FIT_SLOW: 1, FIT_NO: 2}
CENSOR = ["Усі", "Звичайні", "Без цензури"]


def _gb(n_bytes: int) -> str:
    return f"{n_bytes / 1024 ** 3:.1f}"


class CatalogView(ctk.CTkFrame):
    def __init__(self, master, app, **kw):
        super().__init__(master, fg_color=c("bg"), corner_radius=0, **kw)
        self.app = app
        info = get_system_info()
        self._vram, self._ram = info["vram_gb"], info["ram_gb"]
        self._gpu = info["gpu_name"]
        self._installed: dict[str, dict] = {}      # normalized name -> ollama model dict
        self._downloads: dict[str, dict] = {}      # model id -> {stop, done, total, status}
        self._cards: dict[str, ctk.CTkFrame] = {}  # model id -> card (built lazily, reused)
        self._category = "all"
        self._censor = "Усі"
        self._query = ""
        self._build()

    # ── layout ────────────────────────────────────────────────────────────────

    def _build(self):
        self.grid_columnconfigure(0, weight=1)
        self.grid_rowconfigure(3, weight=1)

        top = ctk.CTkFrame(self, fg_color="transparent")
        top.grid(row=0, column=0, sticky="ew", padx=28, pady=(22, 0))
        top.grid_columnconfigure(0, weight=1)
        if self._vram:
            pc = f"{self._gpu or 'відеокарта'} · {self._vram} ГБ відеопам'яті · {self._ram} ГБ RAM"
        else:
            pc = f"Відеокарту не визначено · {self._ram} ГБ RAM"
        page_header(top, "Моделі", f"Твій ПК: {pc}").grid(row=0, column=0, sticky="w")

        search = ctk.CTkFrame(top, fg_color=c("card_alt"), corner_radius=RADIUS["button"])
        search.grid(row=0, column=1, sticky="e")
        ctk.CTkLabel(search, text="", image=icon("search", c("text_faint"), 16)
                     ).pack(side="left", padx=(10, 0))
        self._search = ctk.CTkEntry(search, width=220, height=36, border_width=0,
                                    fg_color="transparent", font=font(13),
                                    placeholder_text="Пошук моделі", text_color=c("text"))
        self._search.pack(side="left", padx=(4, 8))
        self._search.bind("<KeyRelease>", lambda e: self._on_search())

        filt = ctk.CTkFrame(self, fg_color="transparent")
        filt.grid(row=1, column=0, sticky="ew", padx=28, pady=(16, 0))
        filt.grid_columnconfigure(1, weight=1)
        self._cat_seg = segmented(filt, [label for _, label in CATEGORIES],
                                  command=self._on_category)
        self._cat_seg.set("Усі")
        self._cat_seg.grid(row=0, column=0, columnspan=3, sticky="w")
        self._cen_seg = segmented(filt, CENSOR, command=self._on_censor)
        self._cen_seg.set("Усі")
        self._cen_seg.grid(row=1, column=0, sticky="w", pady=(10, 0))
        self._count = ctk.CTkLabel(filt, text="", font=font(12, "bold"),
                                   text_color=c("text_dim"))
        self._count.grid(row=1, column=2, sticky="e", pady=(10, 0))

        self._legend = ctk.CTkLabel(
            self, font=font(12), text_color=c("text_faint"), anchor="w", justify="left",
            text="Рекомендовано — модель повністю влазить у відеопам'ять і працює швидко.  "
                 "Піде повільніше — частина буде в RAM.  Не вистачить пам'яті — можна "
                 "встановити, але може не запуститися.")
        self._legend.grid(row=2, column=0, sticky="ew", padx=30, pady=(10, 2))
        # wrap to the page width (binding to the label itself would feed back into its size)
        self.bind("<Configure>",
                  lambda e: self._legend.configure(wraplength=max(e.width - 70, 300)), add="+")

        self._scroll = ctk.CTkScrollableFrame(self, fg_color="transparent",
                                              scrollbar_button_color=c("border"))
        self._scroll.grid(row=3, column=0, sticky="nsew", padx=16, pady=(4, 12))
        self._scroll.grid_columnconfigure((0, 1), weight=1, uniform="col")
        self._empty = ctk.CTkLabel(self._scroll, text="", font=font(14), text_color=c("text_dim"))

    # ── data from App ─────────────────────────────────────────────────────────

    def set_models(self, local: list[dict], ollama_ok: bool):
        self._ollama_ok = ollama_ok
        self._installed = {normalize(m["name"]): m for m in local}
        for mid in list(self._cards):        # installed state may have changed
            if mid not in self._downloads:
                self._cards.pop(mid).destroy()
        self._render()

    def on_show(self):
        self._render()

    # ── filtering ─────────────────────────────────────────────────────────────

    def _on_category(self, label):
        self._category = next(k for k, v in CATEGORIES if v == label)
        self._render()

    def _on_censor(self, value):
        self._censor = value
        self._render()

    def _on_search(self):
        self._query = self._search.get().strip().lower()
        self._render()

    def _visible(self) -> list[dict]:
        if self._category == "installed":
            items = [m for m in CATALOG if normalize(m["id"]) in self._installed]
            known = {normalize(m["id"]) for m in CATALOG}
            for name, m in self._installed.items():
                if name not in known:           # models installed outside the catalog
                    items.append({"id": m["name"], "name": m["name"], "category": "custom",
                                  "size_gb": round(m.get("size", 0) / 1024 ** 3, 1),
                                  "description": "Встановлена поза каталогом (напр. своя GGUF).",
                                  "uncensored": False, "moe": False, "tags": []})
        elif self._category == "all":
            items = list(CATALOG)
        else:
            items = [m for m in CATALOG if m["category"] == self._category]

        if self._censor == "Звичайні":
            items = [m for m in items if not m["uncensored"]]
        elif self._censor == "Без цензури":
            items = [m for m in items if m["uncensored"]]
        if self._query:
            q = self._query
            items = [m for m in items if q in m["name"].lower() or q in m["id"].lower()
                     or q in m["description"].lower()]
        items.sort(key=lambda m: (FIT_RANK[fit(m, self._vram, self._ram)], -m["size_gb"]))
        return items

    def _render(self):
        items = self._visible()
        for card in self._cards.values():
            card.grid_forget()
        self._empty.grid_forget()
        n_rec = sum(fit(m, self._vram, self._ram) == FIT_FAST for m in items)
        self._count.configure(text=f"{len(items)} моделей · {n_rec} рекомендовано для твого ПК")
        if not items:
            self._empty.configure(text="Нічого не знайдено" if self._query or
                                  self._category != "installed" else
                                  "Ще нічого не встановлено — обери модель у вкладці «Усі».")
            self._empty.grid(row=0, column=0, columnspan=2, pady=40)
            return
        self._place(items, 0)

    def _place(self, items, start):
        # build cards in small batches so the window stays responsive
        for i in range(start, min(start + 8, len(items))):
            m = items[i]
            card = self._cards.get(m["id"]) or self._make_card(m)
            self._cards[m["id"]] = card
            card.grid(row=i // 2, column=i % 2, sticky="nsew", padx=8, pady=8)
        if start + 8 < len(items):
            self.after(1, self._place, items, start + 8)

    # ── card ──────────────────────────────────────────────────────────────────

    def _make_card(self, m: dict) -> ctk.CTkFrame:
        installed = normalize(m["id"]) in self._installed
        f = fit(m, self._vram, self._ram)
        card = ctk.CTkFrame(self._scroll, fg_color=c("card"), corner_radius=RADIUS["card"],
                            border_width=1, border_color=c("border"))
        card.grid_columnconfigure(0, weight=1)

        head = ctk.CTkFrame(card, fg_color="transparent")
        head.grid(row=0, column=0, sticky="ew", padx=16, pady=(14, 0))
        ctk.CTkLabel(head, text=m["name"], font=font(15, "bold"), text_color=c("text"),
                     anchor="w").pack(side="left")

        tags = ctk.CTkFrame(card, fg_color="transparent")
        tags.grid(row=1, column=0, sticky="w", padx=16, pady=(8, 0))
        if m["category"] != "custom":
            t, kind = FIT_BADGE[f]
            badge(tags, t, kind).pack(side="left", padx=(0, 6))
        if installed:
            badge(tags, "Встановлено", "accent").pack(side="left", padx=(0, 6))
        if m["uncensored"]:
            ctk.CTkLabel(tags, text="  Без цензури  ", height=22, corner_radius=RADIUS["badge"],
                         fg_color=("#F3E8FF", "#2A1B3D"), text_color=("#7C3AED", "#C4A5FF"),
                         font=font(11, "bold")).pack(side="left", padx=(0, 6))

        meta = [f"{m['size_gb']} ГБ"]
        if m.get("moe"):
            meta.append("MoE")
        meta += m.get("tags", [])
        ctk.CTkLabel(card, text=" · ".join(meta), font=font(12), text_color=c("text_faint"),
                     anchor="w").grid(row=2, column=0, sticky="w", padx=16, pady=(8, 0))
        desc = ctk.CTkLabel(card, text=m["description"], font=font(13), text_color=c("text_dim"),
                            anchor="w", justify="left", wraplength=360)
        desc.grid(row=3, column=0, sticky="ew", padx=16, pady=(4, 0))
        card.bind("<Configure>", lambda e, d=desc: d.configure(wraplength=max(e.width - 36, 200)))
        ctk.CTkLabel(card, text=m["id"], font=font(11, mono=True), text_color=c("text_faint"),
                     anchor="w").grid(row=4, column=0, sticky="w", padx=16, pady=(6, 0))

        actions = ctk.CTkFrame(card, fg_color="transparent")
        actions.grid(row=5, column=0, sticky="ew", padx=16, pady=(12, 14))
        card._actions = actions          # noqa: SLF001 - simple widget bag
        card._model = m
        self._fill_actions(card)
        return card

    def _fill_actions(self, card):
        m, box = card._model, card._actions
        for w in box.winfo_children():
            w.destroy()
        mid = m["id"]

        if mid in self._downloads:
            dl = self._downloads[mid]
            box.grid_columnconfigure(0, weight=1)
            bar = ctk.CTkProgressBar(box, height=6, corner_radius=3,
                                     progress_color=c("accent"), fg_color=c("card_alt"))
            bar.grid(row=0, column=0, sticky="ew", pady=(6, 0))
            bar.set(dl.get("frac", 0))
            lbl = ctk.CTkLabel(box, text=dl.get("text", "Підготовка…"), font=font(11),
                               text_color=c("text_dim"), anchor="w")
            lbl.grid(row=1, column=0, sticky="w")
            ghost_button(box, "Скасувати", width=100, height=32,
                         command=lambda: dl["stop"].set()).grid(row=0, column=1, rowspan=2,
                                                                 padx=(10, 0))
            dl["bar"], dl["label"] = bar, lbl
            return

        if normalize(mid) in self._installed:
            primary_button(box, "Відкрити в чаті", width=160, height=34,
                           image=icon("chat", "#FFFFFF", 15), compound="left",
                           command=lambda: self._open(mid)).pack(side="left")
            ctk.CTkButton(box, text="", width=34, height=34, corner_radius=RADIUS["button"],
                          image=icon("trash", c("danger"), 16), fg_color="transparent",
                          hover_color=c("danger_soft"), border_width=1,
                          border_color=c("border"),
                          command=lambda: self._delete(m)).pack(side="left", padx=(8, 0))
        else:
            rec = fit(m, self._vram, self._ram) == FIT_FAST
            make = primary_button if rec else ghost_button
            make(box, f"Встановити · {m['size_gb']} ГБ", width=180, height=34,
                 image=icon("download", "#FFFFFF" if rec else c("text_dim"), 15),
                 compound="left", command=lambda: self._download(m)).pack(side="left")

    # ── actions ───────────────────────────────────────────────────────────────

    def _open(self, mid: str):
        name = self._installed.get(normalize(mid), {}).get("name", mid)
        self.app.open_in_chat(name)

    def _download(self, m: dict):
        if not getattr(self, "_ollama_ok", True):
            self.app.show("setup")
            return
        mid = m["id"]
        dl = {"stop": threading.Event(), "frac": 0.0, "text": "Підготовка…"}
        self._downloads[mid] = dl
        card = self._cards.get(mid)
        if card:
            self._fill_actions(card)

        def progress(status, done, total):
            if total:
                frac = done / total
                text = f"{_gb(done)} з {_gb(total)} ГБ · {frac * 100:.0f}%"
            else:
                frac, text = dl["frac"], {"pulling manifest": "Отримую опис…",
                                          "verifying sha256 digest": "Перевіряю файли…",
                                          "writing manifest": "Завершую…",
                                          "success": "Готово"}.get(status, status)
            bg.ui(self._on_progress, mid, frac, text)

        def work():
            return pull_model(mid, progress, dl["stop"])

        bg.run(work, on_done=lambda ok: self._dl_finished(mid, ok, None),
               on_error=lambda e: self._dl_finished(mid, False, str(e)))

    def _on_progress(self, mid, frac, text):
        dl = self._downloads.get(mid)
        if not dl:
            return
        dl["frac"], dl["text"] = frac, text
        try:
            dl["bar"].set(frac)
            dl["label"].configure(text=text)
        except Exception:
            pass   # card is not on screen right now

    def _dl_finished(self, mid, ok, err):
        self._downloads.pop(mid, None)
        card = self._cards.pop(mid, None)
        if card:
            card.destroy()
        if err:
            self.app.toast(f"Не вдалося встановити {mid}: {err}", kind="danger")
        elif ok:
            self.app.toast(f"Модель {mid} встановлено", kind="success")
        self.app.refresh_models()

    def _delete(self, m: dict):
        name = self._installed.get(normalize(m["id"]), {}).get("name", m["id"])

        def go(yes):
            if not yes:
                return
            bg.run(delete_model, name, on_done=lambda ok: (
                self.app.toast(f"{m['name']} видалено" if ok else "Не вдалося видалити",
                               kind="success" if ok else "danger"),
                self.app.refresh_models()))

        confirm(self, f"Видалити {m['name']}?",
                f"Звільнить близько {m['size_gb']} ГБ на диску. Модель можна буде встановити "
                "знову будь-коли.", "Видалити", go, danger=True)
