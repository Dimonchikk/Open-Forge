// Open Forge — стрічка повідомлень чату.
// Чому окремий елемент, а не купа Label/TextBox:
// • текст ділиться на абзаци, кожен абзац міряється один раз і запам'ятовується — під час відповіді
//   перемірюється лише останній абзац, що змінився;
// • малюються лише абзаци, які зараз видно, тож навіть відповідь на сотні тисяч символів не гальмує;
// • висота всього вмісту відома завжди, тому прокрутка коліщатком і повзунком доходить до кінця,
//   а якщо ти внизу — стрічка сама їде за новим текстом (а якщо прокрутив вгору — не смикає).
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Text;
using System.Windows.Forms;

namespace OpenForge
{
    class ChatMessage
    {
        public string Role;          // "user", "assistant", "error"
        public string Raw = "";
        public string Model;         // хто відповідав
        public string Stats;         // «24.1 ток/с · 3.2 с»
        public bool Streaming;

        internal string Shown;
        internal int LayoutW = -1, Height, BubbleW;
        internal bool LayoutFoot;
        internal readonly List<Para> Paras = new List<Para>();

        public ChatMessage(string role, string raw) { Role = role; Raw = raw ?? ""; }

        public bool IsUser { get { return Role == "user"; } }

        // Текст для показу: без міркувань моделі (<think>…</think>) і зайвих порожніх рядків на початку.
        public string Display
        {
            get
            {
                string s = Raw;
                if (!IsUser && s.IndexOf("<think>", StringComparison.Ordinal) >= 0) s = StripThink(s);
                return s.Replace("\r", "").Trim('\n', ' ');
            }
        }

        static string StripThink(string s)
        {
            var sb = new StringBuilder(s.Length);
            int i = 0;
            while (i < s.Length)
            {
                int a = s.IndexOf("<think>", i, StringComparison.Ordinal);
                if (a < 0) { sb.Append(s, i, s.Length - i); break; }
                sb.Append(s, i, a - i);
                int b = s.IndexOf("</think>", a, StringComparison.Ordinal);
                if (b < 0) break;   // ще думає
                i = b + 8;
            }
            return sb.ToString();
        }
    }

    class Para
    {
        public string Text;
        public int H, W;
    }

    class MessageList : UiControl, IWheel
    {
        public readonly List<ChatMessage> Items = new List<ChatMessage>();
        public string EmptyTitle = "", EmptyText = "";
        int offset, total;
        int dragFrom = -1, dragOffset;
        ChatMessage copiedMsg;
        readonly Timer copiedTimer = new Timer { Interval = 1600 };
        ChatMessage hoverCopy;

        const TextFormatFlags Flow = TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl | TextFormatFlags.NoPrefix |
                                     TextFormatFlags.NoPadding | TextFormatFlags.ExpandTabs;
        const int ChunkChars = 2500;   // дуже довгі абзаци ріжемо на шматки — так їх швидше міряти й малювати

        public MessageList()
        {
            OnBg = true;
            copiedTimer.Tick += (s, e) => { copiedTimer.Stop(); copiedMsg = null; Invalidate(); };
            var menu = new ContextMenuStrip();
            var copy = new ToolStripMenuItem("Копіювати повідомлення");
            menu.Items.Add(copy);
            ChatMessage target = null;
            menu.Opening += (s, e) =>
            {
                target = HitMessage(PointToClient(Cursor.Position));
                if (target == null || target.Display.Length == 0) e.Cancel = true;
            };
            copy.Click += (s, e) => { if (target != null) CopyText(target); };
            ContextMenuStrip = menu;
        }

        // ── розміри ───────────────────────────────────────────────────────────────

        int Gutter { get { return Ui.S(16); } }
        int ColW { get { return Math.Max(Ui.S(200), Math.Min(Ui.S(760), Width - 2 * Gutter - Ui.S(12))); } }
        int ColX { get { return (Width - Ui.S(12) - ColW) / 2 + Ui.S(6); } }
        int PadX { get { return Ui.S(14); } }
        int PadY { get { return Ui.S(9); } }
        int Gap { get { return Ui.S(18); } }
        int FootH { get { return Ui.S(26); } }
        int LineH { get { return TextRenderer.MeasureText("Ag", Ui.Chat, Size.Empty, TextFormatFlags.NoPadding).Height; } }
        int MaxOffset { get { return Math.Max(0, total - Height); } }
        public bool AtBottom { get { return offset >= MaxOffset - Ui.S(8); } }

