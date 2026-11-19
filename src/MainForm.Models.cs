// Open Forge — розділ «Моделі»: каталог з фільтрами (призначення, цензура, пошук), позначками,
// чи піде модель на цьому ПК, і завантаженням через Ollama.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace OpenForge
{
    partial class MainForm
    {
        static readonly string[] Purposes = { "Усі", "Чат", "Код", "Зображення", "Міркування", "Переклад", "Швидкі", "Встановлені" };
        static readonly string[] Censors = { "Будь-які", "З цензурою", "Без цензури" };
        static readonly string[] Fits = { "Усі", "Запуститься", "Піде нормально", "Тільки швидкі" };
        static readonly string[] Sorts = { "Рекомендовані", "Від сильної до слабкої", "Від слабкої до сильної", "Від легкої до важкої", "Від важкої до легкої", "За назвою" };
        PillBtn sortBtn;

        ScrollHost modelsHost;
        Segmented purposeSeg, censorSeg, fitSeg;
        InputBox modelSearch;
        Label modelsInfo;
        ModelList modelList;
        readonly Dictionary<string, PullJob> pulls = new Dictionary<string, PullJob>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, string> pullErrors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        readonly System.Windows.Forms.Timer pullTimer = new System.Windows.Forms.Timer { Interval = 300 };

        Control BuildModels()
        {
            // Фільтри й пошук — нерухома шапка; прокручується лише список моделей під нею.
            var box = new Page();
            modelsHost = new ScrollHost();
            var page = modelsHost.Content;

            purposeSeg = new Segmented { Items = Purposes, FitText = true, OnBg = true };
            purposeSeg.Selected = Math.Max(0, Math.Min(Purposes.Length - 1, Settings.GetInt("ModelsPurpose", 0)));
            purposeSeg.ItemClicked += i => { purposeSeg.Selected = i; Settings.SetInt("ModelsPurpose", i); RefreshModelsPage(); modelsHost.ScrollTo(0); };
            box.Controls.Add(purposeSeg);

            censorSeg = new Segmented { Items = Censors, OnBg = true };
            censorSeg.Selected = Math.Max(0, Math.Min(2, Settings.GetInt("ModelsCensor", 0)));
            censorSeg.ItemClicked += i => { censorSeg.Selected = i; Settings.SetInt("ModelsCensor", i); RefreshModelsPage(); modelsHost.ScrollTo(0); };
            box.Controls.Add(censorSeg);

            fitSeg = new Segmented { Items = Fits, OnBg = true };
            fitSeg.Selected = Math.Max(0, Math.Min(Fits.Length - 1, Settings.GetInt("ModelsFit", 0)));
            fitSeg.ItemClicked += i => { fitSeg.Selected = i; Settings.SetInt("ModelsFit", i); RefreshModelsPage(); modelsHost.ScrollTo(0); };
            box.Controls.Add(fitSeg);

            sortBtn = new PillBtn { Icon = "sort", OnBg = true };
            sortBtn.Click += (s, e) => ShowSortMenu();
            box.Controls.Add(sortBtn);
            UpdateSortBtn();

            modelSearch = new InputBox { Around = () => Ui.Bg };
            modelSearch.Box.Hint = "Пошук: qwen, кодер, переклад…";
            modelSearch.Box.TextChanged += (s, e) => { RefreshModelsPage(); modelsHost.ScrollTo(0); };
            box.Controls.Add(modelSearch);

            modelsInfo = Ui.MakeLabel(box, "", Rectangle.Empty, Ui.Small, Ui.CInk2, Ui.CBg);
            modelsInfo.AutoEllipsis = true;
            box.Controls.Add(modelsHost);

            modelList = new ModelList();
            modelList.JobOf = id => { PullJob j; return pulls.TryGetValue(id, out j) ? j : null; };
            modelList.ErrorOf = id => { string s; return pullErrors.TryGetValue(id, out s) ? s : null; };
            modelList.Action += OnModelAction;
            page.Controls.Add(modelList);

            pullTimer.Tick += (s, e) => PumpPulls();

            modelsBox = box;
            box.Resize += (s, e) => LayoutModelsHeader();
            modelsHost.Arrange += () =>
            {
                int w = ModelsWidth(modelsHost.Width);
                modelList.Columns = w >= Ui.S(1120) ? 2 : 1;   // на широкому вікні — дві колонки карток
                modelList.Bounds = new Rectangle(Ui.S(32), Ui.S(4), w, modelList.Wanted);
            };
            return box;
        }

        Control modelsBox;

        void LayoutModelsHeader()
        {
            var box = modelsBox;
            {
                int x = Ui.S(32), w = ModelsWidth(box.Width), y = Ui.S(4);
                purposeSeg.Bounds = new Rectangle(x, y, Math.Min(w, Ui.S(920)), Ui.S(32));
                y += Ui.S(44);
                int cw = Math.Min(Ui.S(320), w / 2), fw = Math.Min(Ui.S(470), w - cw - Ui.S(12));
                censorSeg.Bounds = new Rectangle(x, y, cw, Ui.S(32));
                fitSeg.Bounds = new Rectangle(x + cw + Ui.S(12), y, fw, Ui.S(32));
                int sx = fitSeg.Right + Ui.S(12), bw = sortBtn.Natural();
                if (x + w - sx - bw - Ui.S(12) < Ui.S(220))   // пошук і сортування не влазять у той самий рядок — переносимо
                {
                    y += Ui.S(46);
                    sx = x;
                }
                int sw = Math.Min(x + w - sx - bw - Ui.S(12), Ui.S(440));
                modelSearch.Bounds = new Rectangle(sx, y - Ui.S(2), sw, Ui.S(36));
                sortBtn.Bounds = new Rectangle(modelSearch.Right + Ui.S(12), y, bw, Ui.S(32));
                y += Ui.S(46);
                modelsInfo.Bounds = new Rectangle(x + Ui.S(4), y, w, Ui.S(18));
                y += Ui.S(28);
                modelsHost.Bounds = new Rectangle(0, y, box.Width, Math.Max(0, box.Height - y));
            }
        }

        static int ModelsWidth(int total) { return Math.Max(Ui.S(300), Math.Min(total - Ui.S(64), Ui.S(1560))); }

        void UpdateSortBtn()
        {
            int i = Math.Max(0, Math.Min(Sorts.Length - 1, Settings.GetInt("ModelsSort", 0)));
            sortBtn.Text = "Сортування: " + Sorts[i].Substring(0, 1).ToLower() + Sorts[i].Substring(1);
            sortBtn.Invalidate();
        }

        void ShowSortMenu()
        {
            int cur = Settings.GetInt("ModelsSort", 0);
            var items = new List<PopupItem>();
            for (int i = 0; i < Sorts.Length; i++)
            {
                int k = i;
                items.Add(new PopupItem(Sorts[i], i == cur, () =>
                {
                    Settings.SetInt("ModelsSort", k);
                    UpdateSortBtn();
                    LayoutModelsHeader();   // ширина кнопки змінилась
                    RefreshModelsPage();
                    modelsHost.ScrollTo(0);
                }));
            }
            PopupMenu.Show(sortBtn, items);
        }

        void DetectHardware()
        {
            ThreadPool.QueueUserWorkItem(_ =>
            {
                Hardware.Detect();
                try { BeginInvoke((Action)RefreshModelsPage); } catch { }
            });
        }

        // Перебудувати список за фільтрами (і після кожної зміни списку моделей в Ollama).
        void RefreshModelsPage()
        {
            if (modelList == null) return;
            var installed = new HashSet<string>(models.Select(m => m.Name), StringComparer.OrdinalIgnoreCase);
            var all = new List<CatalogModel>();
            foreach (var m in Catalog.All) { m.Installed = installed.Contains(m.Id); all.Add(m); }
            // Встановлені моделі, яких немає в каталозі, теж показуємо (без оцінки, чи піде).
            foreach (var m in models)
                if (Catalog.Find(m.Name) == null)
                    all.Add(new CatalogModel
                    {
                        Id = m.Name, Name = m.Name, Category = "other", SizeGb = (float)m.SizeGb, Installed = true, Known = false, Tags = new string[0],
                        Description = "Встановлена модель" + (string.IsNullOrEmpty(m.Params) ? "" : " · " + m.Params + " параметрів") + " (немає в каталозі Open Forge)",
                    });

            int p = purposeSeg.Selected, c = censorSeg.Selected;
            string q = modelSearch.Box.Text.Trim();
            IEnumerable<CatalogModel> list = all;
            switch (p)
            {
                case 1: list = list.Where(m => m.Category == "chat"); break;
                case 2: list = list.Where(m => m.Coder); break;
                case 3: list = list.Where(m => m.Vision); break;
                case 4: list = list.Where(m => m.Category == "reasoning"); break;
                case 5: list = list.Where(m => m.Category == "translation"); break;
                case 6: list = list.Where(m => m.Category == "small"); break;
                case 7: list = list.Where(m => m.Installed); break;
            }
            if (c == 1) list = list.Where(m => !m.Uncensored);
            if (c == 2) list = list.Where(m => m.Uncensored);
            int fsel = fitSeg.Selected;
            if (fsel > 0 && Hardware.Done && Hardware.RamGb > 0)
                list = list.Where(m => !m.Known || m.FitFor(Hardware.VramGb, Hardware.RamGb) <= (fsel == 1 ? Fit.Slow : fsel == 2 ? Fit.Ok : Fit.Fast));
            if (q.Length > 0)
                list = list.Where(m => m.Name.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 || m.Id.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 ||
                                       m.Description.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0);
            double vram = Hardware.VramGb, ram = Hardware.RamGb;
            // Спершу встановлені, далі те, що піде швидко, потім повільніше, потім те, що не потягне;
            // всередині — найсильніші зверху, а за однакової сили — порядок каталогу.
            var idx = list.Select((m, i) => new { m, i });
            List<CatalogModel> shown;
            switch (Settings.GetInt("ModelsSort", 0))
            {
                case 1: shown = idx.OrderByDescending(x => x.m.Known ? x.m.Power : 0).ThenByDescending(x => x.m.SizeGb).Select(x => x.m).ToList(); break;
                case 2: shown = idx.OrderBy(x => x.m.Known ? x.m.Power : 0).ThenBy(x => x.m.SizeGb).Select(x => x.m).ToList(); break;
                case 3: shown = idx.OrderBy(x => x.m.SizeGb).ThenBy(x => x.i).Select(x => x.m).ToList(); break;
                case 4: shown = idx.OrderByDescending(x => x.m.SizeGb).ThenBy(x => x.i).Select(x => x.m).ToList(); break;
                case 5: shown = idx.OrderBy(x => x.m.Name, StringComparer.CurrentCultureIgnoreCase).Select(x => x.m).ToList(); break;
                default:
                    shown = idx.OrderBy(x => x.m.Installed ? 0 : 1).ThenBy(x => x.m.Known ? (int)x.m.FitFor(vram, ram) : -1)
                        .ThenByDescending(x => x.m.Known ? x.m.Power : 0).ThenBy(x => x.i).Select(x => x.m).ToList();
                    break;
            }

            modelList.Items = shown;
            modelList.VramGb = vram;
            modelList.RamGb = ram;
            modelList.EmptyText = p == 7 && models.Count == 0
                ? (ollamaVersion == null ? "Ollama не запущена — встановлені моделі з'являться, щойно вона запрацює." : "Ще немає жодної моделі. Обери будь-яку в «Усі» й натисни «Завантажити».")
                : "Нічого не знайдено. Спробуй інший фільтр або пошук.";

            string pc = !Hardware.Done ? "Визначаю твій ПК…" : Hardware.GpuName.Length == 0 && Hardware.RamGb <= 0 ? "Не вдалося визначити відеокарту й пам'ять"
                : "Твій ПК: " + (Hardware.GpuName.Length > 0 ? Hardware.GpuName + ", " : "") + Hardware.VramGb.ToString("0") + " ГБ відеопам'яті, " +
                  Hardware.RamGb.ToString("0") + " ГБ RAM";
            string tail = p == 3 ? " · «Зображення» — моделі, що розуміють картинки (малювати їх Ollama не вміє)" :
                ollamaVersion == null ? " · Ollama не запущена — завантаження почнеться, коли вона запрацює" : " · сила 1–10 — наскільки модель розумна";
            modelsInfo.Text = pc + tail + " · показано " + shown.Count;
            modelList.Invalidate();
            modelsHost.Rearrange();
        }

        void OnModelAction(CatalogModel m, string action)
        {
            switch (action)
            {
                case "pull":
                    if (ollamaVersion == null)
                    {
                        MessageBox.Show(this, "Ollama не запущена. Запусти її (значок ламки біля годинника) і натисни «Завантажити» ще раз.", "Open Forge");
                        return;
                    }
                    if (m.FitFor(Hardware.VramGb, Hardware.RamGb) == Fit.No && Hardware.RamGb > 0 &&
                        MessageBox.Show(this, m.Name + " потребує близько " + (m.SizeGb + 1).ToString("0") + " ГБ пам'яті, а на твоєму ПК її замало — модель працюватиме дуже повільно або не запуститься.\n\nВсе одно завантажити?",
                            "Open Forge", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
                    pullErrors.Remove(m.Id);
                    var job = new PullJob(m.Id);
                    pulls[m.Id] = job;
                    var t = new Thread(() => Ollama.Pull(job));
                    t.IsBackground = true;
                    t.Start();
                    pullTimer.Start();
                    break;
                case "cancel":
                    PullJob j;
                    if (pulls.TryGetValue(m.Id, out j)) j.Cancel();
                    break;
                case "chat":
                    model = m.Id;
                    Settings.Set("Model", model);
                    modelBtn.Text = model;
                    LayoutChat();
                    ShowPage(0);
                    break;
                case "delete":
                    if (MessageBox.Show(this, "Видалити модель " + m.Id + " з диска? Звільниться близько " + m.SizeGb.ToString("0.0") + " ГБ.\nЇї завжди можна завантажити знову.",
                            "Open Forge", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                    string id = m.Id;
                    ThreadPool.QueueUserWorkItem(_ =>
                    {
                        string err = Ollama.Delete(id);
                        try
                        {
                            BeginInvoke((Action)(() =>
                            {
                                if (err != null) MessageBox.Show(this, "Не вдалося видалити " + id + ":\n" + err, "Open Forge");
                                Poll();
                            }));
                        }
                        catch { }
                    });
                    break;
            }
            modelList.Invalidate();
        }

        void PumpPulls()
        {
            bool finished = false;
            foreach (var kv in pulls.ToList())
            {
                var job = kv.Value;
                if (!job.Done) continue;
                pulls.Remove(kv.Key);
                if (job.Error != null) pullErrors[kv.Key] = job.Error;
                finished = true;
            }
            if (pulls.Count == 0) pullTimer.Stop();
            if (finished) Poll();   // оновити список встановлених
            modelList.Invalidate();
        }
    }
}
