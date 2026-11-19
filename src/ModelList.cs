// Open Forge — список моделей каталогу: картка на модель з позначками, чи піде вона на цьому ПК,
// і кнопками «Завантажити» / «Скасувати» / «У чат» / «Видалити». Усе малюється одним елементом.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace OpenForge
{
    class ModelList : UiControl
    {
        public List<CatalogModel> Items = new List<CatalogModel>();
        public Func<string, PullJob> JobOf = id => null;
        public Func<string, string> ErrorOf = id => null;
        public double VramGb, RamGb;
        public string EmptyText = "Нічого не знайдено. Спробуй інший фільтр або пошук.";
        public event Action<CatalogModel, string> Action;   // "pull", "cancel", "chat", "delete"

        string hoverId, hoverAction;

        public ModelList() { OnBg = true; }

        int RowH { get { return Ui.S(98); } }
        int Gap { get { return Ui.S(10); } }

        public int Columns = 1;

        public int Wanted { get { return Items.Count == 0 ? Ui.S(90) : (Items.Count + Columns - 1) / Columns * (RowH + Gap); } }

        Rectangle Row(int i)
        {
            int cw = (Width - 1 - (Columns - 1) * Gap) / Columns;
            return new Rectangle((i % Columns) * (cw + Gap), (i / Columns) * (RowH + Gap), cw, RowH);
        }

        // Кнопки праворуч у картці: (дія, підпис, синя?).
        List<Tuple<string, string, bool>> Buttons(CatalogModel m)
        {
            var list = new List<Tuple<string, string, bool>>();
            var job = JobOf(m.Id);
            if (job != null) list.Add(Tuple.Create("cancel", "Скасувати", false));
            else if (m.Installed)
            {
                list.Add(Tuple.Create("delete", "Видалити", false));
                list.Add(Tuple.Create("chat", "У чат", true));
            }
            else list.Add(Tuple.Create("pull", "Завантажити", true));
            return list;
        }

        int ButtonW(string text) { return TextRenderer.MeasureText(text, Ui.BodySemi, Size.Empty, TextFormatFlags.NoPadding).Width + Ui.S(30); }

        // Прямокутники кнопок, справа наліво.
        List<KeyValuePair<string, Rectangle>> ButtonRects(CatalogModel m, Rectangle r)
        {
            var res = new List<KeyValuePair<string, Rectangle>>();
            int x = r.Right - Ui.S(18), h = Ui.S(32), y = r.Y + (r.Height - h) / 2;
            var bs = Buttons(m);
            for (int i = bs.Count - 1; i >= 0; i--)
            {
                int w = ButtonW(bs[i].Item2);
                x -= w;
                res.Add(new KeyValuePair<string, Rectangle>(bs[i].Item1, new Rectangle(x, y, w, h)));
                x -= Ui.S(8);
            }
            return res;
        }

        // Як піде на цьому ПК — колір і підпис (однакові в каталозі й у виборі моделі).
        internal static Color FitColor(Fit f)
        {
            return f == Fit.Fast ? Ui.Green : f == Fit.Ok ? Ui.Orange : f == Fit.Slow ? Ui.Mix(Ui.Orange, Ui.Red, 0.6f) : Ui.Red;
        }

        internal static string FitLabel(Fit f)
        {
            switch (f)
            {
                case Fit.Fast: return "Швидко на твоєму ПК";
                case Fit.Ok: return "Нормально — MoE, частина в RAM";
                case Fit.Slow: return "Повільно — частина в RAM";
                default: return "Не потягне твій ПК";
            }
        }

        // Позначки моделі: «Без цензури», «Код + картинки» / «Розуміє картинки», «Міркує», «Код».
        public static List<KeyValuePair<string, Color>> BadgesOf(CatalogModel m)
        {
            var badges = new List<KeyValuePair<string, Color>>();
            if (m.Uncensored) badges.Add(new KeyValuePair<string, Color>("Без цензури", Ui.Purple));
            if (m.Vision && m.Coder) badges.Add(new KeyValuePair<string, Color>("Код + картинки", Ui.Orange));
            else if (m.Vision) badges.Add(new KeyValuePair<string, Color>("Розуміє картинки", Ui.Blue));
            if (m.Thinks) badges.Add(new KeyValuePair<string, Color>("Міркує", Ui.Ink2));
            if (m.Coder && !m.Vision) badges.Add(new KeyValuePair<string, Color>("Код", Ui.Orange));
            return badges;
        }

        static string Gb(double v) { return v.ToString(v < 10 ? "0.0" : "0") + " ГБ"; }

        // Колір сили: слабкі — сірувато-сині, середні — сині, найсильніші — фіолетові.
        internal static Color PowerColor(int p) { return p >= 8 ? Ui.Purple : p >= 5 ? Ui.Blue : Ui.Mix(Ui.Blue, Ui.Ink3, 0.45f); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Prep(e);
            if (Items.Count == 0)
            {
                TextRenderer.DrawText(g, EmptyText, Ui.Body, new Rectangle(0, Ui.S(20), Width, Ui.S(60)), Ui.Ink2,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
                return;
            }
            var clip = e.ClipRectangle;
            for (int i = 0; i < Items.Count; i++)
            {
                var r = Row(i);
                if (r.Bottom < clip.Top || r.Top > clip.Bottom) continue;
                PaintRow(g, Items[i], r);
            }
        }

        void Badge(Graphics g, ref int x, int y, string text, Color c)
        {
            int w = TextRenderer.MeasureText(text, Ui.Caps, Size.Empty, TextFormatFlags.NoPadding).Width + Ui.S(14);
            var r = new Rectangle(x, y, w, Ui.S(20));
            Ui.FillRound(g, r, r.Height / 2f, Color.FromArgb(Ui.Dark ? 52 : 30, c));
            Ui.Text(g, text, Ui.Caps, r, c, Ui.Center);
            x += w + Ui.S(6);
        }

        void PaintRow(Graphics g, CatalogModel m, Rectangle r)
        {
            Ui.FillRound(g, r, Ui.S(14), Ui.CardLine);
            float k = Math.Max(1f, Ui.Scale);
            Ui.FillRound(g, RectangleF.Inflate(r, -k, -k), Ui.S(14) - 1, Ui.Card);

            var buttons = ButtonRects(m, r);
            int left = r.X + Ui.S(18);
            int textRight = (buttons.Count > 0 ? buttons[buttons.Count - 1].Value.X : r.Right) - Ui.S(16);
            int textW = Math.Max(Ui.S(60), textRight - left);

            // 1. Назва й позначки.
            int nameW = Math.Min(textW, TextRenderer.MeasureText(m.Name, Ui.Section, Size.Empty, TextFormatFlags.NoPadding).Width + Ui.S(2));
            Ui.Text(g, m.Name, Ui.Section, new Rectangle(left, r.Y + Ui.S(12), nameW, Ui.S(24)), Ui.Ink, Ui.Left);
            int bx = left + nameW + Ui.S(10), by = r.Y + Ui.S(14);
            var badges = BadgesOf(m);
            foreach (var b in badges)
            {
                int w = TextRenderer.MeasureText(b.Key, Ui.Caps, Size.Empty, TextFormatFlags.NoPadding).Width + Ui.S(14);
                if (bx + w > textRight) break;
                Badge(g, ref bx, by, b.Key, b.Value);
            }

            // 2. Опис.
            Ui.Text(g, m.Description, Ui.Body, new Rectangle(left, r.Y + Ui.S(40), textW, Ui.S(20)), Ui.Ink2, Ui.Left);

            // 3. Як піде на цьому ПК, розмір, назва в Ollama — або прогрес завантаження.
            int my = r.Y + Ui.S(66);
            var job = JobOf(m.Id);
            string err = ErrorOf(m.Id);
            if (job != null)
            {
                var bar = new Rectangle(left, my + Ui.S(6), Math.Min(textW, Ui.S(260)), Ui.S(6));
                Ui.FillRound(g, bar, bar.Height / 2f, Ui.Fill);
                if (job.Fraction > 0)
                    Ui.FillRound(g, new RectangleF(bar.X, bar.Y, Math.Max(bar.Height, bar.Width * (float)job.Fraction), bar.Height), bar.Height / 2f, Ui.Blue);
                string t = job.Total > 0
                    ? job.Status + " · " + (int)(job.Fraction * 100) + " % · " + Gb(job.Completed / 1073741824.0) + " з " + Gb(job.Total / 1073741824.0)
                    : job.Status + "…";
                Ui.Text(g, t, Ui.Small, new Rectangle(bar.Right + Ui.S(12), my, Math.Max(0, textRight - bar.Right - Ui.S(12)), Ui.S(18)), Ui.Ink2, Ui.Left);
            }
            else if (err != null)
            {
                Ui.Text(g, "Не вдалося: " + err, Ui.Small, new Rectangle(left, my, textW, Ui.S(18)), Ui.Red, Ui.Left);
            }
            else
            {
                int x = left;
                if (m.Known)
                {
                    // Чи потягне цей ПК: зелене — швидко, помаранчеве — повільніше, червоне — не потягне.
                    var fit = m.FitFor(VramGb, RamGb);
                    Color c = FitColor(fit);
                    string ft = !Hardware.Done ? "Визначаю, чи піде…" : FitLabel(fit);
                    if (!Hardware.Done) c = Ui.Ink3;
                    float d = Ui.S(7);
                    using (var b = new SolidBrush(c)) g.FillEllipse(b, x, my + (Ui.S(18) - d) / 2f, d, d);
                    x += Ui.S(13);
                    int fw = TextRenderer.MeasureText(ft, Ui.Small, Size.Empty, TextFormatFlags.NoPadding).Width;
                    Ui.Text(g, ft, Ui.Small, new Rectangle(x, my, Math.Min(fw + 2, Math.Max(0, textRight - x)), Ui.S(18)), c, Ui.Left);
                    x += fw + Ui.S(16);

                    // Сила 1–10: десять рисочок і число.
                    if (x + Ui.S(130) < textRight)
                    {
                        int pw = m.Power;
                        Ui.Text(g, "Сила", Ui.Small, new Rectangle(x, my, Ui.S(40), Ui.S(18)), Ui.Ink2, Ui.Left);
                        x += TextRenderer.MeasureText("Сила", Ui.Small, Size.Empty, TextFormatFlags.NoPadding).Width + Ui.S(6);
                        int sw = Ui.S(5), sh = Ui.S(10), gap = Ui.S(2), sy = my + (Ui.S(18) - sh) / 2;
                        for (int q = 0; q < 10; q++)
                            Ui.FillRound(g, new Rectangle(x + q * (sw + gap), sy, sw, sh), Ui.S(1.5f), q < pw ? PowerColor(pw) : Ui.Fill);
                        x += 10 * (sw + gap) + Ui.S(4);
                        string pt = pw + "/10";
                        Ui.Text(g, pt, Ui.BodySemi, new Rectangle(x, my - Ui.S(1), Ui.S(40), Ui.S(20)), Ui.Ink, Ui.Left);
                        x += TextRenderer.MeasureText(pt, Ui.BodySemi, Size.Empty, TextFormatFlags.NoPadding).Width + Ui.S(12);
                    }
                }
                string need = m.Known ? "потрібно ~" + Gb(m.NeedGb) + " пам'яті · " : "";
                string meta = (m.Known ? "· " : "") + need + m.Id + (m.Installed ? "  ✓ встановлено" : "");
                Ui.Text(g, meta, Ui.Small, new Rectangle(x, my, Math.Max(0, textRight - x), Ui.S(18)), Ui.Ink3, Ui.Left);
            }

            // Кнопки.
            var bs = Buttons(m);
            foreach (var kv in buttons)
            {
                var spec = bs.Find(b => b.Item1 == kv.Key);
                bool hover = hoverId == m.Id && hoverAction == kv.Key;
                Color fill = spec.Item3 ? Ui.Blue : Ui.Fill;
                if (hover) fill = spec.Item3 ? Ui.Mix(Ui.Blue, Color.Black, 0.12f) : Ui.FillHover;
                Ui.FillRound(g, kv.Value, kv.Value.Height / 2f, fill);
                Color fore = spec.Item3 ? Color.White : kv.Key == "delete" ? Ui.Red : Ui.Ink;
                Ui.Text(g, spec.Item2, Ui.BodySemi, kv.Value, fore, Ui.Center);
            }
        }

        bool Hit(Point p, out CatalogModel model, out string action)
        {
            model = null;
            action = null;
            if (p.Y < 0) return false;
            int row = p.Y / (RowH + Gap), i = -1;
            for (int c = 0; c < Columns; c++)
                if (row * Columns + c < Items.Count && Row(row * Columns + c).Contains(p)) i = row * Columns + c;
            if (i < 0) return false;
            var r = Row(i);
            model = Items[i];
            foreach (var kv in ButtonRects(model, r))
                if (kv.Value.Contains(p)) { action = kv.Key; return true; }
            return false;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            CatalogModel m;
            string a;
            Hit(e.Location, out m, out a);
            string id = a != null ? m.Id : null;
            if (id != hoverId || a != hoverAction) { hoverId = id; hoverAction = a; Invalidate(); }
            Cursor = a != null ? Cursors.Hand : Cursors.Default;
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (hoverId != null) { hoverId = hoverAction = null; Invalidate(); }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left) return;
            CatalogModel m;
            string a;
            if (Hit(e.Location, out m, out a) && Action != null) Action(m, a);
        }
    }
}
