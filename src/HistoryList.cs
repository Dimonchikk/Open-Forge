// Open Forge — панель історії чатів у розділі «Чат»: список (найновіші зверху), вибраний підсвічений,
// правий клік — «Перейменувати» / «Видалити».
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace OpenForge
{
    class HistoryList : UiControl, IWheel
    {
        List<ChatEntry> items = new List<ChatEntry>();
        public string Selected;
        public string EmptyText;   // що писати, коли список порожній (null — підказка для першого запуску)
        int offset, hover = -1;
        public event Action<string> Open, Rename, Delete;

        public HistoryList()
        {
            var menu = new ContextMenuStrip();
            var ren = new ToolStripMenuItem("Перейменувати");
            var del = new ToolStripMenuItem("Видалити");
            menu.Items.AddRange(new ToolStripItem[] { ren, del });
            ChatEntry target = null;
            menu.Opening += (s, e) =>
            {
                int i = Hit(PointToClient(Cursor.Position));
                target = i >= 0 ? items[i] : null;
                if (target == null) e.Cancel = true;
            };
            ren.Click += (s, e) => { if (target != null && Rename != null) Rename(target.Id); };
            del.Click += (s, e) => { if (target != null && Delete != null) Delete(target.Id); };
            ContextMenuStrip = menu;
        }

        protected new Color Back { get { return Ui.Side; } }

        public void SetItems(List<ChatEntry> list)
        {
            items = list;
            ScrollTo(offset);
        }

        int RowH { get { return Ui.S(50); } }
        int Top0 { get { return Ui.S(4); } }
        int Total { get { return Top0 + items.Count * RowH + Ui.S(8); } }

        void ScrollTo(int y)
        {
            offset = Math.Max(0, Math.Min(Math.Max(0, Total - Height), y));
            Invalidate();
        }

        public void Wheel(int delta) { ScrollTo(offset - delta * Ui.S(60) / 120); }

        protected override void OnResize(EventArgs e) { base.OnResize(e); ScrollTo(offset); }

        int Hit(Point p)
        {
            int i = (p.Y + offset - Top0) / RowH;
            return p.Y + offset >= Top0 && i >= 0 && i < items.Count && p.X >= 0 && p.X < Width ? i : -1;
        }

        static string When(DateTime t)
        {
            var today = DateTime.Today;
            if (t.Date == today) return "Сьогодні, " + t.ToString("HH:mm");
            if (t.Date == today.AddDays(-1)) return "Учора, " + t.ToString("HH:mm");
            return t.ToString(t.Year == today.Year ? "d MMMM" : "d MMMM yyyy", new System.Globalization.CultureInfo("uk-UA"));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Ui.Side);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            if (items.Count == 0)
            {
                TextRenderer.DrawText(g, EmptyText ?? "Тут з'являтимуться твої чати — кожна розмова зберігається сама.", Ui.Small,
                    new Rectangle(Ui.S(14), Ui.S(8), Width - Ui.S(28), Ui.S(60)), Ui.Ink3, TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
                return;
            }
            for (int i = 0; i < items.Count; i++)
            {
                int y = Top0 + i * RowH - offset;
                if (y + RowH < 0) continue;
                if (y > Height) break;
                var r = new Rectangle(Ui.S(8), y + Ui.S(2), Width - Ui.S(16), RowH - Ui.S(4));
                bool sel = items[i].Id == Selected;
                if (sel) Ui.FillRound(g, r, Ui.S(9), Ui.RowSel);
                else if (i == hover) Ui.FillRound(g, r, Ui.S(9), Ui.Mix(Ui.Side, Ui.Ink, 0.06f));
                Ui.Text(g, items[i].Title, sel ? Ui.BodySemi : Ui.Body, new Rectangle(r.X + Ui.S(10), r.Y + Ui.S(5), r.Width - Ui.S(18), Ui.S(20)), sel ? Ui.Blue : Ui.Ink, Ui.Left);
                Ui.Text(g, When(items[i].Updated), Ui.Caps, new Rectangle(r.X + Ui.S(10), r.Y + Ui.S(25), r.Width - Ui.S(18), Ui.S(16)), Ui.Ink3, Ui.Left);
            }
            if (Total > Height)
            {
                int th = Math.Max(Ui.S(24), Height * Height / Total);
                int ty = (Height - th) * offset / Math.Max(1, Total - Height);
                Ui.FillRound(g, new RectangleF(Width - Ui.S(6), ty, Ui.S(4), th), Ui.S(2), Ui.Mix(Ui.Side, Ui.Ink, 0.2f));
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int h = Hit(e.Location);
            if (h != hover) { hover = h; Invalidate(); }
            Cursor = h >= 0 ? Cursors.Hand : Cursors.Default;
        }

        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hover = -1; Invalidate(); }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left) return;
            int i = Hit(e.Location);
            if (i >= 0 && Open != null) Open(items[i].Id);
        }
    }

    // Маленьке вікно з одним полем (перейменування чату).
    class TextPrompt : Form
    {
        readonly InputBox box = new InputBox();
        public string Value { get { return box.Box.Text.Trim(); } }

        public TextPrompt(string title, string label, string value)
        {
            Text = title;
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(Ui.S(440), Ui.S(170));
            BackColor = Ui.Bg;
            int x = Ui.S(22), w = ClientSize.Width - 2 * x;
            Ui.MakeLabel(this, label, new Rectangle(x, Ui.S(18), w, Ui.S(22)), Ui.BodySemi, Ui.CInk, Ui.CBg);
            box.Around = () => Ui.Bg;
            box.Box.Text = value ?? "";
            box.Box.EnterSubmits = true;
            box.Box.Submit += () => { if (Value.Length > 0) { DialogResult = DialogResult.OK; Close(); } };
            box.Bounds = new Rectangle(x, Ui.S(48), w, Ui.S(40));
            Controls.Add(box);
            var ok = new PillBtn { Primary = true, Text = "Зберегти", OnBg = true };
            var cancel = new PillBtn { Text = "Скасувати", OnBg = true };
            int by = ClientSize.Height - Ui.S(52);
            ok.Bounds = new Rectangle(x + w - ok.Natural(), by, ok.Natural(), Ui.S(34));
            cancel.Bounds = new Rectangle(ok.Left - Ui.S(10) - cancel.Natural(), by, cancel.Natural(), Ui.S(34));
            ok.Click += (s, e) => { if (Value.Length > 0) { DialogResult = DialogResult.OK; Close(); } };
            cancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
            Controls.Add(ok);
            Controls.Add(cancel);
            Shown += (s, e) => { box.Box.Focus(); box.Box.SelectAll(); };
        }

        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); Ui.StyleCaption(this); }
    }
}