        bool HasFoot(ChatMessage m) { return m.Role == "assistant" && !m.Streaming && m.Display.Length > 0; }

        int TextW(ChatMessage m)
        {
            return m.IsUser ? (int)(ColW * 0.8f) - 2 * PadX : m.Role == "error" ? ColW - 2 * PadX : ColW;
        }

        void Measure(ChatMessage m)
        {
            int w = TextW(m);
            string disp = m.Display;
            if (disp.Length == 0 && m.Streaming) disp = "Думає…";
            bool foot = HasFoot(m);
            if (disp == m.Shown && w == m.LayoutW && foot == m.LayoutFoot) return;

            var pieces = new List<string>();
            foreach (string line in disp.Split('\n'))
            {
                string t = line;
                while (t.Length > ChunkChars)
                {
                    int cut = t.LastIndexOf(' ', ChunkChars);
                    if (cut < ChunkChars / 2) cut = ChunkChars;
                    pieces.Add(t.Substring(0, cut));
                    t = t.Substring(cut).TrimStart(' ');
                }
                pieces.Add(t);
            }

            var old = new List<Para>(m.Paras);
            bool reuse = w == m.LayoutW;
            m.Paras.Clear();
            int h = 0, maxW = 0, lh = LineH;
            for (int i = 0; i < pieces.Count; i++)
            {
                Para p;
                if (reuse && i < old.Count && old[i].Text == pieces[i]) p = old[i];
                else
                {
                    p = new Para { Text = pieces[i] };
                    if (p.Text.Length == 0) { p.H = lh; p.W = 0; }
                    else
                    {
                        var sz = TextRenderer.MeasureText(p.Text, Ui.Chat, new Size(w, int.MaxValue), Flow);
                        p.H = Math.Max(lh, sz.Height);
                        p.W = Math.Min(w, sz.Width);
                    }
                }
                m.Paras.Add(p);
                h += p.H;
                maxW = Math.Max(maxW, p.W);
            }
            m.Shown = disp;
            m.LayoutW = w;
            m.LayoutFoot = foot;
            bool bubble = m.Role != "assistant";
            m.BubbleW = bubble ? Math.Min(w, maxW) + 2 * PadX + Ui.S(2) : w;
            m.Height = h + (bubble ? 2 * PadY : 0) + (foot ? FootH + Ui.S(4) : 0);
        }

        void Recount()
        {
            int y = Ui.S(20);
            foreach (var m in Items) { Measure(m); y += m.Height + Gap; }
            total = y + Ui.S(4);
        }

        // ── зміни ззовні ─────────────────────────────────────────────────────────

        public void Add(ChatMessage m)
        {
            Items.Add(m);
            Recount();
            ScrollTo(MaxOffset);
        }

        public void Clear()
        {
            Items.Clear();
            Recount();
            ScrollTo(0);
        }

        // Повідомлення змінилося (новий текст або кінець відповіді). Якщо ми були внизу — лишаємося внизу.
        public void Changed(ChatMessage m)
        {
            bool stick = AtBottom;
            Recount();
            if (stick) ScrollTo(MaxOffset); else ScrollTo(offset);
        }

        public void ScrollTo(int y)
        {
            offset = Math.Max(0, Math.Min(MaxOffset, y));
            Invalidate();
        }

        public void ScrollToEnd() { ScrollTo(MaxOffset); }

        protected override void OnResize(EventArgs e)
        {
            bool stick = AtBottom;
            base.OnResize(e);
            Recount();
            ScrollTo(stick ? MaxOffset : offset);
        }

        // ── малювання ───────────────────────────────────────────────────────────

