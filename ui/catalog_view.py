"""Model catalog tab with category tabs and censored toggle."""
import threading
import customtkinter as ctk
from ui.theme import c, RADIUS
from models_db.catalog import CATALOG, CATEGORIES, get_by_category, get_recommended
from core.ollama_client import pull_model, delete_model, list_local_models
from core.hardware import get_system_info


class ModelCard(ctk.CTkFrame):
    def __init__(self, master, model: dict, local_ids: set, recommended_ids: set, on_select, **kw):
        super().__init__(master, fg_color=c("card"), corner_radius=RADIUS["card"], **kw)
        self.model = model
        self._on_select = on_select
        self._download_stop = threading.Event()

        is_local = model["id"] in local_ids
        is_rec   = model["id"] in recommended_ids

        # ── header row ────────────────────────────────────────────────────────
        header = ctk.CTkFrame(self, fg_color="transparent")
        header.pack(fill="x", padx=14, pady=(12, 0))

        name_lbl = ctk.CTkLabel(
            header,
            text=model["name"],
            font=ctk.CTkFont(family="Inter", size=14, weight="bold"),
            text_color=c("text"),
            anchor="w",
        )
        name_lbl.pack(side="left")

        if is_rec:
            badge = ctk.CTkLabel(
                header,
                text=" ✦ Рекомендовано ",
                font=ctk.CTkFont(family="Inter", size=10),
                fg_color=c("badge_rec"),
                text_color="#FFFFFF",
                corner_radius=RADIUS["badge"],
            )
            badge.pack(side="right", padx=(4, 0))

        # ── meta row ─────────────────────────────────────────────────────────
        meta_txt = f"{model['params_b']}B · {model['size_gb']} GB · VRAM {model['vram_gb']} GB"
        meta = ctk.CTkLabel(
            self,
            text=meta_txt,
            font=ctk.CTkFont(family="Inter", size=11),
            text_color=c("text_dim"),
            anchor="w",
        )
        meta.pack(fill="x", padx=14, pady=(2, 0))

        # ── description ───────────────────────────────────────────────────────
        desc = ctk.CTkLabel(
            self,
            text=model["description"],
            font=ctk.CTkFont(family="Inter", size=12),
            text_color=c("text_dim"),
            anchor="w",
            wraplength=360,
            justify="left",
        )
        desc.pack(fill="x", padx=14, pady=(4, 0))

        # ── progress bar (hidden until download) ─────────────────────────────
        self._prog_frame = ctk.CTkFrame(self, fg_color="transparent")
        self._prog_frame.pack(fill="x", padx=14, pady=(6, 0))
        self._prog_bar = ctk.CTkProgressBar(self._prog_frame, mode="determinate", height=4)
        self._prog_bar.set(0)
        self._prog_label = ctk.CTkLabel(
            self._prog_frame,
            text="",
            font=ctk.CTkFont(family="Inter", size=10),
            text_color=c("text_dim"),
        )
        self._prog_frame.pack_forget()  # hidden by default

        # ── action button ─────────────────────────────────────────────────────
        btn_frame = ctk.CTkFrame(self, fg_color="transparent")
        btn_frame.pack(fill="x", padx=14, pady=(8, 12))

        if is_local:
            self._btn = ctk.CTkButton(
                btn_frame,
                text="Вибрати",
                width=110,
                corner_radius=RADIUS["button"],
                command=lambda: on_select(model["id"]),
            )
            del_btn = ctk.CTkButton(
                btn_frame,
                text="Видалити",
                width=90,
                fg_color=c("danger"),
                hover_color="#CC2F27",
                corner_radius=RADIUS["button"],
                command=self._delete,
            )
            del_btn.pack(side="right")
        else:
            self._btn = ctk.CTkButton(
                btn_frame,
                text="Завантажити",
                width=130,
                corner_radius=RADIUS["button"],
                command=self._download,
            )
        self._btn.pack(side="left")

    # ── download ──────────────────────────────────────────────────────────────

    def _download(self):
        self._btn.configure(text="Скасувати", command=self._cancel_download)
        self._prog_frame.pack(fill="x", padx=14, pady=(6, 0))
        self._prog_bar.pack(fill="x")
        self._prog_label.pack()
        self._download_stop.clear()

        def run():
            def progress(status, pct):
                self.after(0, self._update_progress, status, pct)
            try:
                pull_model(self.model["id"], progress, self._download_stop)
                self.after(0, self._download_done)
            except Exception as e:
                self.after(0, self._download_error, str(e))

        threading.Thread(target=run, daemon=True).start()

    def _cancel_download(self):
        self._download_stop.set()
        self._btn.configure(text="Завантажити", command=self._download)
        self._prog_frame.pack_forget()

    def _update_progress(self, status: str, pct: float):
        self._prog_bar.set(pct / 100)
        self._prog_label.configure(text=f"{status}  {pct:.0f}%")

    def _download_done(self):
        self._prog_frame.pack_forget()
        self._btn.configure(text="Вибрати", command=lambda: self._on_select(self.model["id"]))

    def _download_error(self, msg: str):
        self._prog_frame.pack_forget()
        self._btn.configure(text="Помилка — спробувати ще", command=self._download)

    # ── delete ────────────────────────────────────────────────────────────────

    def _delete(self):
        delete_model(self.model["id"])
        self._btn.configure(text="Завантажити", command=self._download)


