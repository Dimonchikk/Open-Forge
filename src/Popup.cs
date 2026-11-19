// Open Forge — випадне меню в стилі програми (замість сірого системного): заокруглена картка з тінню,
// рядок під мишею підсвічується, вибраний пункт — синій з галочкою. Світла й темна тема.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace OpenForge
{
    class PopupItem
    {
        public string Text, Sub;     // Sub — блідий підпис праворуч (наприклад розмір моделі)
        public bool Checked;
        public Action Click;
        public PopupItem(string text, bool check, Action click) { Text = text; Checked = check; Click = click; }
    }

    class PopupMenu : UiControl
    {
        readonly List<PopupItem> items;
        int hover = -1;
        ToolStripDropDown dd;

        PopupMenu(List<PopupItem> items) { this.items = items; Cursor = Cursors.Hand; }

        static int RowH { get { return Ui.S(36); } }
        static int Pad { get { return Ui.S(6); } }

        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        // Показати під елементом anchor (ширина — не менша за anchor і за найдовший пункт).
        public static void Show(Control anchor, List<PopupItem> items)
        {
            var menu = new PopupMenu(items);
            int w = anchor.Width;
            foreach (var it in items)
            {
                int tw = TextRenderer.MeasureText(it.Text, Ui.Body).Width + Ui.S(64);
                if (!string.IsNullOrEmpty(it.Sub)) tw += TextRenderer.MeasureText(it.Sub, Ui.Small).Width + Ui.S(16);
                w = Math.Max(w, tw);
            }
            menu.Size = new Size(Math.Min(w, Ui.S(560)), items.Count * RowH + 2 * Pad);

            var dd = new ToolStripDropDown { Padding = Padding.Empty, Margin = Padding.Empty, DropShadowEnabled = true, BackColor = Ui.Card, AutoSize = false };
            var host = new ToolStripControlHost(menu) { Padding = Padding.Empty, Margin = Padding.Empty, AutoSize = false, Size = menu.Size };
            dd.Items.Add(host);
            dd.Size = menu.Size;
            menu.dd = dd;
            dd.Closed += (s, e) => { var d = dd; anchor.BeginInvoke((Action)(() => d.Dispose())); };
            dd.HandleCreated += (s, e) =>
            {
                // Windows 11: справжні заокруглені кути з тінню; на Windows 10 просто нічого не станеться.
                try { int round = 2; DwmSetWindowAttribute(dd.Handle, 33, ref round, 4); } catch { }
            };
            // Якщо знизу не влазить — над елементом.
            var screen = Screen.FromControl(anchor).WorkingArea;
            var below = anchor.PointToScreen(new Point(0, anchor.Height + Ui.S(6)));
            if (below.Y + dd.Height > screen.Bottom) below = anchor.PointToScreen(new Point(0, -dd.Height - Ui.S(6)));
            if (below.X + dd.Width > screen.Right) below.X = screen.Right - dd.Width;
            dd.Show(below);
            menu.Focus();
        }

        Rectangle Row(int i) { return new Rectangle(Pad, Pad + i * RowH, Width - 2 * Pad, RowH); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Ui.Card);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using (var pen = new Pen(Ui.Dark ? Ui.Separator : Ui.CardLine, Math.Max(1f, Ui.Scale))) g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
            for (int i = 0; i < items.Count; i++)
            {
                var r = Row(i);
                var it = items[i];
                if (i == hover) Ui.FillRound(g, r, Ui.S(8), Ui.Dark ? Ui.Fill : Ui.Mix(Ui.Card, Ui.Ink, 0.06f));
                else if (it.Checked) Ui.FillRound(g, r, Ui.S(8), Ui.Mix(Ui.Card, Ui.Blue, Ui.Dark ? 0.16f : 0.09f));
                Color fore = it.Checked ? Ui.Blue : Ui.Ink;
                int right = r.Right - Ui.S(34);
                if (!string.IsNullOrEmpty(it.Sub))
                {
                    int sw = TextRenderer.MeasureText(it.Sub, Ui.Small).Width;
                    Ui.Text(g, it.Sub, Ui.Small, new Rectangle(right - sw, r.Y, sw, r.Height), Ui.Ink3, Ui.Left);
                    right -= sw + Ui.S(12);
                }
                Ui.Text(g, it.Text, it.Checked ? Ui.BodySemi : Ui.Body, new Rectangle(r.X + Ui.S(12), r.Y, right - r.X - Ui.S(12), r.Height), fore, Ui.Left);
                if (it.Checked)
                {
                    float s = Ui.S(14);
                    var ic = new RectangleF(r.Right - Ui.S(26), r.Y + (r.Height - s) / 2f, s, s);
                    using (var pen = new Pen(Ui.Blue, Math.Max(1.6f, Ui.S(2))) { StartCap = System.Drawing.Drawing2D.LineCap.Round, EndCap = System.Drawing.Drawing2D.LineCap.Round, LineJoin = System.Drawing.Drawing2D.LineJoin.Round })
                        g.DrawLines(pen, new[] { new PointF(ic.X + s * 0.12f, ic.Y + s * 0.52f), new PointF(ic.X + s * 0.4f, ic.Y + s * 0.8f), new PointF(ic.X + s * 0.9f, ic.Y + s * 0.22f) });
                }
            }
        }

        int Hit(Point p)
        {
            for (int i = 0; i < items.Count; i++) if (Row(i).Contains(p)) return i;
            return -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int h = Hit(e.Location);
            if (h != hover) { hover = h; Invalidate(); }
        }

        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); if (hover >= 0) { hover = -1; Invalidate(); } }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            int i = Hit(e.Location);
            if (i < 0) return;
            if (dd != null) dd.Close();
            if (items[i].Click != null) items[i].Click();
        }

        protected override bool IsInputKey(Keys k) { return k == Keys.Up || k == Keys.Down || k == Keys.Enter || base.IsInputKey(k); }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Down) { hover = (hover + 1) % items.Count; Invalidate(); }
            else if (e.KeyCode == Keys.Up) { hover = hover <= 0 ? items.Count - 1 : hover - 1; Invalidate(); }
            else if (e.KeyCode == Keys.Enter && hover >= 0) { if (dd != null) dd.Close(); if (items[hover].Click != null) items[hover].Click(); }
            else if (e.KeyCode == Keys.Escape && dd != null) dd.Close();
        }
    }
}
