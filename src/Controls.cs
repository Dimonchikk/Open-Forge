// Open Forge — власні елементи інтерфейсу (малюються самостійно, з подвійною буферизацією, як у «Тихому ПК»).
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace OpenForge
{
    // Елемент, який прокручується коліщатком миші (його знаходить WheelRouter).
    interface IWheel
    {
        void Wheel(int delta);
    }

    // Коліщатко завжди прокручує те, над чим курсор, а не те, що має фокус клавіатури
    // (інакше, поки курсор у полі вводу, стрічка чату не прокручувалась би).
    class WheelRouter : IMessageFilter
    {
        readonly Form form;
        public WheelRouter(Form f) { form = f; }

        public bool PreFilterMessage(ref Message m)
        {
            if (m.Msg != 0x020A) return false;   // WM_MOUSEWHEEL
            Control c = form;
            var p = form.PointToClient(Cursor.Position);
            if (!form.ClientRectangle.Contains(p)) return false;
            while (true)
            {
                var k = c.GetChildAtPoint(p, GetChildAtPointSkip.Invisible);
                if (k == null) break;
                p = new Point(p.X - k.Left, p.Y - k.Top);
                c = k;
            }
            // Багаторядкове поле з власною прокруткою прокручується само.
            var tb = c as TextBox;
            if (tb != null && tb.Multiline && tb.ScrollBars != ScrollBars.None) return false;
            for (; c != null; c = c.Parent)
            {
                var w = c as IWheel;
                if (w != null)
                {
                    long wp = m.WParam.ToInt64();
                    w.Wheel((short)((wp >> 16) & 0xFFFF));
                    return true;
                }
            }
            return false;
        }
    }

    class UiControl : Control
    {
        public bool OnBg;
        protected Color Back { get { return OnBg ? Ui.Bg : Ui.Card; } }

        public UiControl()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            Font = Ui.Body;
        }

        protected Graphics Prep(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Back);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            return g;
        }

        protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); Invalidate(); }
        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }
    }

    // Сегментований перемикач (як у macOS) з анімованим «бігунком».
    class Segmented : UiControl
    {
        string[] items = new string[0];
        public Func<int, Color> Accent;
        public bool FitText;   // ширина сегментів за довжиною тексту (для головних вкладок)
        int selected = -1, hover = -1;
        float thumb = -1;
        readonly Timer anim = new Timer();
        public event Action<int> ItemClicked;

        public Segmented()
        {
            Cursor = Cursors.Hand;
            anim.Interval = 15;
            anim.Tick += (s, e) =>
            {
                float d = selected - thumb;
                if (Math.Abs(d) < 0.02f) { thumb = selected; anim.Stop(); }
                else thumb += d * 0.3f;
                Invalidate();
            };
        }

        public string[] Items
        {
            get { return items; }
            set { items = value ?? new string[0]; Invalidate(); }
        }

        public int Selected
        {
            get { return selected; }
            set
            {
                if (value == selected) return;
                selected = value;
                if (thumb >= 0 && selected >= 0 && Visible && IsHandleCreated) anim.Start();
                else { thumb = selected; anim.Stop(); Invalidate(); }
            }
        }

        float Pad { get { return Ui.S(2); } }

        // Межі сегментів: рівні або пропорційні до тексту.
        float[] Edges()
        {
            int n = Math.Max(1, items.Length);
            var e = new float[n + 1];
            float total = Width - 2 * Pad;
            e[0] = Pad;
            if (!FitText || items.Length == 0)
            {
                for (int i = 1; i <= n; i++) e[i] = Pad + total * i / n;
                return e;
            }
            var w = new float[n];
            float sum = 0;
            for (int i = 0; i < n; i++)
            {
                w[i] = TextRenderer.MeasureText(items[i], Ui.BodySemi, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width;
                sum += w[i];
            }
            float extra = (total - sum) / n;
            for (int i = 0; i < n; i++) e[i + 1] = e[i] + w[i] + extra;
            e[n] = Pad + total;
            return e;
        }

        RectangleF Seg(float i)
        {
            var e = Edges();
            int n = e.Length - 1;
            int a = Math.Max(0, Math.Min(n - 1, (int)Math.Floor(i)));
            int b = Math.Min(n - 1, a + 1);
            float t = Math.Max(0, Math.Min(1, i - a));
            float x = e[a] + (e[b] - e[a]) * t;
            float w = (e[a + 1] - e[a]) + ((e[b + 1] - e[b]) - (e[a + 1] - e[a])) * t;
            return new RectangleF(x, Pad, w, Height - 2 * Pad);
        }

        int HitTest(Point p)
        {
            if (items.Length == 0) return -1;
            var e = Edges();
            for (int i = 0; i < items.Length; i++)
                if (p.X >= e[i] && p.X < e[i + 1]) return i;
            return -1;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Prep(e);
            float rad = Ui.S(9);
            Ui.FillRound(g, new RectangleF(0, 0, Width - 1, Height - 1), rad, Enabled ? Ui.Fill : Ui.SegDisabled);
            if (Enabled && hover >= 0 && hover != selected)
                Ui.FillRound(g, Seg(hover), rad - Pad, Ui.FillHover);
            using (var pen = new Pen(Ui.SegSep, Math.Max(1f, Ui.Scale)))
            {
                for (int i = 1; i < items.Length; i++)
                {
                    if (thumb >= 0 && i > thumb - 1 && i < thumb + 2) continue;
                    if (hover >= 0 && (i == hover || i == hover + 1)) continue;
                    var r = Seg(i);
                    g.DrawLine(pen, r.X, r.Y + r.Height * 0.28f, r.X, r.Bottom - r.Height * 0.28f);
                }
            }
            if (thumb >= 0 && items.Length > 0)
            {
                var t = Seg(thumb);
                Ui.FillRound(g, new RectangleF(t.X, t.Y + Ui.S(1), t.Width, t.Height), rad - Pad,
                    Color.FromArgb(Enabled ? Ui.ShadowAlpha : Ui.ShadowAlpha / 3, 0, 0, 0));
                Ui.FillRound(g, t, rad - Pad, Ui.Thumb);
            }
            for (int i = 0; i < items.Length; i++)
            {
                var r = Rectangle.Round(Seg(i));
                r.Inflate(-Ui.S(3), 0);
                bool sel = i == selected;
                Color c = Enabled ? (sel ? (Accent != null ? Accent(i) : Ui.Ink) : Ui.SegText) : Ui.Ink3;
                // У вузькому вікні сегменти вужчі — тоді текст дрібнішим шрифтом, щоб не обрізався.
                var f = sel ? Ui.BodySemi : Ui.Body;
                if (TextRenderer.MeasureText(items[i], Ui.BodySemi, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width > r.Width - Ui.S(8)) f = Ui.Small;
                Ui.Text(g, items[i], f, r, c, Ui.Center);
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int h = HitTest(e.Location);
            if (h != hover) { hover = h; Invalidate(); }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            hover = -1;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (!(Enabled && e.Button == MouseButtons.Left)) return;
            int i = HitTest(e.Location);
            if (i >= 0 && ItemClicked != null) ItemClicked(i);
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            base.OnEnabledChanged(e);
            Cursor = Enabled ? Cursors.Hand : Cursors.Default;
        }
    }

    // Кнопка-«пігулка»: сіра, синя (Primary) або з власним кольором; може мати значок ліворуч.
    class PillBtn : UiControl
    {
        bool hover;
        public bool Primary;
        public string Icon;                     // ключ з Icons або null
        public Func<Color> FillColor;           // власний колір заливки
        public Func<Color> Fore;                // власний колір тексту
        public Func<Color> Bg;                  // колір довкола (якщо кнопка не на картці й не на фоні)
        public PillBtn() { Cursor = Cursors.Hand; Font = Ui.BodySemi; }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Cursor = Enabled ? Cursors.Hand : Cursors.Default; }

        // Ширина під текст і значок.
        public int Natural()
        {
            int w = TextRenderer.MeasureText(Text, Font, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width + Ui.S(32);
            if (Icon != null) w += Ui.S(22);
            return w;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Bg != null ? Bg() : Back);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Color fill = FillColor != null ? FillColor() : Primary ? Ui.Blue : Ui.Fill;
            if (!Enabled) fill = Ui.Mix(fill, Bg != null ? Bg() : Back, 0.5f);
            else if (hover) fill = FillColor != null || Primary ? Ui.Mix(fill, Color.Black, 0.12f) : Ui.FillHover;
            Ui.FillRound(g, new RectangleF(0, 0, Width - 1, Height - 1), (Height - 1) / 2f, fill);
            Color fore = Fore != null ? Fore() : Primary ? Color.White : Ui.Ink;
            if (!Enabled) fore = Ui.Mix(fore, fill, 0.45f);
            var r = ClientRectangle;
            if (Icon != null)
            {
                int tw = TextRenderer.MeasureText(Text, Font, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width;
                float s = Ui.S(15), total = s + Ui.S(7) + tw;
                float x = Math.Max(Ui.S(10), (Width - total) / 2f);
                Icons.Draw(g, Icon, new RectangleF(x, (Height - s) / 2f, s, s), fore);
                r = new Rectangle((int)(x + s + Ui.S(7)), 0, Width - (int)(x + s + Ui.S(7)), Height);
                Ui.Text(g, Text, Font, r, fore, Ui.Left);
            }
            else Ui.Text(g, Text, Font, r, fore, Ui.Center);
        }
    }

    // Кругла кнопка зі значком. Розмір завжди квадратний — тож це рівне коло, а не овал,
    // хоч би який розмір їй задали (береться менша сторона).
    class RoundBtn : UiControl
    {
        bool hover;
        public string Icon = "plus";
        public bool Primary, Outline;
        public Func<Color> Bg;
        readonly ToolTip tip = new ToolTip();
        public RoundBtn() { Cursor = Cursors.Hand; }

        public string Tip { set { tip.SetToolTip(this, value); } }

        protected override void SetBoundsCore(int x, int y, int width, int height, BoundsSpecified specified)
        {
            int d = Math.Min(width, height);
            base.SetBoundsCore(x + (width - d) / 2, y + (height - d) / 2, d, d, specified);
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Cursor = Enabled ? Cursors.Hand : Cursors.Default; }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Color back = Bg != null ? Bg() : Back;
            g.Clear(back);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            Color fill, fore;
            if (Primary)
            {
                fill = Enabled ? (hover ? Ui.Mix(Ui.Blue, Color.Black, 0.12f) : Ui.Blue) : Ui.Mix(Ui.Ink3, back, 0.45f);
                fore = Color.White;
            }
            else
            {
                fill = hover && Enabled ? Ui.FillHover : Outline ? back : Ui.Fill;
                fore = Enabled ? Ui.Ink2 : Ui.Ink3;
            }
            using (var b = new SolidBrush(fill)) g.FillEllipse(b, r);
            if (Outline && !Primary)
                using (var pen = new Pen(Ui.Separator, Math.Max(1f, Ui.Scale))) g.DrawEllipse(pen, r);
            float s = Width * 0.44f;
            Icons.Draw(g, Icon, new RectangleF((Width - s) / 2f, (Height - s) / 2f, s, s), fore);
        }
    }

    // Тонка смуга прокрутки.
    class ThinBar : UiControl
    {
        public float Pos, Frac = 1;
        public ThinBar() { OnBg = true; }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Prep(e);
            if (Frac >= 1) return;
            float h = Math.Max(Ui.S(30), Height * Frac);
            float y = (Height - h) * Pos;
            Ui.FillRound(g, new RectangleF(Ui.S(1), y, Width - Ui.S(2), h), (Width - Ui.S(2)) / 2f, Ui.Mix(Ui.Bg, Ui.Ink, 0.22f));
        }
    }

    // Сторінка з картками й підписами розділів.
    class Page : UiControl
    {
        public readonly List<Rectangle> Cards = new List<Rectangle>();
        public readonly Dictionary<string, KeyValuePair<Rectangle, string>> Captions = new Dictionary<string, KeyValuePair<Rectangle, string>>();

        public Page() { OnBg = true; }

        public void SetCaption(string id, Rectangle r, string text)
        {
            Captions[id] = new KeyValuePair<Rectangle, string>(r, text);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Prep(e);
            foreach (var r in Cards)
            {
                Ui.FillRound(g, r, Ui.S(14), Ui.CardLine);
                Ui.FillRound(g, RectangleF.Inflate(r, -Math.Max(1f, Ui.Scale), -Math.Max(1f, Ui.Scale)), Ui.S(14) - 1, Ui.Card);
            }
            foreach (var c in Captions.Values)
            {
                string text = c.Value, sub = null;
                int bar = text.IndexOf('|');
                if (bar >= 0) { sub = text.Substring(bar + 1); text = text.Substring(0, bar); }
                Ui.Text(g, text, Ui.Section, c.Key, Ui.Ink, Ui.Left);
                if (sub != null)
                {
                    int w = TextRenderer.MeasureText(g, text, Ui.Section, Size.Empty, TextFormatFlags.NoPadding).Width + Ui.S(8);
                    Ui.Text(g, sub, Ui.Small, new Rectangle(c.Key.X + w, c.Key.Y + Ui.S(1), c.Key.Width - w, c.Key.Height), Ui.Ink2, Ui.Left);
                }
            }
        }
    }

    // Прокручувана сторінка: висота вмісту рахується за найнижчим елементом, тож прокрутка завжди дістає до кінця.
    class ScrollHost : UiControl, IWheel
    {
        public readonly Page Content = new Page();
        readonly ThinBar bar = new ThinBar();
        int offset;

        public ScrollHost()
        {
            OnBg = true;
            Controls.Add(Content);
            Controls.Add(bar);
            bar.BringToFront();
        }

        // Розставити елементи сторінки (за поточною шириною) — викликається при зміні розміру й через Rearrange().
        public event Action Arrange;

        public void Rearrange()
        {
            if (Arrange != null) Arrange();
            Content.Invalidate();
            Fit();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            Content.Width = Width;
            bar.Bounds = new Rectangle(Width - Ui.S(10), Ui.S(4), Ui.S(6), Math.Max(0, Height - Ui.S(8)));
            Rearrange();
        }

        public void Fit()
        {
            int bottom = 0;
            foreach (Control c in Content.Controls) if (c.Visible || !IsHandleCreated) bottom = Math.Max(bottom, c.Bottom);
            foreach (var r in Content.Cards) bottom = Math.Max(bottom, r.Bottom);
            foreach (var c in Content.Captions.Values) bottom = Math.Max(bottom, c.Key.Bottom);
            bottom += Ui.S(20);
            Content.Height = Math.Max(Height, bottom);
            ScrollTo(offset);
        }

        public void ScrollTo(int y)
        {
            offset = Math.Max(0, Math.Min(Content.Height - Height, y));
            Content.Top = -offset;
            int range = Content.Height - Height;
            bar.Frac = range <= 0 ? 1 : (float)Height / Content.Height;
            bar.Pos = range <= 0 ? 0 : (float)offset / range;
            bar.Invalidate();
        }

        public void Wheel(int delta) { ScrollTo(offset - delta * Ui.S(70) / 120); }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            Wheel(e.Delta);
            var h = e as HandledMouseEventArgs;
            if (h != null) h.Handled = true;
        }
    }

    // Стан у шапці: кольорова крапка й напис на блідому тлі.
    class StatusPill : UiControl
    {
        Color color = Color.Green;
        public StatusPill() { OnBg = true; }

        public event Action Resized;

        public void Set(string text, Color c)
        {
            if (Text == text && color == c) return;
            bool longer = text != Text;
            color = c;
            Text = text;
            Invalidate();
            if (longer && Resized != null) Resized();
        }

        // Ширина під напис з полями — шапка дає пігулці саме стільки місця.
        public int Natural()
        {
            if (string.IsNullOrEmpty(Text)) return 0;
            return TextRenderer.MeasureText(Text, Ui.Small, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width + Ui.S(42);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Prep(e);
            if (string.IsNullOrEmpty(Text)) return;
            int h = Ui.S(26);
            int w = Math.Min(Width - 1, Natural());
            var r = new RectangleF(Width - 1 - w, (Height - h) / 2f, w, h);
            Ui.FillRound(g, r, h / 2f, Color.FromArgb(Ui.Dark ? 46 : 30, color));
            float d = Ui.S(6);
            using (var b = new SolidBrush(color)) g.FillEllipse(b, r.X + Ui.S(13), r.Y + (h - d) / 2f, d, d);
            Ui.Text(g, Text, Ui.Small, new Rectangle((int)r.X + Ui.S(25), (int)r.Y, w - Ui.S(25) - Ui.S(12), h), color, Ui.Left);
        }
    }

    // Перемикач теми: як у Windows / світла / темна.
    class ThemeSwitch : UiControl
    {
        static readonly string[] Keys = { "half", "sun", "moon" };
        static readonly string[] Tips = { "Як у Windows", "Світла тема", "Темна тема" };
        int selected, hover = -1;
        readonly ToolTip tip = new ToolTip();
        public event Action<int> Changed;

        public ThemeSwitch() { OnBg = true; Cursor = Cursors.Hand; }

        public int Selected { get { return selected; } set { selected = value; Invalidate(); } }

        RectangleF Seg(int i)
        {
            float pad = Ui.S(3), w = (Width - 2 * pad) / 3f;
            return new RectangleF(pad + i * w, pad, w, Height - 2 * pad);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Prep(e);
            Ui.FillRound(g, new RectangleF(0, 0, Width - 1, Height - 1), Height / 2f, Ui.Fill);
            for (int i = 0; i < 3; i++)
            {
                var r = Seg(i);
                if (i == selected)
                {
                    Ui.FillRound(g, new RectangleF(r.X, r.Y + Ui.S(1), r.Width, r.Height), r.Height / 2f, Color.FromArgb(Ui.ShadowAlpha, 0, 0, 0));
                    Ui.FillRound(g, r, r.Height / 2f, Ui.Thumb);
                }
                else if (i == hover) Ui.FillRound(g, r, r.Height / 2f, Ui.FillHover);
                float s = Ui.S(15);
                Icons.Draw(g, Keys[i], new RectangleF(r.X + (r.Width - s) / 2f, r.Y + (r.Height - s) / 2f, s, s), i == selected ? Ui.Ink : Ui.Ink2);
            }
        }

        int Hit(Point p) { for (int i = 0; i < 3; i++) if (Seg(i).Contains(p)) return i; return -1; }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int h = Hit(e.Location);
            if (h != hover) { hover = h; tip.SetToolTip(this, h >= 0 ? Tips[h] : ""); Invalidate(); }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e) { hover = -1; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            int h = Hit(e.Location);
            if (h < 0 || e.Button != MouseButtons.Left) return;
            selected = h;
            Invalidate();
            if (Changed != null) Changed(h);
        }
    }

    // Звичайне поле Windows (свій курсор, виділення, меню на правий клік), але:
    // • підказка малюється поверх порожнього поля самим полем — курсор не ховається під нею;
    // • Ctrl+A/C/V/X/Z/Y обробляються за кодом клавіші (KeyCode), а не за буквою, тож працюють на будь-якій розкладці;
    // • Ctrl+Backspace стирає слово; вставка — лише текст, з правильними переносами рядків.
    class HintTextBox : TextBox
    {
        string hint = "";
        public bool EnterSubmits;
        public event Action Submit;

        [DllImport("user32.dll")] static extern bool HideCaret(IntPtr hWnd);
        [DllImport("user32.dll")] static extern bool ShowCaret(IntPtr hWnd);

        public HintTextBox()
        {
            BorderStyle = BorderStyle.None;
            MaxLength = 0;   // без обмеження 32 767 символів
            ShortcutsEnabled = true;
        }

        public string Hint { get { return hint; } set { hint = value ?? ""; Invalidate(); } }

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (m.Msg == 0x000F && TextLength == 0 && hint.Length > 0) DrawHint();   // WM_PAINT
        }

        void DrawHint()
        {
            bool hidden = false;
            try { hidden = HideCaret(Handle); } catch { }
            try
            {
                using (var g = CreateGraphics())
                {
                    var flags = TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding | TextFormatFlags.TextBoxControl |
                                (Multiline ? TextFormatFlags.WordBreak : TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                    var r = new Rectangle(Ui.S(1) + 1, Multiline ? 1 : 0, ClientSize.Width - Ui.S(2), ClientSize.Height);
                    TextRenderer.DrawText(g, hint, Font, r, Ui.Ink3, flags);
                }
            }
            catch { }
            if (hidden) try { ShowCaret(Handle); } catch { }
        }

        protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); if (TextLength <= 1) Invalidate(); }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (EnterSubmits && e.KeyCode == Keys.Enter && !e.Shift && !e.Control && !e.Alt)
            {
                e.Handled = e.SuppressKeyPress = true;
                if (Submit != null) Submit();
                return;
            }
            if (e.Control && !e.Alt)
            {
                bool done = true;
                switch (e.KeyCode)
                {
                    case Keys.A: SelectAll(); break;
                    case Keys.C: if (SelectionLength > 0) Copy(); break;
                    case Keys.X: if (SelectionLength > 0 && !ReadOnly) Cut(); break;
                    case Keys.V: if (!ReadOnly) PastePlain(); break;
                    case Keys.Z: if (!ReadOnly && CanUndo) Undo(); break;
                    case Keys.Y: if (!ReadOnly && CanUndo) Undo(); break;   // у полі Windows один крок назад: повторне «назад» = «вперед»
                    case Keys.Back: if (!ReadOnly) DeleteWord(); break;
                    default: done = false; break;
                }
                if (done) { e.Handled = e.SuppressKeyPress = true; return; }
            }
            base.OnKeyDown(e);
        }

        // Ctrl+Shift+V і Shift+Insert теж сюди: усе вставляємо як простий текст.
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.Shift | Keys.Insert) || keyData == (Keys.Control | Keys.Shift | Keys.V))
            {
                if (!ReadOnly) PastePlain();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        // Вставлено картинку (скріншот) або файли-картинки — їх забирає власник поля, а не текст.
        public event Action<System.Drawing.Image> ImagePasted;
        public event Action<string[]> FilesPasted;

        public void PastePlain()
        {
            try
            {
                if (ImagePasted != null && Clipboard.ContainsImage())
                {
                    using (var img = Clipboard.GetImage()) if (img != null) { ImagePasted(img); return; }
                }
                if (FilesPasted != null && Clipboard.ContainsFileDropList())
                {
                    var files = new System.Collections.Generic.List<string>();
                    foreach (string f in Clipboard.GetFileDropList()) if (Pictures.IsImageFile(f)) files.Add(f);
                    if (files.Count > 0) { FilesPasted(files.ToArray()); return; }
                }
            }
            catch { }
            string t;
            try { t = Clipboard.ContainsText() ? Clipboard.GetText() : null; } catch { t = null; }
            if (t == null) return;
            t = t.Replace("\r\n", "\n").Replace('\r', '\n');
            t = Multiline ? t.Replace("\n", "\r\n") : t.Replace('\n', ' ');
            Paste(t);
        }

        void DeleteWord()
        {
            if (SelectionLength > 0) { SelectedText = ""; return; }
            int end = SelectionStart, i = end;
            string s = Text;
            while (i > 0 && char.IsWhiteSpace(s[i - 1])) i--;
            while (i > 0 && !char.IsWhiteSpace(s[i - 1])) i--;
            if (i == end) return;
            SelectionStart = i;
            SelectionLength = end - i;
            SelectedText = "";
        }
    }

    // Поле вводу в заокругленій рамці. Багаторядкове поле росте разом з текстом до MaxLines рядків,
    // далі — з'являється прокрутка. Кнопки (RoundBtn) додаються в Controls і розставляються через LeftInset/RightInset.
    class InputBox : UiControl
    {
        public readonly HintTextBox Box = new HintTextBox();
        public int LeftInset, RightInset;
        public int MinLines = 1, MaxLines = 8;
        public Func<Color> Around = () => Ui.Bg;
        public event Action WantHeight;
        int wanted;

        public InputBox()
        {
            Controls.Add(Box);
            Box.Font = Ui.Chat;
            Box.TextChanged += (s, e) => Recalc();
            Box.GotFocus += (s, e) => Invalidate();
            Box.LostFocus += (s, e) => Invalidate();
            Retheme();
            Ui.Rethemers.Add(Retheme);
            Cursor = Cursors.IBeam;
        }

        void Retheme() { Box.BackColor = Ui.Card; Box.ForeColor = Ui.Ink; Invalidate(); }

        public bool Multiline
        {
            get { return Box.Multiline; }
            set { Box.Multiline = value; Box.AcceptsReturn = value; Box.WordWrap = true; Recalc(); }
        }

        int LineH { get { return TextRenderer.MeasureText("Ag", Box.Font, Size.Empty, TextFormatFlags.NoPadding).Height; } }
        int PadY { get { return Ui.S(11); } }

        // Висота, якої хоче поле при поточному тексті.
        public int Wanted { get { if (wanted == 0) Recalc(); return wanted; } }

        void Recalc()
        {
            int lines = MinLines;
            bool over = false;   // текст довший за MaxLines — тоді й лише тоді потрібна смуга прокрутки
            if (Box.Multiline && Box.TextLength > 0)
            {
                int w = Math.Max(Ui.S(40), Box.ClientSize.Width - Ui.S(4));
                // Великий вставлений текст не міряємо: він точно довший за MaxLines.
                if (Box.TextLength > 4000) { lines = MaxLines; over = true; }
                else
                {
                    int h = TextRenderer.MeasureText(Box.Text + "\u200b", Box.Font, new Size(w, int.MaxValue),
                        TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding).Height;
                    int need = (h + LineH / 2) / LineH;
                    lines = Math.Max(MinLines, Math.Min(MaxLines, need));
                    over = need > MaxLines;
                }
            }
            // Перевіряємо щоразу, зокрема й для порожнього поля: після відправки довгого тексту смуга має зникнути.
            var sb = Box.Multiline && over ? ScrollBars.Vertical : ScrollBars.None;
            if (Box.ScrollBars != sb) Box.ScrollBars = sb;
            int want = lines * LineH + 2 * PadY;
            if (want != wanted)
            {
                wanted = want;
                if (WantHeight != null) WantHeight();
            }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            int h = Box.Multiline ? Height - 2 * PadY : LineH + Ui.S(2);
            int x = Ui.S(16) + LeftInset;
            Box.Bounds = new Rectangle(x, Box.Multiline ? PadY : (Height - h) / 2, Math.Max(Ui.S(20), Width - x - Ui.S(12) - RightInset), Math.Max(LineH, h));
            Recalc();
        }

        public void Relayout() { OnResize(EventArgs.Empty); }

        protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); Box.Focus(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Around());
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float rad = Math.Min(Ui.S(22), (Height - 1) / 2f);
            Color line = Box.Focused ? Ui.Mix(Ui.CardLine, Ui.Blue, 0.55f) : Ui.CardLine;
            Ui.FillRound(g, new RectangleF(0, 0, Width - 1, Height - 1), rad, line);
            float k = Math.Max(1f, Ui.Scale);
            Ui.FillRound(g, new RectangleF(k, k, Width - 1 - 2 * k, Height - 1 - 2 * k), rad - k, Ui.Card);
        }
    }
}
