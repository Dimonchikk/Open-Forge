// Open Forge — агент бачить і керує вікнами програм, які сам запустив: знімок вікна одразу після запуску,
// список кнопок і полів (через UI Automation Windows), клік по кнопці чи меню за текстом або за координатами
// зі скріншота, введення тексту, клавіші. Після кожної дії — новий скріншот, як у людини, що тестує програму.
// А ще — перевірка «повного проєкту»: заглушки замість коду й незапущений проєкт агент не здасть.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace OpenForge
{
    partial class AgentRun
    {
        static bool OnWindows { get { return Environment.OSVersion.Platform == PlatformID.Win32NT; } }

        // ── вікна ────────────────────────────────────────────────────────────────

        class Win { public IntPtr H; public string Title, Class; public int Pid; public Rectangle R; }

        [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc cb, IntPtr lp);
        [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr parent, EnumProc cb, IntPtr lp);
        delegate bool EnumProc(IntPtr h, IntPtr lp);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll")] static extern bool IsWindow(IntPtr h);
        [DllImport("user32.dll")] static extern bool IsIconic(IntPtr h);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder sb, int max);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder sb, int max);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out int pid);
        [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int cmd);
        [DllImport("user32.dll")] static extern bool SetCursorPos(int x, int y);
        [DllImport("user32.dll")] static extern void mouse_event(uint flags, int dx, int dy, uint data, UIntPtr extra);
        [DllImport("user32.dll")] static extern bool PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(Point p);
        [DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr h, uint flags);
        [DllImport("user32.dll")] static extern bool AttachThreadInput(uint a, uint b, bool attach);
        [DllImport("user32.dll")] static extern bool BringWindowToTop(IntPtr h);
        [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
        [DllImport("user32.dll")] static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);

        static int PidOf(IntPtr h) { int pid = 0; if (h != IntPtr.Zero) GetWindowThreadProcessId(h, out pid); return pid; }

        // Чи вікно програми зараз на передньому плані (тільки тоді можна натискати клавіші й мишку).
        static bool InFront(Win w) { return PidOf(GetForegroundWindow()) == w.Pid; }

        // Вивести вікно наперед. Windows часто не дає фоновій програмі забрати фокус — пробуємо кілька способів
        // і чесно кажемо, якщо не вийшло (тоді клавіш і кліків мишкою НЕ буде — щоб не надрукувати в чужому вікні).
        static bool Front(Win w)
        {
            if (IsIconic(w.H)) ShowWindow(w.H, 9);
            for (int attempt = 0; attempt < 3; attempt++)
            {
                if (InFront(w)) return true;
                IntPtr fg = GetForegroundWindow();
                int dummy;
                uint fgThread = fg == IntPtr.Zero ? 0 : GetWindowThreadProcessId(fg, out dummy), me = GetCurrentThreadId();
                keybd_event(0x12, 0, 0, UIntPtr.Zero); keybd_event(0x12, 0, 2, UIntPtr.Zero);   // Alt: дозвіл змінити фокус
                if (fgThread != 0 && fgThread != me) AttachThreadInput(me, fgThread, true);
                BringWindowToTop(w.H);
                SetForegroundWindow(w.H);
                if (fgThread != 0 && fgThread != me) AttachThreadInput(me, fgThread, false);
                Thread.Sleep(250);
            }
            return InFront(w);
        }

        const string NoFocus = "Windows не дав вивести вікно програми на передній план, тому клавіші й кліки мишкою скасовано " +
                               "(інакше вони потрапили б у чуже вікно). Спробуй дію з target — кнопки й поля натискаються без мишки — або попроси користувача клікнути у вікно програми.";

        // ── UI Automation без мишки й клавіатури: натиснути кнопку, вписати текст у поле ─────
        static bool TryPattern(object el, string patternType, out object pattern)
        {
            pattern = null;
            if (el == null || uiaClient == null) return false;
            try
            {
                var pt = uiaClient.GetType("System.Windows.Automation." + patternType);
                if (pt == null) return false;
                object id = pt.GetField("Pattern").GetValue(null);
                var m = el.GetType().GetMethod("TryGetCurrentPattern");
                var args = new object[] { id, null };
                if (!(bool)m.Invoke(el, args)) return false;
                pattern = args[1];
                return pattern != null;
            }
            catch { return false; }
        }

        // Натиснути елемент «по-справжньому», але без мишки. true — вдалося.
        static bool UiaPress(object el)
        {
            object p;
            try
            {
                if (TryPattern(el, "InvokePattern", out p)) { p.GetType().GetMethod("Invoke").Invoke(p, null); return true; }
                if (TryPattern(el, "TogglePattern", out p)) { p.GetType().GetMethod("Toggle").Invoke(p, null); return true; }
                if (TryPattern(el, "SelectionItemPattern", out p)) { p.GetType().GetMethod("Select").Invoke(p, null); return true; }
                if (TryPattern(el, "ExpandCollapsePattern", out p)) { p.GetType().GetMethod("Expand").Invoke(p, null); return true; }
            }
            catch { }
            return false;
        }

        static bool UiaSetText(object el, string text)
        {
            object p;
            try
            {
                if (!TryPattern(el, "ValuePattern", out p)) return false;
                object ro = Prop(Prop(p, "Current"), "IsReadOnly");
                if (ro is bool && (bool)ro) return false;
                p.GetType().GetMethod("SetValue").Invoke(p, new object[] { text.Replace("\r\n", "\n").Replace("\n", "\r\n") });
                return true;
            }
            catch { return false; }
        }

        static string WText(IntPtr h, bool cls)
        {
            var sb = new StringBuilder(512);
            if (cls) GetClassName(h, sb, sb.Capacity); else GetWindowText(h, sb, sb.Capacity);
            return sb.ToString();
        }

        static List<Win> TopWindows()
        {
            var list = new List<Win>();
            if (!OnWindows) return list;
            int self = Process.GetCurrentProcess().Id;
            EnumWindows((h, lp) =>
            {
                if (!IsWindowVisible(h)) return true;
                RECT r;
                if (!GetWindowRect(h, out r) || r.R - r.L < 40 || r.B - r.T < 30) return true;
                int pid;
                GetWindowThreadProcessId(h, out pid);
                if (pid == self) return true;
                list.Add(new Win { H = h, Title = WText(h, false), Class = WText(h, true), Pid = pid, R = Rectangle.FromLTRB(r.L, r.T, r.R, r.B) });
                return true;
            }, IntPtr.Zero);
            return list;
        }

        // Вікна, що вже були до початку роботи агента: ними він не керує (це вікна користувача).
        HashSet<IntPtr> userWindows;
        void RememberUserWindows()
        {
            userWindows = new HashSet<IntPtr>();
            try { foreach (var w in TopWindows()) userWindows.Add(w.H); } catch { }
        }
        bool Mine(Win w) { return userWindows == null || !userWindows.Contains(w.H); }

        Win FindWin(string title)
        {
            Win best = null;
            foreach (var w in TopWindows())
            {
                if (w.Title.Length == 0 || w.Title.IndexOf(title ?? "", StringComparison.OrdinalIgnoreCase) < 0) continue;
                // Спершу — вікна, які відкрив агент; серед них — з точною назвою.
                int score = (Mine(w) ? 2 : 0) + (string.Equals(w.Title, title, StringComparison.OrdinalIgnoreCase) ? 1 : 0);
                int bestScore = best == null ? -1 : (Mine(best) ? 2 : 0) + (string.Equals(best.Title, title, StringComparison.OrdinalIgnoreCase) ? 1 : 0);
                if (score > bestScore) best = w;
            }
            return best;
        }

        // ── скріншоти вікон ──────────────────────────────────────────────────────

        // Що було на останньому знімку: щоб перевести координати з картинки в точку екрана.
        IntPtr shotHwnd;
        Point shotOrigin;
        double shotScale = 1;
        Size shotSize;

        static double EncodeScale(int w, int h) { return Math.Min(1.0, 1600.0 / Math.Max(w, h)); }

        // Знімок вікна (навіть якщо воно під іншими) через PrintWindow.
        bool ShotHwnd(Win w)
        {
            if (IsIconic(w.H)) { ShowWindow(w.H, 9); Thread.Sleep(400); RECT rr; if (GetWindowRect(w.H, out rr)) w.R = Rectangle.FromLTRB(rr.L, rr.T, rr.R, rr.B); }
            if (w.R.Width < 10 || w.R.Height < 10) return false;
            using (var bmp = new Bitmap(w.R.Width, w.R.Height))
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    IntPtr hdc = g.GetHdc();
                    try { PrintWindow(w.H, hdc, 2); }   // 2 — PW_RENDERFULLCONTENT: працює й для сучасних вікон
                    finally { g.ReleaseHdc(hdc); }
                }
                AddShot(Pictures.Encode(bmp));
            }
            RememberShot(w.H, w.R);
            return true;
        }

        // Знімок ділянки екрана: вікно разом з меню й діалогами, що відкрились після кліку.
        bool ShotScreen(Win w)
        {
            var area = w.R;
            foreach (var o in TopWindows())
                if (o.Pid == w.Pid && o.H != w.H && Mine(o)) area = Rectangle.Union(area, o.R);
            area.Intersect(SystemInformation.VirtualScreen);
            if (area.Width < 10 || area.Height < 10) return ShotHwnd(w);
            using (var bmp = new Bitmap(area.Width, area.Height))
            {
                using (var g = Graphics.FromImage(bmp)) g.CopyFromScreen(area.Location, Point.Empty, area.Size);
                AddShot(Pictures.Encode(bmp));
            }
            RememberShot(w.H, area);
            return true;
        }

        void RememberShot(IntPtr h, Rectangle area)
        {
            shotHwnd = h;
            shotOrigin = area.Location;
            shotScale = EncodeScale(area.Width, area.Height);
            shotSize = new Size((int)(area.Width * shotScale), (int)(area.Height * shotScale));
        }

        string ShotNote()
        {
            return ModelSeesImages ? " Скріншот (" + shotSize.Width + "×" + shotSize.Height + " px) надіслано тобі наступним повідомленням; координати x,y для кліку бери з нього." : "";
        }

        // Після start_program: дочекатися нового вікна програми й одразу його сфотографувати.
        string AutoShotAfterStart(HashSet<IntPtr> before)
        {
            if (!OnWindows) return "";
            var sw = Stopwatch.StartNew();
            Win found = null;
            while (sw.ElapsedMilliseconds < 9000 && !cancel)
            {
                foreach (var w in TopWindows())
                    if (!before.Contains(w.H) && w.Title.Length > 0 && w.R.Width > 120 && w.R.Height > 80) { found = w; break; }
                if (found != null) break;
                Thread.Sleep(400);
            }
            if (found == null) return "\nНового вікна не з'явилось (консольна програма, сервер або програма ще запускається).";
            Thread.Sleep(900);   // дати вікну домалюватись
            RECT r;
            if (GetWindowRect(found.H, out r)) found.R = Rectangle.FromLTRB(r.L, r.T, r.R, r.B);
            if (!ShotHwnd(found)) return "\nВідкрилось вікно «" + found.Title + "», але зробити знімок не вдалося.";
            return "\nВідкрилось вікно «" + found.Title + "»; знімок показано користувачу." + ShotNote() + Elements(found);
        }

        // ── UI Automation (кнопки, поля, меню) — без посилань на збірки, через рефлексію ─────

        class El { public string Name, Type; public Rectangle R; public object Obj; }

        static Assembly uiaClient;
        static bool uiaTried;

        static bool LoadUia()
        {
            if (uiaTried) return uiaClient != null;
            uiaTried = true;
            if (!OnWindows) return false;
            try
            {
                uiaClient = Assembly.Load("UIAutomationClient, Version=4.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35");
                Assembly.Load("UIAutomationTypes, Version=4.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35");
            }
            catch { uiaClient = null; }
            return uiaClient != null;
        }

        static object Prop(object o, string name)
        {
            if (o == null) return null;
            var p = o.GetType().GetProperty(name);
            return p == null ? null : p.GetValue(o, null);
        }

        // Усі видимі елементи вікна з назвою (кнопки, пункти меню, поля, вкладки…).
        static List<El> UiElements(IntPtr h)
        {
            var list = new List<El>();
            if (!LoadUia()) return list;
            var t = new Thread(() =>
            {
                try
                {
                    var ae = uiaClient.GetType("System.Windows.Automation.AutomationElement");
                    var condT = uiaClient.GetType("System.Windows.Automation.Condition");
                    object cond = condT.GetField("TrueCondition").GetValue(null);
                    Type scopeT = null;
                    foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
                        if (a.GetName().Name == "UIAutomationTypes") scopeT = a.GetType("System.Windows.Automation.TreeScope");
                    if (scopeT == null) return;
                    object root = ae.GetMethod("FromHandle").Invoke(null, new object[] { h });
                    var all = ae.GetMethod("FindAll", new[] { scopeT, condT }).Invoke(root, new[] { Enum.ToObject(scopeT, 4), cond }) as IEnumerable;
                    if (all == null) return;
                    int n = 0;
                    foreach (object e in all)
                    {
                        if (++n > 1500) break;
                        try
                        {
                            object cur = Prop(e, "Current");
                            string name = (Prop(cur, "Name") as string ?? "").Trim();
                            if (name.Length == 0 || name.Length > 80) continue;
                            if (Prop(cur, "IsOffscreen") is bool && (bool)Prop(cur, "IsOffscreen")) continue;
                            object rect = Prop(cur, "BoundingRectangle");
                            if (rect == null || (Prop(rect, "IsEmpty") is bool && (bool)Prop(rect, "IsEmpty"))) continue;
                            var r = new Rectangle((int)(double)Prop(rect, "X"), (int)(double)Prop(rect, "Y"), (int)(double)Prop(rect, "Width"), (int)(double)Prop(rect, "Height"));
                            if (r.Width < 2 || r.Height < 2) continue;
                            string type = Prop(cur, "LocalizedControlType") as string ?? "";
                            lock (list) list.Add(new El { Name = name, Type = type, R = r, Obj = e });
                        }
                        catch { }
                    }
                }
                catch { }
            }) { IsBackground = true };
            t.SetApartmentState(ApartmentState.MTA);
            t.Start();
            t.Join(8000);   // дуже складні вікна не мають зупиняти агента
            lock (list) return new List<El>(list);
        }

        // Елементи головного вікна й відкритих меню / діалогів тієї ж програми.
        List<El> AllElements(Win w)
        {
            var list = UiElements(w.H);
            foreach (var o in TopWindows())
                if (o.Pid == w.Pid && o.H != w.H && Mine(o)) list.AddRange(UiElements(o.H));
            if (list.Count == 0)   // без UI Automation — хоча б звичайні дочірні вікна (кнопки, поля)
                EnumChildWindows(w.H, (c, lp) =>
                {
                    if (!IsWindowVisible(c)) return true;
                    string t = WText(c, false).Trim();
                    RECT r;
                    if (t.Length > 0 && t.Length <= 80 && GetWindowRect(c, out r))
                        list.Add(new El { Name = t, Type = WText(c, true).IndexOf("BUTTON", StringComparison.OrdinalIgnoreCase) >= 0 ? "кнопка" : "", R = Rectangle.FromLTRB(r.L, r.T, r.R, r.B) });
                    return true;
                }, IntPtr.Zero);
            return list;
        }

        // Текстовий список елементів — щоб і модель без «очей» знала, що є у вікні й куди можна натиснути.
        string Elements(Win w)
        {
            List<El> els;
            try { els = AllElements(w); } catch { return ""; }
            if (els.Count == 0) return "";
            var sb = new StringBuilder("\nЕлементи у вікні (тип: назва):");
            var seen = new HashSet<string>();
            int n = 0;
            foreach (var e in els)
            {
                string line = (e.Type.Length > 0 ? e.Type + ": " : "") + e.Name;
                if (!seen.Add(line)) continue;
                sb.Append("\n- ").Append(line);
                if (++n >= 45) { sb.Append("\n- …"); break; }
            }
            return sb.ToString();
        }

        static bool Num(object v, out int n)
        {
            n = 0;
            double d;
            if (v is string) { if (!double.TryParse(((string)v).Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out d)) return false; }
            else try { d = Convert.ToDouble(v, System.Globalization.CultureInfo.InvariantCulture); } catch { return false; }
            n = (int)Math.Round(d);
            return true;
        }

        static string Norm(string s) { return Regex.Replace((s ?? "").Replace("&", ""), @"\s+", " ").Trim().ToLowerInvariant(); }

        El FindEl(Win w, string target)
        {
            string t = Norm(target);
            El exact = null, part = null;
            foreach (var e in AllElements(w))
            {
                string n = Norm(e.Name);
                if (n == t) { if (exact == null || e.R.Width * e.R.Height < exact.R.Width * exact.R.Height) exact = e; }
                else if (part == null && n.Contains(t)) part = e;
            }
            return exact ?? part;
        }

        // ── дії у вікні ──────────────────────────────────────────────────────────

        const uint LDown = 0x02, LUp = 0x04, RDown = 0x08, RUp = 0x10;

        static void ClickAt(Point p, bool right, bool twice)
        {
            SetCursorPos(p.X, p.Y);
            Thread.Sleep(80);
            for (int i = 0; i < (twice ? 2 : 1); i++)
            {
                mouse_event(right ? RDown : LDown, 0, 0, 0, UIntPtr.Zero);
                mouse_event(right ? RUp : LUp, 0, 0, 0, UIntPtr.Zero);
                if (twice) Thread.Sleep(60);
            }
        }

        // Клік мишкою — лише якщо під курсором справді вікно цієї програми.
        static bool ClickSafe(Win w, Point p, bool right, bool twice)
        {
            IntPtr under = WindowFromPoint(p);
            if (PidOf(GetAncestor(under, 2)) != w.Pid && PidOf(under) != w.Pid) return false;
            ClickAt(p, right, twice);
            return true;
        }

        static string EscapeKeys(string s)
        {
            var sb = new StringBuilder();
            foreach (char c in s ?? "")
            {
                if ("+^%~(){}[]".IndexOf(c) >= 0) sb.Append('{').Append(c).Append('}');
                else if (c == '\n') sb.Append("{ENTER}");
                else if (c != '\r') sb.Append(c);
            }
            return sb.ToString();
        }

        bool WindowAction(Dictionary<string, object> a, out string result)
        {
            if (!OnWindows) { result = "керувати вікнами можна лише у Windows"; return false; }
            string title = Arg(a, "title"), action = Arg(a, "action").ToLowerInvariant(), target = Arg(a, "target"), text = Arg(a, "text");
            if (action.Length == 0) action = "click";
            var w = FindWin(title);
            if (w == null) { result = "вікно з назвою «" + title + "» не знайдено. Відкриті вікна програм агента: " + MyTitles(); return false; }
            if (!Mine(w)) { result = "«" + w.Title + "» — вікно користувача, відкрите ще до початку роботи. Агент керує лише програмами, які запустив сам."; return false; }

            string did;

            if (action == "close")
            {
                PostMessage(w.H, 0x0010, IntPtr.Zero, IntPtr.Zero);   // WM_CLOSE — без фокусу
                Thread.Sleep(900);
                result = IsWindow(w.H) && IsWindowVisible(w.H) ? "вікну надіслано команду закритися, але воно ще відкрите (можливо, питає підтвердження)" : "вікно «" + w.Title + "» закрито";
                var still = FindWin(title);
                if (still != null && Mine(still)) { ShotScreen(still); result += "." + ShotNote() + Elements(still); }
                return true;
            }

            if (action == "key")
            {
                if (text.Length == 0) text = target;
                if (!Front(w)) { result = NoFocus; return false; }
                SendKeys.SendWait(text);
                did = "натиснуто " + text;
            }
            else
            {
                // Куди: елемент за текстом (натискаємо через UI Automation, без мишки) або точка зі скріншота.
                El el = null;
                Point? at = null;
                string what = "";
                if (target.Length > 0)
                {
                    el = FindEl(w, target);
                    if (el == null)
                    {
                        result = "елемент «" + target + "» не знайдено у вікні «" + w.Title + "»." + Elements(w) +
                                 (ModelSeesImages ? "\nМожна також вказати x і y з останнього скріншота." : "");
                        return false;
                    }
                    at = new Point(el.R.X + el.R.Width / 2, el.R.Y + el.R.Height / 2);
                    what = (el.Type.Length > 0 ? el.Type + " " : "") + "«" + el.Name + "»";
                }
                else if (a.ContainsKey("x") && a.ContainsKey("y"))
                {
                    int x, y;
                    if (!Num(a["x"], out x) || !Num(a["y"], out y)) { result = "x і y мають бути числами"; return false; }
                    if (shotHwnd == IntPtr.Zero) { result = "спершу зроби скріншот вікна (screenshot_window), щоб координати було від чого рахувати"; return false; }
                    at = new Point(shotOrigin.X + (int)Math.Round(x / shotScale), shotOrigin.Y + (int)Math.Round(y / shotScale));
                    what = "точку " + x + "," + y;
                }
                else if (action != "type") { result = "вкажи target (текст кнопки, пункту меню чи поля) або x і y зі скріншота"; return false; }

                if (action == "type")
                {
                    // Спершу — вписати прямо в поле (без клавіатури). Клавіші — лише якщо вікно точно попереду.
                    if (el != null && UiaSetText(el.Obj, text)) { }
                    else
                    {
                        if (!Front(w)) { result = NoFocus; return false; }
                        if (at.HasValue && !ClickSafe(w, at.Value, false, false)) { result = "точка " + what + " не належить вікну програми — клік скасовано"; return false; }
                        Thread.Sleep(150);
                        if (!InFront(w)) { result = NoFocus; return false; }
                        SendKeys.SendWait(EscapeKeys(text));
                    }
                    did = "введено «" + (text.Length > 60 ? text.Substring(0, 60) + "…" : text) + "»" + (what.Length > 0 ? " у " + what : "");
                }
                else
                {
                    bool done = action == "click" && el != null && UiaPress(el.Obj);
                    if (!done)
                    {
                        if (!Front(w)) { result = NoFocus; return false; }
                        if (!ClickSafe(w, at.Value, action == "right_click", action == "double_click")) { result = "точка " + what + " не належить вікну програми — клік скасовано"; return false; }
                    }
                    did = (action == "double_click" ? "двічі клікнуто " : action == "right_click" ? "клікнуто правою кнопкою " : "натиснуто ") + what;
                }
            }

            Thread.Sleep(900);   // дати програмі відреагувати
            var now = FindWin(title) ?? (IsWindow(w.H) ? w : null);
            if (now == null || !IsWindowVisible(now.H))
            {
                result = did + ". Після цього вікно «" + w.Title + "» закрилось. Відкриті вікна програм агента: " + MyTitles();
                return true;
            }
            RECT r;
            if (GetWindowRect(now.H, out r)) now.R = Rectangle.FromLTRB(r.L, r.T, r.R, r.B);
            ShotScreen(now);
            result = did + ". Новий знімок вікна показано користувачу." + ShotNote() + Elements(now);
            return true;
        }

        string MyTitles()
        {
            var names = new List<string>();
            foreach (var w in TopWindows()) if (Mine(w) && w.Title.Length > 0 && !names.Contains("«" + w.Title + "»")) names.Add("«" + w.Title + "»");
            return names.Count == 0 ? "жодного" : string.Join(", ", names);
        }

        // ── повний проєкт ────────────────────────────────────────────────────────

        // Заглушки замість коду: «...», «// ...», «тут ваш код», «rest of the code» тощо.
        static readonly Regex Stub = new Regex(
            @"^[ \t]*(?:(?://|#|--|;|')[ \t]*)?(?:\.\.\.|…)[ \t]*$" +
            @"|^[ \t]*/\*[ \t]*(?:\.\.\.|…)[ \t]*\*/[ \t]*$" +
            @"|^[ \t]*<!--[ \t]*(?:\.\.\.|…)[ \t]*-->[ \t]*$" +
            @"|your code here|rest of (?:the )?(?:code|implementation|file)|(?:same|existing|previous) code|implement (?:this|me|here|the rest)" +
            @"|add (?:your|more|the) (?:code|logic|implementation)|code goes here|remaining (?:code|methods|functions)" +
            @"|тут (?:ваш|твій|буде) код|решта коду|(?:додайте|допишіть|реалізуйте) (?:свій|ваш|тут|самостійно|решту)|аналогічно для|і так далі для" +
            @"|TODO:?\s*(?:implement|реаліз|допис|додати)",
            RegexOptions.Multiline | RegexOptions.IgnoreCase | RegexOptions.Compiled);

        readonly Dictionary<string, string> stubFiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        int lastWrite = -1, lastCheck = -1, stepNo;
        bool askedCheck, askedStubs;

        // Перевірити щойно записаний файл. Повертає попередження для моделі або "".
        string CheckStubs(string path, string content)
        {
            string ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
            if (ext == ".md" || ext == ".txt" || ext == ".log" || ext == ".csv") { stubFiles.Remove(path); return ""; }
            var lines = new List<string>();
            foreach (Match m in Stub.Matches(content ?? ""))
            {
                int ln = 1;
                for (int i = 0; i < m.Index; i++) if (content[i] == '\n') ln++;
                string line = m.Value.Trim();
                lines.Add("рядок " + ln + ": «" + (line.Length > 50 ? line.Substring(0, 50) + "…" : line) + "»");
                if (lines.Count >= 5) break;
            }
            if (lines.Count == 0) { stubFiles.Remove(path); return ""; }
            stubFiles[path] = string.Join("; ", lines);
            return "\nУВАГА: у файлі заглушки замість коду (" + stubFiles[path] + "). Користувачу потрібен повністю робочий проєкт — перепиши файл цілком, без пропусків і «...».";
        }

        void NoteTool(string name, bool ok)
        {
            stepNo++;
            if (name == "create_file" || name == "delete_file") lastWrite = stepNo;
            else if (name == "run_command" || name == "start_program" || name == "screenshot_page" || name == "screenshot_window" || name == "window_action") lastCheck = stepNo;
        }

        // Модель хоче завершити. Якщо проєкт не перевірено або є заглушки — один раз повернути її до роботи.
        string BeforeFinish(out string note)
        {
            note = null;
            if (stubFiles.Count > 0 && !askedStubs)
            {
                askedStubs = true;
                var sb = new StringBuilder("Ще не готово: у цих файлах заглушки замість коду:");
                foreach (var kv in stubFiles) sb.Append("\n- ").Append(kv.Key).Append(" (").Append(kv.Value).Append(")");
                sb.Append("\nПерепиши кожен з них повністю, з усім кодом, потім перевір, що проєкт запускається, і тоді коротко підсумуй.");
                note = "У файлах лишились заглушки замість коду — агент дописує їх повністю.";
                return sb.ToString();
            }
            if (lastWrite > lastCheck && !askedCheck)
            {
                askedCheck = true;
                note = "Проєкт ще не перевірено — агент запускає його й виправляє помилки.";
                return "Перш ніж завершити: ти ще не перевірив проєкт після останніх змін. Запусти його — run_command для скриптів і тестів, " +
                       "start_program для програми з вікном (потім window_action, щоб натиснути головні кнопки), screenshot_page для сайту. " +
                       "Виправ помилки, які побачиш, і тоді коротко підсумуй, що зроблено й як це запустити.";
            }
            return null;
        }

        const string FullProjectRules =
            "\n\nRULES FOR A COMPLETE PROJECT (the user wants a finished, working project, not an example):\n" +
            "- Write EVERY file in full with create_file. Never use placeholders like \"...\", \"// rest of the code\", \"TODO: implement\", " +
            "\"your code here\" or \"similar for the other cases\". If a file is long, still write all of it.\n" +
            "- Implement every feature the user asked for; do not leave buttons, menus or functions that do nothing.\n" +
            "- Create everything needed to run it: dependencies file (requirements.txt / package.json), a README with how to run, sample data if needed.\n" +
            "- Then CHECK it: run scripts and tests with run_command; start programs with a window via start_program (you get a screenshot and " +
            "the list of buttons), press the main buttons with window_action and look at the result; check web pages with screenshot_page.\n" +
            "- If something fails, fix it and check again. Only then write a short summary: what was made and how to run it.";
    }
}
