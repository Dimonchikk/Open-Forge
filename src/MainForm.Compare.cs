// Open Forge — розділ «Порівняння»: одне питання — до трьох моделей поруч.
// Кожна колонка має свою відповідь (та сама стрічка, що й у чаті: виділення, код, «Копіювати»),
// а під нею — швидкість, час і кількість токенів; найшвидша модель отримує позначку.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace OpenForge
{
    // Картка-колонка: літера, вибір моделі, кнопка «прибрати», відповідь.
    class CompareCard : UiControl
    {
        public readonly PillBtn Picker = new PillBtn { Icon = "models", Text = "Модель" };
        public readonly RoundBtn Close = new RoundBtn { Icon = "x", Bg = () => Ui.Card };
        public readonly MessageList List = new MessageList { Compact = true, OnBg = false };
        public string Letter = "A";
        public string Model;
        public ChatMessage Answer;
        public ChatStream Stream;
        public int Seen;

        public CompareCard()
        {
            OnBg = true;
            Controls.Add(Picker);
            Controls.Add(Close);
            Controls.Add(List);
            Close.Tip = "Прибрати колонку";
            List.EmptyText = "Тут з'явиться відповідь цієї моделі.";
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            int pad = Ui.S(14), top = Ui.S(12), h = Ui.S(32);
            int closeW = Close.Visible ? h : 0;
            int lx = pad + Ui.S(34);
            Picker.Bounds = new Rectangle(lx, top, Math.Max(Ui.S(60), Math.Min(Picker.Natural(), Width - lx - pad - closeW - Ui.S(8))), h);
            Close.Bounds = new Rectangle(Width - pad - h, top, h, h);
            List.Bounds = new Rectangle(Ui.S(4), top + h + Ui.S(8), Width - Ui.S(8), Math.Max(0, Height - top - h - Ui.S(14)));
        }

        public void Relayout() { OnResize(EventArgs.Empty); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Prep(e);
            var r = new RectangleF(0, 0, Width - 1, Height - 1);
            Ui.FillRound(g, r, Ui.S(14), Ui.CardLine);
            float k = Math.Max(1f, Ui.Scale);
            Ui.FillRound(g, RectangleF.Inflate(r, -k, -k), Ui.S(14) - 1, Ui.Card);
            var c = new RectangleF(Ui.S(14), Ui.S(15), Ui.S(26), Ui.S(26));
            using (var b = new SolidBrush(Ui.Mix(Ui.Card, Ui.Blue, 0.14f))) g.FillEllipse(b, c);
            Ui.Text(g, Letter, Ui.BodySemi, Rectangle.Round(c), Ui.Blue, Ui.Center);
        }
    }

    partial class MainForm
    {
        const int MaxSides = 3;
        UiControl comparePage;
        readonly List<CompareCard> cards = new List<CompareCard>();
        PillBtn addSideBtn, compareBtn, compareModelsBtn;
        Label compareHint;
        InputBox compareInput;
        MessageList compareEmpty;
        readonly System.Windows.Forms.Timer comparePump = new System.Windows.Forms.Timer { Interval = 100 };

        Control BuildCompare()
        {
            comparePage = new UiControl { OnBg = true };

            compareHint = Ui.MakeLabel(comparePage, "Обери 2–3 моделі й постав одне питання — відповіді з'являться поруч.", Rectangle.Empty, Ui.Body, Ui.CInk2, Ui.CBg);
            compareHint.AutoEllipsis = true;

            addSideBtn = new PillBtn { Icon = "plus", Text = "Додати модель", OnBg = true };
            addSideBtn.Click += (s, e) => AddSide();
            comparePage.Controls.Add(addSideBtn);

            compareInput = new InputBox { Multiline = true, MaxLines = 4 };
            compareInput.Box.Hint = "Питання для всіх моделей…";
            compareInput.Box.EnterSubmits = true;
            compareInput.Box.Submit += RunCompare;
            compareInput.WantHeight += LayoutCompare;
            comparePage.Controls.Add(compareInput);

            compareBtn = new PillBtn { Primary = true, Icon = "compare", Text = "Порівняти", OnBg = true };
            compareBtn.Click += (s, e) => RunCompare();
            comparePage.Controls.Add(compareBtn);

            compareEmpty = new MessageList();
            comparePage.Controls.Add(compareEmpty);
            compareModelsBtn = new PillBtn { Primary = true, Text = "Відкрити моделі", OnBg = true };
            compareModelsBtn.Click += (s, e) => ShowPage(1);
            comparePage.Controls.Add(compareModelsBtn);
            compareModelsBtn.BringToFront();

            comparePump.Tick += (s, e) => PumpCompare();
            comparePage.Resize += (s, e) => LayoutCompare();
            AddSide();
            AddSide();
            return comparePage;
        }

        bool CompareBusy { get { return cards.Exists(c => c.Stream != null); } }

        void AddSide()
        {
            if (cards.Count >= MaxSides || CompareBusy) return;
            var c = new CompareCard();
            int i = cards.Count;
            c.Model = Settings.Get("Compare" + i, "");
            c.Picker.Click += (s, e) => ShowCompareMenu(c);
            c.Close.Click += (s, e) => RemoveSide(c);
            cards.Add(c);
            comparePage.Controls.Add(c);
            FillCompareModels();
            LayoutCompare();
        }

        void RemoveSide(CompareCard c)
        {
            if (cards.Count <= 2 || CompareBusy) return;
            cards.Remove(c);
            comparePage.Controls.Remove(c);
            c.Dispose();
            for (int i = 0; i < cards.Count; i++) Settings.Set("Compare" + i, cards[i].Model ?? "");
            LayoutCompare();
        }

        // Після оновлення списку моделей: кожній колонці — встановлена модель (за замовчуванням різні).
        void FillCompareModels()
        {
            if (comparePage == null) return;
            var used = new HashSet<string>();
            foreach (var c in cards)
            {
                if (models.Count > 0 && !models.Exists(m => m.Name == c.Model))
                {
                    var free = models.Find(m => !used.Contains(m.Name));
                    c.Model = (free ?? models[0]).Name;
                }
                if (c.Model != null) used.Add(c.Model);
                c.Picker.Text = string.IsNullOrEmpty(c.Model) ? "Модель" : c.Model;
                c.Relayout();
            }
            LayoutCompare();
        }

        void ShowCompareMenu(CompareCard c)
        {
            if (models.Count == 0 || CompareBusy) return;
            var menu = new ContextMenuStrip();
            foreach (var m in models)
            {
                string name = m.Name;
                var it = new ToolStripMenuItem(name + "   ·   " + m.SizeGb.ToString("0.0") + " ГБ") { Checked = name == c.Model };
                it.Click += (s, e) =>
                {
                    c.Model = name;
                    Settings.Set("Compare" + cards.IndexOf(c), name);
                    c.Picker.Text = name;
                    c.Relayout();
                };
                menu.Items.Add(it);
            }
            menu.Show(c.Picker, new Point(0, c.Picker.Height + Ui.S(4)));
        }

        void LayoutCompare()
        {
            if (comparePage == null || compareInput == null) return;
            int w = comparePage.Width, h = comparePage.Height, x = Ui.S(32);
            int inner = Math.Max(Ui.S(200), w - 2 * x);
            bool ready = models.Count >= 2 && (ollamaVersion != null || firstPoll);

            compareEmpty.Visible = compareModelsBtn.Visible = !ready;
            compareHint.Visible = addSideBtn.Visible = compareInput.Visible = compareBtn.Visible = ready;
            foreach (var c in cards) c.Visible = ready;
            if (!ready)
            {
                compareEmpty.EmptyTitle = ollamaVersion == null ? "Ollama не запущена" : "Потрібні хоча б дві моделі";
                compareEmpty.EmptyText = ollamaVersion == null
                    ? "Порівняння запрацює, щойно Ollama буде запущена."
                    : "Зараз встановлено: " + models.Count + ". Додай ще одну — наприклад, звичайну й без цензури однакового розміру, щоб побачити різницю.";
                compareEmpty.Bounds = new Rectangle(0, 0, w, h);
                int bw = compareModelsBtn.Natural();
                compareModelsBtn.Visible = ollamaVersion != null;
                compareModelsBtn.Bounds = new Rectangle((w - bw) / 2, h / 2 + Ui.S(70), bw, Ui.S(36));
                compareEmpty.Invalidate();
                return;
            }

            int aw = addSideBtn.Natural();
            addSideBtn.Visible = cards.Count < MaxSides && models.Count > cards.Count;
            addSideBtn.Enabled = !CompareBusy;
            addSideBtn.Bounds = new Rectangle(x + inner - aw, Ui.S(2), aw, Ui.S(32));
            compareHint.Bounds = new Rectangle(x + Ui.S(2), Ui.S(8), Math.Max(10, (addSideBtn.Visible ? addSideBtn.Left : x + inner) - x - Ui.S(12)), Ui.S(22));

            int ih = compareInput.Wanted, bw2 = Math.Max(Ui.S(130), compareBtn.Natural());
            compareInput.Bounds = new Rectangle(x, h - ih - Ui.S(20), inner - bw2 - Ui.S(10), ih);
            compareBtn.Bounds = new Rectangle(x + inner - bw2, compareInput.Bottom - Ui.S(40), bw2, Ui.S(40));

            int top = Ui.S(46), bottom = compareInput.Top - Ui.S(14), gap = Ui.S(14);
            int n = cards.Count, cw = (inner - gap * (n - 1)) / Math.Max(1, n);
            for (int i = 0; i < n; i++)
            {
                var c = cards[i];
                c.Letter = ((char)('A' + i)).ToString();
                c.Close.Visible = n > 2;
                c.Close.Enabled = !CompareBusy;
                c.Picker.Enabled = !CompareBusy;
                c.Bounds = new Rectangle(x + i * (cw + gap), top, cw, Math.Max(Ui.S(100), bottom - top));
                c.Relayout();
                c.Invalidate();
            }
        }

        void RunCompare()
        {
            if (CompareBusy)
            {
                foreach (var c in cards) if (c.Stream != null) c.Stream.Cancel();
                return;
            }
            string prompt = compareInput.Box.Text.Trim();
            if (prompt.Length == 0 || models.Count == 0) return;
            string sys = Settings.Get("SystemPrompt", "");
            foreach (var c in cards)
            {
                if (string.IsNullOrEmpty(c.Model)) continue;
                c.List.Clear();
                c.Answer = new ChatMessage("assistant", "") { Streaming = true };   // назва моделі вже вгорі колонки
                c.List.Add(c.Answer);
                var s = new ChatStream();
                c.Stream = s;
                c.Seen = 0;
                string mdl = c.Model;
                var history = new List<Turn> { new Turn("user", prompt) };
                var t = new Thread(() => Ollama.Chat(mdl, sys, history, s));
                t.IsBackground = true;
                t.Start();
            }
            compareBtn.Text = "Зупинити";
            compareBtn.Icon = "stop";
            compareBtn.Primary = false;
            compareBtn.Invalidate();
            comparePump.Start();
            LayoutCompare();
        }

        void PumpCompare()
        {
            bool any = false;
            foreach (var c in cards)
            {
                var s = c.Stream;
                if (s == null) continue;
                int seen = c.Seen;
                string t = s.TakeIfChanged(ref seen);
                c.Seen = seen;
                if (t != null) { c.Answer.Raw = t; c.List.Changed(c.Answer); }
                if (!s.Done) { any = true; continue; }
                // Готово: підсумок під відповіддю.
                c.Stream = null;
                c.Answer.Streaming = false;
                c.Answer.Raw = s.Text;
                var parts = new List<string>();
                if (s.TokensPerSecond > 0) parts.Add(s.TokensPerSecond.ToString("0.0") + " ток/с");
                parts.Add((s.Finished - s.Started).TotalSeconds.ToString("0.0") + " с");
                if (s.EvalCount > 0) parts.Add(s.EvalCount + " токенів");
                if (s.Cancelled) parts.Add("зупинено");
                c.Answer.Stats = string.Join(" · ", parts.ToArray());
                c.Answer.Tag = s.TokensPerSecond;
                if (c.Answer.Display.Length == 0) c.List.Remove(c.Answer);
                if (s.Error != null) c.List.Add(new ChatMessage("error", s.Error));
                c.List.Changed(c.Answer);
            }
            if (any) return;
            comparePump.Stop();
            compareBtn.Text = "Порівняти";
            compareBtn.Icon = "compare";
            compareBtn.Primary = true;
            compareBtn.Invalidate();

            // Найшвидша — якщо швидкість відома хоча б у двох.
            CompareCard best = null;
            int known = 0;
            foreach (var c in cards)
            {
                if (c.Answer == null || !(c.Answer.Tag > 0)) continue;
                known++;
                if (best == null || c.Answer.Tag > best.Answer.Tag) best = c;
            }
            if (known >= 2 && best != null)
            {
                best.Answer.Stats += " · найшвидша ⚡";
                best.List.Changed(best.Answer);
            }
            LayoutCompare();
        }
    }
}
