// Open Forge — бічна панель чату (як «Прогрес» у Claude): що агент робить зараз, що він створив
// (сайт, файли, скріншоти) і що використано в розмові (файли, модель, папка, скільки «пам'яті розмови» зайнято).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace OpenForge
{
    class PanelData
    {
        public bool HasAgent, Live;
        public string Status;                 // «Готово · змінено файлів: 2» або «Працює»
        public DateTime Since;
        public readonly List<KeyValuePair<string, int>> Steps = new List<KeyValuePair<string, int>>();   // текст, стан: 0 ✓, 1 ✗, 2 триває
        public string SitePath, SiteShot;     // index.html агента і його скріншот (base64)
        public readonly List<string> Files = new List<string>();      // повні шляхи створених файлів (нові — зверху)
        public readonly List<string> Shots = new List<string>();      // скріншоти агента (base64)
        public readonly List<string> Attached = new List<string>();   // назви доданих тобою файлів
        public int Photos;
        public readonly List<CatalogModel> Models = new List<CatalogModel>();
        public readonly List<string> OtherModels = new List<string>();   // моделі не з каталогу
        public string Folder, Access;
        public int TokUsed, TokMax;
        public bool Empty { get { return !HasAgent && Attached.Count == 0 && Photos == 0 && Models.Count == 0 && OtherModels.Count == 0 && Folder == null; } }
    }

    class SidePanel : UiControl, IWheel
    {
        PanelData data = new PanelData();
        int offset, total;
        readonly List<KeyValuePair<Rectangle, string>> hits = new List<KeyValuePair<Rectangle, string>>();
        string hover;
        readonly Dictionary<string, Bitmap> thumbs = new Dictionary<string, Bitmap>();
        public event Action Close;
        public event Action<string> PreviewSite;

        public SidePanel() { OnBg = true; }

        public PanelData Data
        {
            set
            {
                data = value ?? new PanelData();
                offset = Math.Max(0, Math.Min(offset, MaxOffset));
                Invalidate();
            }
        }

        int MaxOffset { get { return Math.Max(0, total - Height + Ui.S(12)); } }

        public void Wheel(int delta)
        {
            int o = Math.Max(0, Math.Min(MaxOffset, offset - delta / 120 * Ui.S(60)));
            if (o != offset) { offset = o; Invalidate(); }
        }

        protected override void OnMouseWheel(MouseEventArgs e) { Wheel(e.Delta); }

        // Мініатюра скріншота: обрізана зверху до потрібної висоти (сторінку видно з «шапки»).
        Bitmap Thumb(string b64, int w, int h)
        {
            string key = (b64.GetHashCode()) + ":" + w + "x" + h;
            Bitmap b;
            if (thumbs.TryGetValue(key, out b)) return b;
            if (thumbs.Count > 40) { foreach (var t in thumbs.Values) t.Dispose(); thumbs.Clear(); }
            var img = Pictures.Decode(b64);
            if (img == null) return null;
            using (img)
            {
                b = new Bitmap(w, h);
                using (var g = Graphics.FromImage(b))
                {
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    float k = Math.Max((float)w / img.Width, (float)h / img.Height);
                    g.DrawImage(img, 0, 0, img.Width * k, img.Height * k);
                }
            }
            thumbs[key] = b;
            return b;
        }

        void Hit(Rectangle r, string key) { hits.Add(new KeyValuePair<Rectangle, string>(r, key)); }

        static string Dur(TimeSpan t)
        {
            int s = (int)t.TotalSeconds;
            return s >= 60 ? s / 60 + " хв " + s % 60 + " с" : s + " с";
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Prep(e);
            hits.Clear();
            // Картка панелі.
            // Висота картки — за вмістом (як у Claude), але не більша за панель; нижче — просто тло.
            int cardH = Math.Min(Height - 1, Math.Max(Ui.S(60), total + Ui.S(12)));
            var card = new Rectangle(0, 0, Width - 1, cardH);
            Ui.FillRound(g, card, Ui.S(14), Ui.CardLine);
            float kk = Math.Max(1f, Ui.Scale);
            Ui.FillRound(g, RectangleF.Inflate(card, -kk, -kk), Ui.S(14) - 1, Ui.Card);

            int pad = Ui.S(16), w = Width - 2 * pad;
            int top = Ui.S(4);
            var clip = new Rectangle(1, top, Width - 2, cardH - top - 1);
            g.SetClip(clip);
            int y = top - offset;
            firstSec = true;

            if (data.Empty)
            {
                TextRenderer.DrawText(g, "Тут з'являтиметься, що зробив агент, які файли ти додав і скільки пам'яті розмови зайнято.", Ui.Small,
                    new Rectangle(pad, y + Ui.S(16), w - Ui.S(36), Ui.S(80)), Ui.Ink3, TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
                y += Ui.S(90);
            }

            // ── Прогрес агента ─────────────────────────────────────────────
            if (data.HasAgent && Section(g, "Прогрес", "progress", data.Live ? "працює" : Pl(data.Steps.Count, "крок", "кроки", "кроків"), ref y, pad, w))
            {
                var sr = new Rectangle(pad, y, w, Ui.S(24));
                if (data.Live)
                {
                    int phase = (int)(DateTime.UtcNow.Ticks / TimeSpan.TicksPerMillisecond / 280 % 3);
                    for (int k = 0; k < 3; k++)
                        using (var b = new SolidBrush(Color.FromArgb(k == phase ? 255 : 90, Ui.Orange)))
                            g.FillEllipse(b, sr.X + k * Ui.S(6), sr.Y + Ui.S(10), Ui.S(4), Ui.S(4));
                    Ui.Text(g, "Працює · " + Dur(DateTime.UtcNow - data.Since), Ui.BodySemi, new Rectangle(sr.X + Ui.S(24), sr.Y, w - Ui.S(24), sr.Height), Ui.Ink, Ui.Left);
                }
                else
                {
                    using (var b = new SolidBrush(Ui.Green)) g.FillEllipse(b, sr.X, sr.Y + Ui.S(8), Ui.S(8), Ui.S(8));
                    Ui.Text(g, data.Status ?? "Готово", Ui.BodySemi, new Rectangle(sr.X + Ui.S(16), sr.Y, w - Ui.S(16), sr.Height), Ui.Ink, Ui.Left);
                }
                y += Ui.S(30);
                int from = Math.Max(0, data.Steps.Count - 12);
                if (from > 0)
                {
                    Ui.Text(g, "… ще " + from + " кроків раніше", Ui.Small, new Rectangle(pad + Ui.S(16), y, w, Ui.S(20)), Ui.Ink3, Ui.Left);
                    y += Ui.S(22);
                }
                for (int i = from; i < data.Steps.Count; i++)
                {
                    var st = data.Steps[i];
                    var r = new Rectangle(pad, y, w, Ui.S(22));
                    Color mark = st.Value == 0 ? Ui.Green : st.Value == 1 ? Ui.Red : Ui.Orange;
                    string sym = st.Value == 0 ? "✓" : st.Value == 1 ? "✗" : "•";
                    Ui.Text(g, sym, Ui.BodySemi, new Rectangle(r.X, r.Y, Ui.S(16), r.Height), mark, Ui.Left);
                    Ui.Text(g, st.Key, Ui.Small, new Rectangle(r.X + Ui.S(18), r.Y, w - Ui.S(18), r.Height), st.Value == 1 ? Ui.Mix(Ui.Ink2, Ui.Red, 0.6f) : Ui.Ink2, Ui.Left);
                    y += Ui.S(22);
                }
                y += Ui.S(14);
            }

            // ── Вивід ──────────────────────────────────────────────────────
            if ((data.SitePath != null || data.Files.Count > 0 || data.Shots.Count > 0) &&
                Section(g, "Вивід", "output", (data.Files.Count > 0 ? Pl(data.Files.Count, "файл", "файли", "файлів") : "") + (data.Shots.Count > 0 ? (data.Files.Count > 0 ? " · " : "") + Pl(data.Shots.Count, "знімок", "знімки", "знімків") : ""), ref y, pad, w))
            {
                if (data.SitePath != null)
                {
                    // Велика картка сайту з превʼю (як «Артефакт» у Claude).
                    int ih = Ui.S(130);
                    int ch = (data.SiteShot != null ? ih : 0) + Ui.S(52);
                    var cr = new Rectangle(pad, y, w, ch);
                    Ui.FillRound(g, cr, Ui.S(10), hover == "site" ? Ui.FillHover : Ui.Fill);
                    if (data.SiteShot != null)
                    {
                        var pic = Thumb(data.SiteShot, w - Ui.S(16), ih - Ui.S(8));
                        if (pic != null)
                        {
                            var pr = new Rectangle(cr.X + Ui.S(8), cr.Y + Ui.S(8), pic.Width, pic.Height);
                            using (var path = Ui.Round(pr, Ui.S(6))) { var old = g.Clip; g.SetClip(path, System.Drawing.Drawing2D.CombineMode.Intersect); g.DrawImage(pic, pr); g.Clip = old; }
                        }
                    }
                    int ty = cr.Y + (data.SiteShot != null ? ih : 0) + Ui.S(6);
                    Ui.Text(g, Path.GetFileName(Path.GetDirectoryName(data.SitePath)) + " · index.html", Ui.BodySemi, new Rectangle(cr.X + Ui.S(12), ty, w - Ui.S(24), Ui.S(20)), Ui.Ink, Ui.Left);
                    Ui.Text(g, "Сайт · клік — переглянути", Ui.Small, new Rectangle(cr.X + Ui.S(12), ty + Ui.S(20), w - Ui.S(24), Ui.S(18)), Ui.Ink3, Ui.Left);
                    Hit(cr, "site");
                    y += ch + Ui.S(8);
                }
                for (int i = 0; i < data.Files.Count; i++)
                {
                    string f = data.Files[i];
                    var r = new Rectangle(pad - Ui.S(6), y, w + Ui.S(12), Ui.S(32));
                    string key = "file:" + i;
                    if (hover == key) Ui.FillRound(g, r, Ui.S(8), Ui.Fill);
                    float s = Ui.S(15);
                    Icons.Draw(g, "file", new RectangleF(r.X + Ui.S(8), r.Y + (r.Height - s) / 2f, s, s), Ui.Ink2);
                    string ext = Path.GetExtension(f).TrimStart('.').ToUpperInvariant();
                    int ew = TextRenderer.MeasureText(ext, Ui.Caps).Width;
                    Ui.Text(g, ext, Ui.Caps, new Rectangle(r.Right - ew - Ui.S(8), r.Y, ew, r.Height), Ui.Ink3, Ui.Left);
                    string rel = data.Folder != null && f.StartsWith(data.Folder, StringComparison.OrdinalIgnoreCase) ? f.Substring(data.Folder.Length).TrimStart('\\', '/') : Path.GetFileName(f);
                    Ui.Text(g, rel, Ui.Body, new Rectangle(r.X + Ui.S(32), r.Y, r.Width - Ui.S(44) - ew, r.Height), Ui.Ink, Ui.Left);
                    Hit(r, key);
                    y += Ui.S(32);
                }
                if (data.Shots.Count > 0)
                {
                    y += Ui.S(6);
                    int tw = (w - Ui.S(12)) / 3, th = tw * 5 / 8, x = pad;
                    for (int i = 0; i < data.Shots.Count; i++)
                    {
                        if (i > 0 && i % 3 == 0) { x = pad; y += th + Ui.S(6); }
                        var r = new Rectangle(x, y, tw, th);
                        var pic = Thumb(data.Shots[i], tw, th);
                        if (pic != null)
                            using (var path = Ui.Round(r, Ui.S(6)))
                            {
                                var old = g.Clip;
                                g.SetClip(path, System.Drawing.Drawing2D.CombineMode.Intersect);
                                g.DrawImage(pic, r);
                                g.Clip = old;
                                using (var pen = new Pen(hover == "shot:" + i ? Ui.Blue : Ui.Separator, kk)) g.DrawPath(pen, path);
                            }
                        Hit(r, "shot:" + i);
                        x += tw + Ui.S(6);
                    }
                    y += th + Ui.S(6);
                }
                y += Ui.S(14);
            }

            // ── Використано ────────────────────────────────────────────────
            if (!data.Empty && Section(g, "Використано в цій розмові", "used", "", ref y, pad, w))
            {
                foreach (var m in data.Models) y = Row(g, "models", m.Name, (m.Uncensored ? "без цензури · " : "") + "сила " + m.Power + "/10", pad, y, w);
                foreach (string m in data.OtherModels) y = Row(g, "models", m, "", pad, y, w);
                if (data.Attached.Count > 0)
                    y = Row(g, "file", data.Attached.Count == 1 ? data.Attached[0] : data.Attached[0] + " +" + (data.Attached.Count - 1),
                        data.Attached.Count == 1 ? "файл" : data.Attached.Count + " файлів", pad, y, w);
                if (data.Photos > 0) y = Row(g, "image", "Фото й скріншоти", data.Photos.ToString(), pad, y, w);
                if (data.Folder != null)
                {
                    string fn = Path.GetFileName(data.Folder.TrimEnd('\\', '/'));
                    var r = new Rectangle(pad - Ui.S(6), y, w + Ui.S(12), RowH(fn, data.Access ?? "", w));
                    if (hover == "folder") Ui.FillRound(g, r, Ui.S(8), Ui.Fill);
                    y = Row(g, "folder", fn, data.Access ?? "", pad, y, w);
                    Hit(r, "folder");
                }
                // Пам'ять розмови: скільки контексту зайнято.
                if (data.TokMax > 0)
                {
                    y += Ui.S(6);
                    float frac = Math.Min(1f, (float)data.TokUsed / data.TokMax);
                    string t = "Пам'ять розмови: ~" + K(data.TokUsed) + " з " + K(data.TokMax) + " токенів";
                    int th = TextRenderer.MeasureText(g, t, Ui.Small, new Size(w, 0), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix).Height;
                    TextRenderer.DrawText(g, t, Ui.Small, new Rectangle(pad, y, w, th + Ui.S(2)), Ui.Ink2, TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
                    y += th + Ui.S(6);
                    var bar = new Rectangle(pad, y, w, Ui.S(6));
                    Ui.FillRound(g, bar, bar.Height / 2f, Ui.Fill);
                    Color bc = frac > 0.85f ? Ui.Red : frac > 0.6f ? Ui.Orange : Ui.Blue;
                    if (frac > 0) Ui.FillRound(g, new RectangleF(bar.X, bar.Y, Math.Max(bar.Height, bar.Width * frac), bar.Height), bar.Height / 2f, bc);
                    y += Ui.S(12);
                    if (frac > 0.85f)
                    {
                        TextRenderer.DrawText(g, "Майже заповнено — модель почне забувати початок. Почни новий чат або збільш «Пам'ять розмови» в налаштуваннях.",
                            Ui.Small, new Rectangle(pad, y, w, Ui.S(54)), Ui.Red, TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
                        y += Ui.S(54);
                    }
                }
                y += Ui.S(10);   // запас знизу, щоб останній рядок не притискався до краю
            }
            int newTotal = y + offset - top;
            g.ResetClip();
            if (newTotal != total) { total = newTotal; BeginInvoke((Action)Invalidate); }   // висота картки змінилась

            // Хрестик — нерухомий, у першому рядку, поверх прокрученого вмісту (на тлі картки).
            var cx = new Rectangle(Width - Ui.S(12) - Ui.S(28), Ui.S(16), Ui.S(28), Ui.S(28));
            Ui.FillRound(g, Rectangle.Inflate(cx, Ui.S(3), Ui.S(3)), Ui.S(9), Ui.Card);
            if (hover == "close") Ui.FillRound(g, cx, Ui.S(7), Ui.Fill);
            Icons.Draw(g, "x", RectangleF.Inflate(cx, -Ui.S(7), -Ui.S(7)), Ui.Ink2);
            closeRect = cx;
        }

        static string K(int n) { return n >= 1000 ? (n / 1000.0).ToString(n >= 10000 ? "0" : "0.0") + "K" : n.ToString(); }

        // Заголовок розділу зі стрілкою: клік згортає й розгортає (стан запам'ятовується).
        // Повертає true, якщо розділ розгорнутий і треба малювати його вміст.
        static HashSet<string> collapsed;
        static HashSet<string> Collapsed
        {
            get
            {
                if (collapsed == null)
                {
                    collapsed = new HashSet<string>();
                    foreach (string k in Settings.Get("PanelCollapsed", "").Split(',')) if (k.Length > 0) collapsed.Add(k);
                }
                return collapsed;
            }
        }

        bool firstSec;
        Rectangle closeRect;

        bool Section(Graphics g, string title, string key, string summary, ref int y, int pad, int w)
        {
            bool open = !Collapsed.Contains(key);
            float kk = Math.Max(1f, Ui.Scale);
            if (!firstSec)
                using (var pen = new Pen(Ui.Dark ? Ui.Separator : Ui.CardLine, kk)) g.DrawLine(pen, pad, y, pad + w, y);
            int right = firstSec ? Ui.S(40) : 0;   // у першому рядку праворуч — хрестик
            firstSec = false;
            int rh = Ui.S(52);
            var r = new Rectangle(pad - Ui.S(8), y + Ui.S(6), w + Ui.S(16) - right, rh - Ui.S(12));
            if (hover == "sec:" + key) Ui.FillRound(g, r, Ui.S(8), Ui.Fill);
            int tw = TextRenderer.MeasureText(title, Ui.BodySemi, Size.Empty, TextFormatFlags.NoPadding).Width;
            Ui.Text(g, title, Ui.BodySemi, new Rectangle(pad, y, tw + Ui.S(4), rh), Ui.Ink, Ui.Left);
            // Стрілка: › — згорнуто, ⌄ — розгорнуто.
            float cx = pad + tw + Ui.S(13), cy = y + rh / 2f, a = Ui.S(3.5f);
            using (var pen = new Pen(Ui.Ink3, Math.Max(1.4f, Ui.S(1.6f))) { StartCap = System.Drawing.Drawing2D.LineCap.Round, EndCap = System.Drawing.Drawing2D.LineCap.Round, LineJoin = System.Drawing.Drawing2D.LineJoin.Round })
                g.DrawLines(pen, open ? new[] { new PointF(cx - a, cy - a / 2), new PointF(cx, cy + a / 2), new PointF(cx + a, cy - a / 2) }
                                      : new[] { new PointF(cx - a / 2, cy - a), new PointF(cx + a / 2, cy), new PointF(cx - a / 2, cy + a) });
            if (!open && summary.Length > 0)
            {
                int sw = TextRenderer.MeasureText(summary, Ui.Small).Width;
                Ui.Text(g, summary, Ui.Small, new Rectangle(pad + w - right - sw, y, sw, rh), Ui.Ink3, Ui.Left);
            }
            Hit(r, "sec:" + key);
            y += rh;
            return open;
        }

        static string Pl(int n, string one, string few, string many)
        {
            int m = n % 10, h = n % 100;
            return n + " " + (m == 1 && h != 11 ? one : m >= 2 && m <= 4 && (h < 12 || h > 14) ? few : many);
        }

        // Рядок «іконка · назва · підпис». Якщо разом не влазять — підпис переходить на другий рядок, а назва не обрізається.
        int RowH(string text, string right, int w)
        {
            int tw = TextRenderer.MeasureText(text, Ui.Body, Size.Empty, TextFormatFlags.NoPadding).Width;
            int rw = right.Length > 0 ? TextRenderer.MeasureText(right, Ui.Small, Size.Empty, TextFormatFlags.NoPadding).Width : 0;
            return right.Length == 0 || tw + rw + Ui.S(46) <= w ? Ui.S(32) : Ui.S(46);
        }

        int Row(Graphics g, string icon, string text, string right, int pad, int y, int w)
        {
            int h = RowH(text, right, w);
            var r = new Rectangle(pad, y, w, h);
            float s = Ui.S(15);
            int tx = r.X + Ui.S(26), tw = w - Ui.S(26);
            if (h == Ui.S(32))
            {
                Icons.Draw(g, icon, new RectangleF(r.X + Ui.S(2), r.Y + (r.Height - s) / 2f, s, s), Ui.Ink2);
                int rw = right.Length > 0 ? TextRenderer.MeasureText(right, Ui.Small).Width : 0;
                if (rw > 0) Ui.Text(g, right, Ui.Small, new Rectangle(r.Right - rw, r.Y, rw, r.Height), Ui.Ink3, Ui.Left);
                Ui.Text(g, text, Ui.Body, new Rectangle(tx, r.Y, tw - rw - Ui.S(8), r.Height), Ui.Ink, Ui.Left);
            }
            else
            {
                Icons.Draw(g, icon, new RectangleF(r.X + Ui.S(2), r.Y + Ui.S(7), s, s), Ui.Ink2);
                Ui.Text(g, text, Ui.Body, new Rectangle(tx, r.Y + Ui.S(4), tw, Ui.S(20)), Ui.Ink, Ui.Left);
                Ui.Text(g, right, Ui.Small, new Rectangle(tx, r.Y + Ui.S(24), tw, Ui.S(17)), Ui.Ink3, Ui.Left);
            }
            return y + h;
        }

        string HitKey(Point p)
        {
            if (closeRect.Contains(p)) return "close";
            foreach (var h in hits)
                if (h.Key.Contains(p) && p.Y < Height) return h.Value;
            return null;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            string k = HitKey(e.Location);
            if (k != hover) { hover = k; Invalidate(); }
            Cursor = k != null ? Cursors.Hand : Cursors.Default;
        }

        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); if (hover != null) { hover = null; Invalidate(); } }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            string k = HitKey(e.Location);
            if (k == null) return;
            if (k == "close") { if (Close != null) Close(); return; }
            if (k.StartsWith("sec:"))
            {
                string key = k.Substring(4);
                if (!Collapsed.Remove(key)) Collapsed.Add(key);
                Settings.Set("PanelCollapsed", string.Join(",", Collapsed));
                if (offset > MaxOffset) offset = MaxOffset;
                Invalidate();
                return;
            }
            if (k == "site") { if (PreviewSite != null) PreviewSite(data.SitePath); return; }
            if (k == "folder") { Shell(data.Folder, false); return; }
            if (k.StartsWith("shot:")) { int i = int.Parse(k.Substring(5)); if (i < data.Shots.Count) Pictures.Open(data.Shots[i]); return; }
            if (k.StartsWith("file:"))
            {
                int i = int.Parse(k.Substring(5));
                if (i >= data.Files.Count) return;
                string f = data.Files[i];
                if (e.Button == MouseButtons.Right)
                {
                    var menu = new List<PopupItem>
                    {
                        new PopupItem("Відкрити", false, () => OpenFile(f)),
                        new PopupItem("Показати в папці", false, () => Shell(f, true)),
                    };
                    if (f.EndsWith(".html", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".htm", StringComparison.OrdinalIgnoreCase))
                        menu.Insert(0, new PopupItem("Переглянути", false, () => { if (PreviewSite != null) PreviewSite(f); }));
                    PopupMenu.ShowAt(this, PointToScreen(e.Location), menu);
                }
                else OpenFile(f);
            }
        }

        void OpenFile(string f)
        {
            if (!File.Exists(f)) { MessageBox.Show(FindForm(), "Файл не знайдено — можливо, його видалили або перемістили:\n" + f, "Open Forge"); return; }
            if ((f.EndsWith(".html", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".htm", StringComparison.OrdinalIgnoreCase)) && PreviewSite != null) { PreviewSite(f); return; }
            try { Process.Start(new ProcessStartInfo(f) { UseShellExecute = true }); }
            catch { Shell(f, true); }   // немає програми для цього типу — показати в папці
        }

        static void Shell(string path, bool select)
        {
            try
            {
                if (select) Process.Start("explorer.exe", "/select,\"" + path + "\"");
                else Process.Start("explorer.exe", "\"" + path + "\"");
            }
            catch { }
        }
    }
}