class CatalogView(ctk.CTkFrame):
    def __init__(self, master, on_model_select, **kw):
        super().__init__(master, fg_color=c("bg"), **kw)
        self._on_select = on_model_select
        self._censored_only = False

        sys_info = get_system_info()
        self._recommended = get_recommended(sys_info["vram_gb"], sys_info["ram_gb"])
        self._local_ids: set = set()
        self._refresh_local()

        self._build_ui()

    def _refresh_local(self):
        self._local_ids = {m["name"].split(":")[0] + ":" + m["name"].split(":")[-1]
                           if ":" in m["name"] else m["name"]
                           for m in list_local_models()}

    def _build_ui(self):
        # top bar: title + toggle
        top = ctk.CTkFrame(self, fg_color="transparent")
        top.pack(fill="x", padx=20, pady=(16, 8))

        ctk.CTkLabel(
            top,
            text="Каталог моделей",
            font=ctk.CTkFont(family="Inter", size=20, weight="bold"),
            text_color=c("text"),
        ).pack(side="left")

        self._toggle_var = ctk.StringVar(value="normal")
        seg = ctk.CTkSegmentedButton(
            top,
            values=["Звичайні", "Без цензури"],
            variable=self._toggle_var,
            command=self._on_toggle,
            width=240,
            corner_radius=RADIUS["button"],
        )
        seg.pack(side="right")

        # category tabs
        self._tab_view = ctk.CTkTabview(self, corner_radius=RADIUS["card"])
        self._tab_view.pack(fill="both", expand=True, padx=20, pady=(0, 16))

        for key, label in CATEGORIES:
            tab = self._tab_view.add(label)
            self._fill_tab(tab, key)

    def _on_toggle(self, value: str):
        self._censored_only = (value == "Без цензури")
        # rebuild all tabs
        for key, label in CATEGORIES:
            tab = self._tab_view.tab(label)
            for w in tab.winfo_children():
                w.destroy()
            self._fill_tab(tab, key)

    def _fill_tab(self, tab, category: str):
        scroll = ctk.CTkScrollableFrame(tab, fg_color="transparent")
        scroll.pack(fill="both", expand=True)

        models = get_by_category(category, censored=self._censored_only if self._censored_only else None)
        if self._censored_only:
            models = [m for m in models if not m["censored"]]
        else:
            models = [m for m in models if m["censored"]]

        for m in models:
            card = ModelCard(
                scroll,
                model=m,
                local_ids=self._local_ids,
                recommended_ids=self._recommended,
                on_select=self._on_select,
            )
            card.pack(fill="x", padx=8, pady=6)