        IEnumerable<KeyValuePair<ChatMessage, Rectangle>> OnScreen()
        {
            int y = Ui.S(20) - offset;
            foreach (var m in Items)
            {
                var r = new Rectangle(m.IsUser ? ColX + ColW - m.BubbleW : ColX, y, m.BubbleW, m.Height);
                if (r.Bottom >= 0 && r.Top <= Height) yield return new KeyValuePair<ChatMessage, Rectangle>(m, r);
                y += m.Height + Gap;
                if (y > Height) yield break;
            }
        }

        Rectangle CopyRect(ChatMessage m, Rectangle r)
        {
            int w = TextRenderer.MeasureText(CopyLabel(m), Ui.Small, Size.Empty, TextFormatFlags.NoPadding).Width + Ui.S(22);
            return new Rectangle(r.X - Ui.S(4), r.Bottom - FootH + Ui.S(4), w + Ui.S(8), FootH - Ui.S(6));
        }

        string CopyLabel(ChatMessage m) { return m == copiedMsg ? "Скопійовано" : "Копіювати"; }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Prep(e);
            if (Items.Count == 0) { PaintEmpty(g); return; }
            foreach (var kv in OnScreen())
            {
                var m = kv.Key;
                var r = kv.Value;
                Color fore = Ui.Ink;
                int tx = r.X, ty = r.Y;
                if (m.IsUser)
                {
                    Ui.FillRound(g, r, Ui.S(18), Ui.Blue);
                    fore = Color.White;
                    tx += PadX; ty += PadY;
                }
                else if (m.Role == "error")
                {
                    Ui.FillRound(g, r, Ui.S(14), Color.FromArgb(Ui.Dark ? 46 : 26, Ui.Red));
                    fore = Ui.Red;
                    tx += PadX; ty += PadY;
                }
                else if (m.Streaming && m.Display.Length == 0) fore = Ui.Ink3;

                int w = m.LayoutW;
                foreach (var p in m.Paras)
                {
                    if (ty + p.H >= 0 && ty <= Height && p.Text.Length > 0)
                        TextRenderer.DrawText(g, p.Text, Ui.Chat, new Rectangle(tx, ty, w, p.H), fore, Flow);
                    ty += p.H;
                    if (ty > Height) break;
                }

                if (HasFoot(m))
                {
                    var cr = CopyRect(m, r);
                    if (m == hoverCopy) Ui.FillRound(g, cr, Ui.S(7), Ui.Fill);
                    float s = Ui.S(13);
                    Icons.Draw(g, "copy", new RectangleF(cr.X + Ui.S(7), cr.Y + (cr.Height - s) / 2f, s, s), Ui.Ink2);
                    Ui.Text(g, CopyLabel(m), Ui.Small, new Rectangle(cr.X + Ui.S(25), cr.Y, cr.Width - Ui.S(25), cr.Height), Ui.Ink2, Ui.Left);
                    string stats = (m.Model ?? "") + (string.IsNullOrEmpty(m.Stats) ? "" : (string.IsNullOrEmpty(m.Model) ? "" : " · ") + m.Stats);
                    if (stats.Length > 0)
                        Ui.Text(g, stats, Ui.Small, new Rectangle(cr.Right + Ui.S(10), cr.Y, r.Right - cr.Right - Ui.S(10), cr.Height), Ui.Ink3, Ui.Left);
                }
            }
            PaintBar(g);
        }

        void PaintEmpty(Graphics g)
        {
            int w = Math.Min(Width - 2 * Gutter, Ui.S(480));
            int x = (Width - w) / 2, y = Height / 2 - Ui.S(70);
            var logo = new RectangleF((Width - Ui.S(52)) / 2f, y - Ui.S(64), Ui.S(52), Ui.S(52));
            using (var b = new SolidBrush(Ui.Mix(Ui.Bg, Ui.Blue, 0.14f))) g.FillEllipse(b, logo);
            Icons.Draw(g, "spark", RectangleF.Inflate(logo, -Ui.S(14), -Ui.S(14)), Ui.Blue);
            Ui.Text(g, EmptyTitle, Ui.H1, new Rectangle(x, y, w, Ui.S(34)), Ui.Ink, Ui.Center);
            TextRenderer.DrawText(g, EmptyText, Ui.Body, new Rectangle(x, y + Ui.S(42), w, Ui.S(80)), Ui.Ink2,
                TextFormatFlags.WordBreak | TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPrefix);
        }

