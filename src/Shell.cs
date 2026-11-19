// Open Forge — значки (малюються лініями, як у «Тихому ПК») і бокове меню.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace OpenForge
{
    static class Icons
    {
        static PointF P(float x, float y, float s, float fx, float fy) { return new PointF(x + s * fx, y + s * fy); }

        public static void Draw(Graphics g, string key, RectangleF r, Color c)
        {
            float s = r.Width, x = r.X, y = r.Y;
            using (var pen = new Pen(c, Math.Max(1.4f, s / 12f)))
            using (var brush = new SolidBrush(c))
            {
                pen.StartCap = pen.EndCap = LineCap.Round;
                pen.LineJoin = LineJoin.Round;
                switch (key)
                {
                    case "chat":
                        using (var p = new GraphicsPath())
                        {
                            var b = new RectangleF(x + s * 0.06f, y + s * 0.1f, s * 0.88f, s * 0.64f);
                            float d = s * 0.36f;
                            p.AddArc(b.X, b.Y, d, d, 180, 90);
                            p.AddArc(b.Right - d, b.Y, d, d, 270, 90);
                            p.AddArc(b.Right - d, b.Bottom - d, d, d, 0, 90);
                            p.AddLine(b.Right - d / 2, b.Bottom, x + s * 0.42f, b.Bottom);
                            p.AddLine(x + s * 0.42f, b.Bottom, x + s * 0.24f, y + s * 0.92f);
                            p.AddLine(x + s * 0.24f, y + s * 0.92f, x + s * 0.26f, b.Bottom);
                            p.AddArc(b.X, b.Bottom - d, d, d, 90, 90);
                            p.CloseFigure();
                            g.DrawPath(pen, p);
                        }
                        break;
                    case "models":   // шари — як стос моделей
                        g.DrawPolygon(pen, new[] { P(x, y, s, 0.5f, 0.08f), P(x, y, s, 0.95f, 0.3f), P(x, y, s, 0.5f, 0.52f), P(x, y, s, 0.05f, 0.3f) });
                        g.DrawLines(pen, new[] { P(x, y, s, 0.05f, 0.5f), P(x, y, s, 0.5f, 0.72f), P(x, y, s, 0.95f, 0.5f) });
                        g.DrawLines(pen, new[] { P(x, y, s, 0.05f, 0.7f), P(x, y, s, 0.5f, 0.92f), P(x, y, s, 0.95f, 0.7f) });
                        break;
                    case "compare":
                        using (var p = Ui.Round(new RectangleF(x + s * 0.06f, y + s * 0.12f, s * 0.36f, s * 0.76f), s * 0.08f)) g.DrawPath(pen, p);
                        using (var p = Ui.Round(new RectangleF(x + s * 0.58f, y + s * 0.12f, s * 0.36f, s * 0.76f), s * 0.08f)) g.DrawPath(pen, p);
                        break;
                    case "settings":
                        for (int k = 0; k < 8; k++)
                        {
                            double a = k * Math.PI / 4;
                            g.DrawLine(pen, x + s * (0.5f + 0.3f * (float)Math.Cos(a)), y + s * (0.5f + 0.3f * (float)Math.Sin(a)),
                                x + s * (0.5f + 0.44f * (float)Math.Cos(a)), y + s * (0.5f + 0.44f * (float)Math.Sin(a)));
                        }
                        g.DrawEllipse(pen, x + s * 0.22f, y + s * 0.22f, s * 0.56f, s * 0.56f);
                        g.DrawEllipse(pen, x + s * 0.39f, y + s * 0.39f, s * 0.22f, s * 0.22f);
                        break;
                    case "send":     // стрілка вгору
                        g.DrawLine(pen, P(x, y, s, 0.5f, 0.88f), P(x, y, s, 0.5f, 0.14f));
                        g.DrawLines(pen, new[] { P(x, y, s, 0.18f, 0.44f), P(x, y, s, 0.5f, 0.12f), P(x, y, s, 0.82f, 0.44f) });
                        break;
                    case "stop":
                        Ui.FillRound(g, new RectangleF(x + s * 0.2f, y + s * 0.2f, s * 0.6f, s * 0.6f), s * 0.1f, c);
                        break;
                    case "plus":
                        g.DrawLine(pen, P(x, y, s, 0.5f, 0.14f), P(x, y, s, 0.5f, 0.86f));
                        g.DrawLine(pen, P(x, y, s, 0.14f, 0.5f), P(x, y, s, 0.86f, 0.5f));
                        break;
                    case "x":
                        g.DrawLine(pen, P(x, y, s, 0.2f, 0.2f), P(x, y, s, 0.8f, 0.8f));
                        g.DrawLine(pen, P(x, y, s, 0.8f, 0.2f), P(x, y, s, 0.2f, 0.8f));
                        break;
                    case "folder":
                        g.DrawPolygon(pen, new[] { P(x, y, s, 0.06f, 0.2f), P(x, y, s, 0.38f, 0.2f), P(x, y, s, 0.48f, 0.32f), P(x, y, s, 0.94f, 0.32f),
                            P(x, y, s, 0.94f, 0.84f), P(x, y, s, 0.06f, 0.84f) });
                        break;
                    case "new":      // олівець — «новий чат»
                        g.DrawLines(pen, new[] { P(x, y, s, 0.2f, 0.8f), P(x, y, s, 0.22f, 0.62f), P(x, y, s, 0.7f, 0.14f), P(x, y, s, 0.88f, 0.32f),
                            P(x, y, s, 0.4f, 0.8f), P(x, y, s, 0.2f, 0.8f) });
                        break;
                    case "image":   // рамка з горою й сонцем
                        using (var p = Ui.Round(new RectangleF(x + s * 0.08f, y + s * 0.16f, s * 0.84f, s * 0.68f), s * 0.12f)) g.DrawPath(pen, p);
                        g.DrawLines(pen, new[] { P(x, y, s, 0.14f, 0.76f), P(x, y, s, 0.4f, 0.48f), P(x, y, s, 0.58f, 0.64f), P(x, y, s, 0.7f, 0.54f), P(x, y, s, 0.86f, 0.7f) });
                        g.DrawEllipse(pen, x + s * 0.6f, y + s * 0.28f, s * 0.12f, s * 0.12f);
                        break;
                    case "chevron-right":
                        g.DrawLines(pen, new[] { P(x, y, s, 0.38f, 0.22f), P(x, y, s, 0.64f, 0.5f), P(x, y, s, 0.38f, 0.78f) });
                        break;
                    case "arrow-down":
                        g.DrawLine(pen, P(x, y, s, 0.5f, 0.12f), P(x, y, s, 0.5f, 0.86f));
                        g.DrawLines(pen, new[] { P(x, y, s, 0.18f, 0.56f), P(x, y, s, 0.5f, 0.88f), P(x, y, s, 0.82f, 0.56f) });
                        break;
                    case "chevron":
                        g.DrawLines(pen, new[] { P(x, y, s, 0.22f, 0.38f), P(x, y, s, 0.5f, 0.64f), P(x, y, s, 0.78f, 0.38f) });
                        break;
                    case "copy":
                        using (var p = Ui.Round(new RectangleF(x + s * 0.32f, y + s * 0.32f, s * 0.56f, s * 0.56f), s * 0.1f)) g.DrawPath(pen, p);
                        g.DrawLines(pen, new[] { P(x, y, s, 0.2f, 0.68f), P(x, y, s, 0.12f, 0.68f), P(x, y, s, 0.12f, 0.12f), P(x, y, s, 0.68f, 0.12f), P(x, y, s, 0.68f, 0.2f) });
                        break;
                    case "spark":    // логотип: іскра з кузні
                        using (var p = new GraphicsPath())
                        {
                            float cx = x + s / 2, cy = y + s / 2, rr = s * 0.46f, k = rr * 0.22f;
                            p.AddBezier(cx, cy - rr, cx + k, cy - k, cx + k, cy - k, cx + rr, cy);
                            p.AddBezier(cx + rr, cy, cx + k, cy + k, cx + k, cy + k, cx, cy + rr);
                            p.AddBezier(cx, cy + rr, cx - k, cy + k, cx - k, cy + k, cx - rr, cy);
                            p.AddBezier(cx - rr, cy, cx - k, cy - k, cx - k, cy - k, cx, cy - rr);
                            g.FillPath(brush, p);
                        }
                        break;
                    case "sun":
                        g.DrawEllipse(pen, x + s * 0.3f, y + s * 0.3f, s * 0.4f, s * 0.4f);
                        for (int k = 0; k < 8; k++)
                        {
                            double a = k * Math.PI / 4;
                            g.DrawLine(pen, x + s * (0.5f + 0.32f * (float)Math.Cos(a)), y + s * (0.5f + 0.32f * (float)Math.Sin(a)),
                                x + s * (0.5f + 0.45f * (float)Math.Cos(a)), y + s * (0.5f + 0.45f * (float)Math.Sin(a)));
                        }
                        break;
                    case "moon":
                        using (var outer = new GraphicsPath())
                        using (var inner = new GraphicsPath())
                        {
                            outer.AddEllipse(x + s * 0.1f, y + s * 0.1f, s * 0.8f, s * 0.8f);
                            inner.AddEllipse(x + s * 0.34f, y - s * 0.04f, s * 0.68f, s * 0.68f);
                            using (var rg = new Region(outer))
                            {
                                rg.Exclude(inner);
                                g.FillRegion(brush, rg);
                            }
                        }
                        break;
                    case "speaker":   // динамік
                        g.DrawPolygon(pen, new[] { P(x, y, s, 0.1f, 0.38f), P(x, y, s, 0.3f, 0.38f), P(x, y, s, 0.52f, 0.16f), P(x, y, s, 0.52f, 0.84f), P(x, y, s, 0.3f, 0.62f), P(x, y, s, 0.1f, 0.62f) });
                        g.DrawArc(pen, x + s * 0.5f, y + s * 0.3f, s * 0.24f, s * 0.4f, -55, 110);
                        g.DrawArc(pen, x + s * 0.5f, y + s * 0.14f, s * 0.42f, s * 0.72f, -55, 110);
                        break;
                    case "history":   // годинник
                        g.DrawEllipse(pen, x + s * 0.08f, y + s * 0.08f, s * 0.84f, s * 0.84f);
                        g.DrawLines(pen, new[] { P(x, y, s, 0.5f, 0.24f), P(x, y, s, 0.5f, 0.5f), P(x, y, s, 0.68f, 0.62f) });
                        break;
                    case "trash":     // кошик
                        g.DrawLine(pen, P(x, y, s, 0.12f, 0.24f), P(x, y, s, 0.88f, 0.24f));
                        g.DrawLines(pen, new[] { P(x, y, s, 0.36f, 0.24f), P(x, y, s, 0.4f, 0.1f), P(x, y, s, 0.6f, 0.1f), P(x, y, s, 0.64f, 0.24f) });
                        g.DrawLines(pen, new[] { P(x, y, s, 0.22f, 0.24f), P(x, y, s, 0.27f, 0.9f), P(x, y, s, 0.73f, 0.9f), P(x, y, s, 0.78f, 0.24f) });
                        break;
                    case "refresh":   // стрілка по колу — «ще раз»
                        g.DrawArc(pen, x + s * 0.12f, y + s * 0.12f, s * 0.76f, s * 0.76f, -60, 300);
                        g.DrawLines(pen, new[] { P(x, y, s, 0.64f, 0.1f), P(x, y, s, 0.7f, 0.2f), P(x, y, s, 0.58f, 0.27f) });
                        break;
                    case "eye":       // перегляд
                        using (var p = new GraphicsPath())
                        {
                            p.AddBezier(P(x, y, s, 0.04f, 0.5f), P(x, y, s, 0.3f, 0.12f), P(x, y, s, 0.7f, 0.12f), P(x, y, s, 0.96f, 0.5f));
                            p.AddBezier(P(x, y, s, 0.96f, 0.5f), P(x, y, s, 0.7f, 0.88f), P(x, y, s, 0.3f, 0.88f), P(x, y, s, 0.04f, 0.5f));
                            g.DrawPath(pen, p);
                        }
                        g.DrawEllipse(pen, x + s * 0.37f, y + s * 0.37f, s * 0.26f, s * 0.26f);
                        break;
                    case "file":      // аркуш із загнутим кутом
                        g.DrawLines(pen, new[] { P(x, y, s, 0.6f, 0.06f), P(x, y, s, 0.2f, 0.06f), P(x, y, s, 0.2f, 0.94f), P(x, y, s, 0.8f, 0.94f),
                            P(x, y, s, 0.8f, 0.26f), P(x, y, s, 0.6f, 0.06f), P(x, y, s, 0.6f, 0.26f), P(x, y, s, 0.8f, 0.26f) });
                        break;
                    case "phone":     // смартфон
                        using (var p = Ui.Round(new RectangleF(x + s * 0.26f, y + s * 0.06f, s * 0.48f, s * 0.88f), s * 0.1f)) g.DrawPath(pen, p);
                        g.DrawLine(pen, P(x, y, s, 0.44f, 0.8f), P(x, y, s, 0.56f, 0.8f));
                        break;
                    case "laptop":    // ноутбук
                        g.DrawRectangle(pen, x + s * 0.18f, y + s * 0.2f, s * 0.64f, s * 0.44f);
                        g.DrawLine(pen, P(x, y, s, 0.06f, 0.8f), P(x, y, s, 0.94f, 0.8f));
                        break;
                    case "panel":     // вікно з бічною колонкою праворуч
                        g.DrawRectangle(pen, x + s * 0.08f, y + s * 0.16f, s * 0.84f, s * 0.68f);
                        g.DrawLine(pen, P(x, y, s, 0.62f, 0.16f), P(x, y, s, 0.62f, 0.84f));
                        break;
                    case "sort":      // смужки, що коротшають
                        g.DrawLine(pen, P(x, y, s, 0.12f, 0.24f), P(x, y, s, 0.88f, 0.24f));
                        g.DrawLine(pen, P(x, y, s, 0.12f, 0.5f), P(x, y, s, 0.64f, 0.5f));
                        g.DrawLine(pen, P(x, y, s, 0.12f, 0.76f), P(x, y, s, 0.4f, 0.76f));
                        break;
                    case "search":    // лупа
                        g.DrawEllipse(pen, x + s * 0.1f, y + s * 0.1f, s * 0.56f, s * 0.56f);
                        g.DrawLine(pen, P(x, y, s, 0.6f, 0.6f), P(x, y, s, 0.9f, 0.9f));
                        break;
                    case "half":
                        g.DrawEllipse(pen, x + s * 0.08f, y + s * 0.08f, s * 0.84f, s * 0.84f);
                        g.FillPie(brush, x + s * 0.08f, y + s * 0.08f, s * 0.84f, s * 0.84f, 90, 180);
                        break;
                }
            }
        }
    }

    // Бокове меню: логотип, «Новий чат», під ним — список чатів (історія), унизу — «Налаштування»,
    // версія й кнопка оновлення.
    class Sidebar : UiControl
    {
        int selected, hover = -1;     // 0 — «Новий чат», 1 — «Налаштування», -1 — нічого
        bool hoverButton;
        string update;
        Control list;
        public event Action<int> ItemClicked;
        public event Action UpdateClicked;

        public int Selected { get { return selected; } set { selected = value; Invalidate(); } }
        public string NewVersion { get { return update; } set { update = value; Invalidate(); OnResize(EventArgs.Empty); } }

        // Список чатів між «Новим чатом» і «Налаштуваннями».
        public Control List
        {
            set { list = value; Controls.Add(value); OnResize(EventArgs.Empty); }
        }

        // Пошук у чатах — над списком (замість підпису «ЧАТИ»).
        Control search;
        public Control Search
        {
            set { search = value; Controls.Add(value); OnResize(EventArgs.Empty); }
        }

        Rectangle ItemRect(int i)
        {
            int y = i == 0 ? Ui.S(84) : Height - Ui.S(update != null ? 132 : 84);
            return new Rectangle(Ui.S(12), y, Width - Ui.S(24), Ui.S(38));
        }

        Rectangle ButtonRect { get { return new Rectangle(Ui.S(14), Height - Ui.S(52), Width - Ui.S(28), Ui.S(36)); } }
        bool ShowButton { get { return update != null; } }
        int ListTop { get { return Ui.S(search != null ? 172 : 150); } }

        // Показники навантаження ПК — над «Налаштуваннями» (ховаються, якщо вікно надто низьке).
        const int MeterH = 27;
        bool ShowMeters { get { return Height >= Ui.S(560); } }
        int MetersTop { get { return ItemRect(1).Y - Ui.S(14) - Ui.S(4 * MeterH); } }
        // Пристрої, підключені кабелем, — над показниками ПК.
        List<DeviceInfo> devices = new List<DeviceInfo>();
        string devStatus;                       // наприклад «Завантажую інструменти Android… 40 %»
        int devHover = -2;                      // -1 — рядок «нічого не підключено», 0.. — пристрій
        public event Action<DeviceInfo, Point> DeviceClicked;
        public List<DeviceInfo> Devices { set { devices = value ?? new List<DeviceInfo>(); OnResize(EventArgs.Empty); } }
        public string DeviceStatus { set { devStatus = value; Invalidate(); } }
        bool ShowDevices { get { return Height >= Ui.S(600); } }
        int DevRows { get { return Math.Max(1, Math.Min(3, devices.Count)); } }
        int DevBottom { get { return ShowMeters ? MetersTop - Ui.S(18) : ItemRect(1).Y - Ui.S(12); } }
        int DevTop { get { return DevBottom - Ui.S(22) - DevRows * Ui.S(40); } }
        Rectangle DevRow(int i) { return new Rectangle(Ui.S(12), DevTop + Ui.S(22) + i * Ui.S(40), Width - Ui.S(24), Ui.S(38)); }
        int ListBottom { get { return ShowDevices ? DevTop - Ui.S(10) : (ShowMeters ? MetersTop - Ui.S(14) : ItemRect(1).Y - Ui.S(8)); } }

        static Color DevColor(DeviceState s)
        {
            return s == DeviceState.Ok ? Ui.Green : s == DeviceState.Attention ? Ui.Orange : s == DeviceState.Service ? Ui.Purple : Ui.Red;
        }

        void PaintDevices(Graphics g)
        {
            using (var pen = new Pen(Ui.CardLine, Math.Max(1f, Ui.Scale))) g.DrawLine(pen, Ui.S(16), DevTop - Ui.S(4), Width - Ui.S(16), DevTop - Ui.S(4));
            Ui.Text(g, "ПРИСТРОЇ", Ui.Caps, new Rectangle(Ui.S(24), DevTop + Ui.S(2), Width - Ui.S(40), Ui.S(16)), Ui.Ink3, Ui.Left);
            if (devices.Count == 0)
            {
                var r = DevRow(0);
                if (devHover == -1) Ui.FillRound(g, r, Ui.S(9), Ui.Mix(Ui.Side, Ui.Ink, 0.06f));
                float s = Ui.S(16);
                Icons.Draw(g, "phone", new RectangleF(r.X + Ui.S(12), r.Y + (r.Height - s) / 2f, s, s), Ui.Ink3);
                Ui.Text(g, devStatus ?? "Підключи телефон кабелем", Ui.Small, new Rectangle(r.X + Ui.S(40), r.Y, r.Width - Ui.S(44), r.Height), Ui.Ink3, Ui.Left);
                return;
            }
            for (int i = 0; i < DevRows; i++)
            {
                var d = devices[i];
                var r = DevRow(i);
                if (devHover == i) Ui.FillRound(g, r, Ui.S(9), Ui.Mix(Ui.Side, Ui.Ink, 0.06f));
                float s = Ui.S(17);
                Icons.Draw(g, d.Kind == "apple" && d.Mode.StartsWith("DFU") ? "laptop" : "phone", new RectangleF(r.X + Ui.S(11), r.Y + (r.Height - s) / 2f, s, s), Ui.Ink2);
                float dd = Ui.S(7);
                using (var b = new SolidBrush(DevColor(d.State))) g.FillEllipse(b, r.X + Ui.S(22), r.Y + r.Height / 2f + Ui.S(2), dd, dd);
                Ui.Text(g, d.Name, Ui.Body, new Rectangle(r.X + Ui.S(40), r.Y + Ui.S(2), r.Width - Ui.S(44), Ui.S(18)), Ui.Ink, Ui.Left);
                string mode = i == DevRows - 1 && devices.Count > DevRows ? d.Mode + " · ще " + (devices.Count - DevRows) : d.Mode;
                Ui.Text(g, devStatus != null && i == 0 ? devStatus : mode, Ui.Caps, new Rectangle(r.X + Ui.S(40), r.Y + Ui.S(20), r.Width - Ui.S(44), Ui.S(16)), DevColor(d.State), Ui.Left);
            }
        }

        int DevHit(Point p)
        {
            if (!ShowDevices) return -2;
            if (devices.Count == 0) return DevRow(0).Contains(p) ? -1 : -2;
            for (int i = 0; i < DevRows; i++) if (DevRow(i).Contains(p)) return i;
            return -2;
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (list != null) list.Bounds = new Rectangle(Ui.S(4), ListTop, Width - Ui.S(6), Math.Max(0, ListBottom - ListTop));
            if (search != null) search.Bounds = new Rectangle(Ui.S(12), Ui.S(128), Width - Ui.S(24), Ui.S(36));
            Invalidate();
        }

        // Перемалювати лише смужку показників (раз на 1,5 с).
        public void RefreshMeters() { if (ShowMeters) Invalidate(new Rectangle(0, MetersTop - Ui.S(4), Width, Ui.S(4 * MeterH + 8))); }

        static string Pct(float v) { return v < 0 ? "—" : Math.Round(v) + " %"; }
        static string Gb(double used, double total) { return total <= 0 ? "—" : used.ToString("0.0") + " / " + total.ToString("0") + " ГБ"; }

        void PaintMeters(Graphics g)
        {
            var rows = new[]
            {
                Tuple.Create("Процесор", Pct(PcMonitor.Cpu), PcMonitor.Cpu, Ui.Blue),
                Tuple.Create("Пам'ять", Gb(PcMonitor.RamUsedGb, PcMonitor.RamTotalGb), PcMonitor.Ram, Ui.Green),
                Tuple.Create("Відеокарта", Pct(PcMonitor.Gpu), PcMonitor.Gpu, Ui.Orange),
                Tuple.Create("Відеопам'ять", Gb(PcMonitor.VramUsedGb, PcMonitor.VramTotalGb), PcMonitor.Vram, Ui.Purple),
            };
            int x = Ui.S(24), w = Width - Ui.S(46), y = MetersTop;
            foreach (var r in rows)
            {
                Ui.Text(g, r.Item1, Ui.Caps, new Rectangle(x, y, w / 2, Ui.S(16)), Ui.Ink2, Ui.Left);
                Ui.Text(g, r.Item2, Ui.Caps, new Rectangle(x + w / 2, y, w / 2, Ui.S(16)), Ui.Ink, Ui.Left | TextFormatFlags.Right);
                var bar = new RectangleF(x, y + Ui.S(18), w, Ui.S(4));
                Ui.FillRound(g, bar, bar.Height / 2f, Ui.Mix(Ui.Side, Ui.Ink, 0.1f));
                if (r.Item3 > 0) Ui.FillRound(g, new RectangleF(bar.X, bar.Y, Math.Max(bar.Height, bar.Width * Math.Min(100, r.Item3) / 100f), bar.Height), bar.Height / 2f, r.Item4);
                y += Ui.S(MeterH);
            }
        }

        static readonly string[] Labels = { "Новий чат", "Налаштування" };
        static readonly string[] Keys = { "new", "settings" };

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Prep(e);
            g.Clear(Ui.Side);
            using (var pen = new Pen(Ui.CardLine, Math.Max(1f, Ui.Scale))) g.DrawLine(pen, Width - 1, 0, Width - 1, Height);

            // Логотип: синій кружечок з іскрою.
            var logo = new RectangleF(Ui.S(22), Ui.S(28), Ui.S(30), Ui.S(30));
            using (var b = new SolidBrush(Ui.Blue)) g.FillEllipse(b, logo);
            Icons.Draw(g, "spark", RectangleF.Inflate(logo, -Ui.S(7), -Ui.S(7)), Color.White);
            Ui.Text(g, "Open Forge", Ui.Brand, new Rectangle(Ui.S(60), Ui.S(24), Width - Ui.S(80), Ui.S(22)), Ui.Ink, Ui.Left);
            Ui.Text(g, "Нейромережі на твоєму ПК", Ui.Caps, new Rectangle(Ui.S(60), Ui.S(46), Width - Ui.S(80), Ui.S(16)), Ui.Ink2, Ui.Left);

            for (int i = 0; i < 2; i++)
            {
                var r = ItemRect(i);
                bool sel = i == selected;
                if (sel) Ui.FillRound(g, r, Ui.S(9), Ui.RowSel);
                else if (i == hover) Ui.FillRound(g, r, Ui.S(9), Ui.Mix(Ui.Side, Ui.Ink, 0.06f));
                float s = Ui.S(17);
                Icons.Draw(g, Keys[i], new RectangleF(r.X + Ui.S(12), r.Y + (r.Height - s) / 2f, s, s), sel ? Ui.Blue : Ui.Ink2);
                Ui.Text(g, Labels[i], sel ? Ui.BodySemi : Ui.Body, new Rectangle(r.X + Ui.S(40), r.Y, r.Width - Ui.S(44), r.Height), sel ? Ui.Blue : Ui.Ink, Ui.Left);
            }
            if (ShowDevices) PaintDevices(g);
            if (ShowMeters)
            {
                PaintMeters(g);
                using (var pen = new Pen(Ui.CardLine, Math.Max(1f, Ui.Scale)))
                    g.DrawLine(pen, Ui.S(16), MetersTop - Ui.S(10), Width - Ui.S(16), MetersTop - Ui.S(10));
            }
            if (search == null) Ui.Text(g, "ЧАТИ", Ui.Caps, new Rectangle(Ui.S(24), Ui.S(130), Width - Ui.S(40), Ui.S(16)), Ui.Ink3, Ui.Left);
            using (var pen = new Pen(Ui.CardLine, Math.Max(1f, Ui.Scale)))
                g.DrawLine(pen, Ui.S(16), ItemRect(1).Y - Ui.S(6), Width - Ui.S(16), ItemRect(1).Y - Ui.S(6));

            Ui.Text(g, "Версія " + AppVersion.Text + (update != null ? " · є нова " + update : ""), Ui.Caps,
                new Rectangle(Ui.S(26), Height - Ui.S(update != null ? 80 : 36), Width - Ui.S(38), Ui.S(20)), update != null ? Ui.Blue : Ui.Ink3, Ui.Left);
            if (ShowButton)
            {
                var b = ButtonRect;
                Ui.FillRound(g, b, b.Height / 2f, hoverButton ? Ui.Mix(Ui.Blue, Color.Black, 0.12f) : Ui.Blue);
                Ui.Text(g, "Оновити до " + update, Ui.BodySemi, b, Color.White, Ui.Center);
            }
        }

        int Hit(Point p)
        {
            for (int i = 0; i < 2; i++) if (ItemRect(i).Contains(p)) return i;
            return -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int h = Hit(e.Location);
            bool hb = ShowButton && ButtonRect.Contains(e.Location);
            int dh = DevHit(e.Location);
            if (h != hover || hb != hoverButton || dh != devHover) { hover = h; hoverButton = hb; devHover = dh; Invalidate(); }
            Cursor = h >= 0 || hb || dh > -2 ? Cursors.Hand : Cursors.Default;
        }

        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hover = -1; devHover = -2; hoverButton = false; Invalidate(); }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            int dh = DevHit(e.Location);
            if (dh > -2) { if (DeviceClicked != null) DeviceClicked(dh >= 0 ? devices[dh] : null, PointToScreen(e.Location)); return; }
            if (e.Button != MouseButtons.Left) return;
            if (ShowButton && ButtonRect.Contains(e.Location)) { if (UpdateClicked != null) UpdateClicked(); return; }
            int h = Hit(e.Location);
            if (h >= 0 && ItemClicked != null) ItemClicked(h);
        }
    }
}
