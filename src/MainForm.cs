// Open Forge — головне вікно: бокове меню, шапка з назвою розділу, станом Ollama й темою, і сторінки розділів.
// Кожен розділ — у своєму файлі MainForm.<Розділ>.cs.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace OpenForge
{
    partial class MainForm : Form
    {
        const int SideW = 236, HeaderH = 88;
        static readonly string[] Titles = { "Чат", "Налаштування", "Налаштування", "Налаштування" };
        static readonly string[] Tabs = { "Моделі", "Порівняння", "Загальні" };
        static readonly string[] Subtitles =
        {
            "Розмова з моделлю, що працює прямо на твоєму ПК",
            "Встановлені моделі й каталог з рекомендаціями під твій ПК",
            "Одне питання — кілька моделей поруч",
            "Інструкція для моделі, твій ПК і про програму",
        };

        readonly Sidebar sidebar = new Sidebar();
        readonly Label lblTitle, lblSub;
        readonly ThemeSwitch theme = new ThemeSwitch();
        readonly StatusPill status = new StatusPill();
        RoundBtn ollamaBtn;
        bool ollamaStarting;
        readonly Segmented settingsTabs = new Segmented { Items = Tabs, OnBg = true };
        readonly Control[] pages = new Control[4];
        int current = -1;

        // Стан Ollama (оновлюється у фоні кожні 3 секунди).
        string ollamaVersion;
        List<ModelInfo> models = new List<ModelInfo>();
        bool polling, firstPoll = true;
        System.Threading.Timer monitorTimer;
        volatile bool minimized;
        readonly System.Windows.Forms.Timer poll = new System.Windows.Forms.Timer { Interval = 3000 };
        readonly System.Windows.Forms.Timer updateTimer = new System.Windows.Forms.Timer { Interval = (int)Updates.Every.TotalMilliseconds };

        public static bool WantDark()
        {
            int t = Settings.GetInt("Theme", 0);
            return t == 2 || (t == 0 && Ui.SystemDark());
        }

        public MainForm(EventWaitHandle show)
        {
            Text = "Open Forge";
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.Sizable;
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(Ui.S(820), Ui.S(560));
            ClientSize = new Size(Ui.S(1180), Ui.S(780));
            DoubleBuffered = true;
            KeyPreview = true;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            sidebar.ItemClicked += i =>
            {
                if (i == 0) { ShowPage(0); NewChat(); }
                else ShowPage(Math.Max(1, Math.Min(3, Settings.GetInt("SettingsTab", 1))));
            };
            settingsTabs.ItemClicked += i => ShowPage(i + 1);
            Controls.Add(settingsTabs);
            sidebar.UpdateClicked += () => Program.Open(Updates.Download);
            Controls.Add(sidebar);

            lblTitle = Ui.MakeLabel(this, "", Rectangle.Empty, Ui.H1, Ui.CInk, Ui.CBg);
            lblSub = Ui.MakeLabel(this, "", Rectangle.Empty, Ui.Body, Ui.CInk2, Ui.CBg);
            lblTitle.AutoEllipsis = lblSub.AutoEllipsis = true;
            status.Resized += () => OnResize(EventArgs.Empty);
            Controls.Add(status);
            ollamaBtn = new RoundBtn { Icon = "power", Bg = () => Ui.Bg };
            ollamaBtn.Click += (s, e) => OllamaButton();
            Controls.Add(ollamaBtn);
            theme.Selected = Math.Max(0, Math.Min(2, Settings.GetInt("Theme", 0)));
            theme.Changed += i => { Settings.SetInt("Theme", i); ApplyTheme(); };
            Controls.Add(theme);

            pages[0] = BuildChat();
            pages[1] = BuildModels();
            pages[2] = BuildCompare();
            pages[3] = BuildSettings();
            foreach (var p in pages) { p.Visible = false; Controls.Add(p); }

            status.Set("Перевіряю Ollama…", Ui.Ink3);
            poll.Tick += (s, e) => Poll();
            updateTimer.Tick += (s, e) => CheckUpdates();
            SystemEvents.UserPreferenceChanged += OnSystemTheme;

            if (show != null)
            {
                // Друга копія програми просить показати вікно.
                var t = new Thread(() =>
                {
                    while (true)
                    {
                        show.WaitOne();
                        try { BeginInvoke((Action)BringUp); } catch { }
                    }
                });
                t.IsBackground = true;
                t.Start();
            }

            Application.AddMessageFilter(new WheelRouter(this));
            BackColor = Ui.Bg;
            OnResize(EventArgs.Empty);
            ShowPage(Math.Max(0, Math.Min(3, Settings.GetInt("Page", 0))));
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Ui.StyleCaption(this);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Poll();
            poll.Start();
            // Навантаження ПК у боковому меню — опитуємо у фоні раз на 1,5 с (лише коли вікно не згорнуте).
            monitorTimer = new System.Threading.Timer(_ =>
            {
                if (minimized) return;
                PcMonitor.Sample();
                try { BeginInvoke((Action)sidebar.RefreshMeters); } catch { }
            }, null, 0, 1500);
            DetectHardware();
            CheckUpdates();
            updateTimer.Start();
            if (current == 0) chatInput.Box.Focus();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            SaveSystemPrompt();
            if (monitorTimer != null) monitorTimer.Dispose();
            if (stream != null) stream.Cancel();
            SystemEvents.UserPreferenceChanged -= OnSystemTheme;
            base.OnFormClosing(e);
        }

        void BringUp()
        {
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            Show();
            Activate();
            BringToFront();
        }

        void ShowPage(int i)
        {
            if (i == current) return;
            current = i;
            sidebar.Selected = i == 0 ? (chatId == null ? 0 : -1) : 1;
            Settings.SetInt("Page", i);
            if (i >= 1) { Settings.SetInt("SettingsTab", i); settingsTabs.Selected = i - 1; }
            settingsTabs.Visible = i >= 1;
            OnResize(EventArgs.Empty);
            lblTitle.Text = Titles[i];
            lblSub.Text = Subtitles[i];
            for (int k = 0; k < pages.Length; k++) pages[k].Visible = k == i;
            if (i == 0 && chatInput != null && IsHandleCreated) chatInput.Box.Focus();
            if (i == 3) RefreshSettings();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            minimized = WindowState == FormWindowState.Minimized;
            if (WindowState == FormWindowState.Minimized || pages[pages.Length - 1] == null) return;   // ще будується
            int w = ClientSize.Width, h = ClientSize.Height, side = Ui.S(SideW);
            sidebar.Bounds = new Rectangle(0, 0, side, h);
            int x = side + Ui.S(32), right = w - Ui.S(28);
            theme.Bounds = new Rectangle(right - Ui.S(108), Ui.S(30), Ui.S(108), Ui.S(32));
            int sw = Math.Max(Ui.S(120), status.Natural());
            status.Bounds = new Rectangle(theme.Left - Ui.S(12) - sw, Ui.S(30), sw, Ui.S(32));
            ollamaBtn.Bounds = new Rectangle(status.Left - Ui.S(40), Ui.S(30), Ui.S(32), Ui.S(32));
            lblTitle.Bounds = new Rectangle(x - Ui.S(3), Ui.S(22), Math.Max(10, ollamaBtn.Left - x - Ui.S(10)), Ui.S(36));
            lblSub.Bounds = new Rectangle(x - Ui.S(1), Ui.S(56), Math.Max(10, ollamaBtn.Left - x - Ui.S(10)), Ui.S(20));   // не налазить на пігулку стану
            var area = new Rectangle(side, Ui.S(HeaderH), w - side, Math.Max(0, h - Ui.S(HeaderH)));
            pages[0].Bounds = area;
            // Вкладки «Моделі / Порівняння / Загальні» під заголовком «Налаштування».
            settingsTabs.Bounds = new Rectangle(x - Ui.S(2), Ui.S(HeaderH) + Ui.S(2), Math.Min(Ui.S(420), w - x - Ui.S(28)), Ui.S(32));
            var sub = new Rectangle(side, settingsTabs.Bottom + Ui.S(14), w - side, Math.Max(0, h - settingsTabs.Bottom - Ui.S(14)));
            for (int k = 1; k < pages.Length; k++) pages[k].Bounds = sub;
        }

        // ── тема ────────────────────────────────────────────────────────────────

        void OnSystemTheme(object sender, UserPreferenceChangedEventArgs e)
        {
            if (e.Category != UserPreferenceCategory.General || Settings.GetInt("Theme", 0) != 0) return;
            try { BeginInvoke((Action)ApplyTheme); } catch { }
        }

        void ApplyTheme()
        {
            bool dark = WantDark();
            if (dark == Ui.Dark) return;
            Ui.ApplyTheme(dark);
            BackColor = Ui.Bg;
            foreach (var a in Ui.Rethemers) a();
            Ui.StyleCaption(this);
            InvalidateAll(this);
        }

        static void InvalidateAll(Control c)
        {
            c.Invalidate();
            foreach (Control k in c.Controls) InvalidateAll(k);
        }

        // ── Ollama й оновлення у фоні ──────────────────────────────────────────

        // Кнопка біля пігулки стану: запустити Ollama, а якщо працює — меню «Перезапустити / Зупинити».
        void OllamaButton()
        {
            if (ollamaVersion == null)
            {
                if (!OllamaControl.Installed)
                {
                    if (MessageBox.Show(this, "Ollama не встановлена. Відкрити сторінку завантаження ollama.com?", "Open Forge",
                            MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                        try { System.Diagnostics.Process.Start(OllamaControl.DownloadUrl); } catch { }
                    return;
                }
                StartOllama();
                return;
            }
            PopupMenu.Show(ollamaBtn, new List<PopupItem>
            {
                new PopupItem("Перезапустити Ollama", false, () => RestartOllama(false)) { Sub = "якщо модель зависла" },
                new PopupItem("Зупинити Ollama", false, () => RestartOllama(true)) { Sub = "звільнить відеопам'ять" },
            });
        }

        void StartOllama()
        {
            string err = OllamaControl.Start();
            if (err != null) { MessageBox.Show(this, "Не вдалося запустити Ollama:\n" + err, "Open Forge"); return; }
            ollamaStarting = true;
            status.Set("Запускаю Ollama…", Ui.Ink3);
            // Перевіряти частіше, поки не запрацює (до ~30 с).
            int tries = 0;
            var t = new System.Windows.Forms.Timer { Interval = 1000 };
            t.Tick += (s, e) =>
            {
                Poll();
                if (ollamaVersion != null || ++tries > 30) { t.Stop(); t.Dispose(); if (ollamaVersion == null) { ollamaStarting = false; status.Set("Ollama не запущена", Ui.Orange); } }
            };
            t.Start();
        }

        void RestartOllama(bool stopOnly)
        {
            if (Busy && MessageBox.Show(this, "Модель зараз відповідає — відповідь обірветься. Продовжити?", "Open Forge",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            status.Set(stopOnly ? "Зупиняю Ollama…" : "Перезапускаю Ollama…", Ui.Ink3);
            ThreadPool.QueueUserWorkItem(_ =>
            {
                OllamaControl.Stop();
                try
                {
                    BeginInvoke((Action)(() =>
                    {
                        ollamaVersion = null;
                        if (stopOnly) { status.Set("Ollama не запущена", Ui.Orange); Poll(); }
                        else StartOllama();
                    }));
                }
                catch { }
            });
        }

        void Poll()
        {
            if (polling) return;
            polling = true;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                string v = Ollama.Version();
                var list = v != null ? Ollama.Models() : null;
                try { BeginInvoke((Action)(() => OnOllama(v, list))); } catch { }
            });
        }

        void OnOllama(string version, List<ModelInfo> list)
        {
            polling = false;
            bool changed = version != ollamaVersion || firstPoll;
            firstPoll = false;
            ollamaVersion = version;
            if (version != null) { ollamaStarting = false; status.Set("Ollama " + version + " працює", Ui.Green); }
            else if (ollamaStarting) status.Set("Запускаю Ollama…", Ui.Ink3);
            else status.Set("Ollama не запущена", Ui.Orange);
            ollamaBtn.Tip = version != null ? "Ollama: перезапустити або зупинити" : "Запустити Ollama";
            bool prim = version == null && !ollamaStarting;
            if (ollamaBtn.Primary != prim) { ollamaBtn.Primary = prim; ollamaBtn.Invalidate(); }
            if (list != null && !SameModels(list, models)) { models = list; changed = true; }
            else if (version == null && models.Count > 0) { models = new List<ModelInfo>(); changed = true; }
            if (!changed) return;
            OnModelsChanged();
            RefreshModelsPage();
        }

        static bool SameModels(List<ModelInfo> a, List<ModelInfo> b)
        {
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++) if (a[i].Name != b[i].Name || a[i].Size != b[i].Size) return false;
            return true;
        }

        void CheckUpdates()
        {
            ThreadPool.QueueUserWorkItem(_ =>
            {
                Updates.CheckNow();
                try { BeginInvoke((Action)(() => sidebar.NewVersion = Updates.Latest)); } catch { }
            });
        }

        // ── спільне для сторінок ────────────────────────────────────────────────
    }
}