        Rectangle Track { get { return new Rectangle(Width - Ui.S(10), Ui.S(4), Ui.S(6), Height - Ui.S(8)); } }

        Rectangle Thumb
        {
            get
            {
                var t = Track;
                if (total <= Height) return Rectangle.Empty;
                int h = Math.Max(Ui.S(30), (int)((long)t.Height * Height / total));
                int y = t.Y + (int)((long)(t.Height - h) * offset / Math.Max(1, MaxOffset));
                return new Rectangle(t.X, y, t.Width, h);
            }
        }

        void PaintBar(Graphics g)
        {
            var th = Thumb;
            if (th.IsEmpty) return;
            Ui.FillRound(g, new RectangleF(th.X + Ui.S(1), th.Y, th.Width - Ui.S(2), th.Height), (th.Width - Ui.S(2)) / 2f,
                Ui.Mix(Ui.Bg, Ui.Ink, dragFrom >= 0 ? 0.4f : 0.22f));
        }

        // ── миша ──────────────────────────────────────────────────────────────────

        ChatMessage HitMessage(Point p)
        {
            foreach (var kv in OnScreen())
                if (p.Y >= kv.Value.Top && p.Y <= kv.Value.Bottom) return kv.Key;
            return null;
        }

        ChatMessage HitCopy(Point p)
        {
            foreach (var kv in OnScreen())
                if (HasFoot(kv.Key) && CopyRect(kv.Key, kv.Value).Contains(p)) return kv.Key;
            return null;
        }

        void CopyText(ChatMessage m)
        {
            try { Clipboard.SetText(m.Display); } catch { return; }
            copiedMsg = m;
            copiedTimer.Stop();
            copiedTimer.Start();
            Invalidate();
        }

        public void Wheel(int delta) { ScrollTo(offset - delta * Ui.S(80) / 120); }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            Wheel(e.Delta);
            var h = e as HandledMouseEventArgs;
            if (h != null) h.Handled = true;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();   // щоб коліщатко й клавіші прокрутки йшли сюди
            if (e.Button != MouseButtons.Left) return;
            var th = Thumb;
            var track = Track;
            track.Inflate(Ui.S(4), 0);
            if (!th.IsEmpty && track.Contains(e.Location))
            {
                if (!th.Contains(e.Location)) ScrollTo(offset + (e.Y < th.Y ? -Height : Height) * 9 / 10);
                dragFrom = e.Y;
                dragOffset = offset;
                Capture = true;
                Invalidate();
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (dragFrom >= 0)
            {
                var t = Track;
                var th = Thumb;
                int free = Math.Max(1, t.Height - th.Height);
                ScrollTo(dragOffset + (int)((long)(e.Y - dragFrom) * MaxOffset / free));
                return;
            }
            var hc = HitCopy(e.Location);
            if (hc != hoverCopy) { hoverCopy = hc; Invalidate(); }
            Cursor = hc != null ? Cursors.Hand : Cursors.Default;
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (dragFrom >= 0) { dragFrom = -1; Capture = false; Invalidate(); return; }
            if (e.Button == MouseButtons.Left)
            {
                var hc = HitCopy(e.Location);
                if (hc != null) CopyText(hc);
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (hoverCopy != null) { hoverCopy = null; Invalidate(); }
        }

        protected override bool IsInputKey(Keys k)
        {
            return k == Keys.Up || k == Keys.Down || k == Keys.PageUp || k == Keys.PageDown || k == Keys.Home || k == Keys.End || base.IsInputKey(k);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            switch (e.KeyCode)
            {
                case Keys.Up: ScrollTo(offset - Ui.S(40)); break;
                case Keys.Down: ScrollTo(offset + Ui.S(40)); break;
                case Keys.PageUp: ScrollTo(offset - Height * 9 / 10); break;
                case Keys.PageDown: ScrollTo(offset + Height * 9 / 10); break;
                case Keys.Home: if (e.Control) ScrollTo(0); break;
                case Keys.End: if (e.Control) ScrollToEnd(); break;
            }
        }
    }
}
