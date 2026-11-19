// Open Forge — стрічка повідомлень чату (і колонка відповіді в «Порівнянні»).
// Чому окремий елемент, а не купа Label/TextBox:
// • текст розкладається на рядки нами самими: кожен абзац один раз і запам'ятовується, тож під час відповіді
//   перемірюється лише останній абзац, а малюються лише видимі рядки — навіть величезна відповідь не гальмує;
// • висота всього вмісту відома завжди, тому прокрутка доходить до кінця, а якщо ти внизу — стрічка їде за текстом;
// • оскільки ми знаємо, де стоїть кожна літера, текст можна виділяти мишею (і копіювати Ctrl+C);
// • код між ``` показується окремим блоком моноширинним шрифтом з кнопкою «Копіювати».
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
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
        public string Folder;        // папка агента (кнопка «Відкрити папку»)
        public double Tag;           // швидкість (для «Порівняння»)
        public List<string> Images;  // прикріплені картинки (JPEG base64)
        public List<FileAttachment> Files;   // прикріплені файли (їхній текст іде моделі)
        internal List<Bitmap> Thumbs;
        public bool Streaming;
        public bool StepsOpen;       // кроки агента розгорнуті
        public bool ThinkOpen;       // міркування моделі розгорнуті
        public double ThinkSec;      // скільки модель міркувала (0 — невідомо)
        public DateTime StartedAt = DateTime.UtcNow;
        internal string Site;        // index.html, який зробив агент (для «Переглянути сайт»)
        internal bool SiteChecked;

        internal string Shown, LayoutDisp;
        internal int LayoutW = -1, Height, BubbleW;
        internal bool LayoutFoot, LayoutSteps, LayoutThinkOpen;
        internal int LayoutImages, LayoutThink = -1, LayoutFiles;
        internal readonly List<Para> Paras = new List<Para>();

        public ChatMessage(string role, string raw) { Role = role; Raw = raw ?? ""; }

        public bool IsUser { get { return Role == "user"; } }

        // Текст для показу: без міркувань моделі (<think>…</think>), без табуляцій і зайвих порожніх рядків на краях.
        public string Display
        {
            get
            {
                string s = Raw;
                if (!IsUser && s.IndexOf("<think>", StringComparison.Ordinal) >= 0) s = StripThink(s);
                return s.Replace("\r", "").Replace("\t", "    ").Trim('\n', ' ');
            }
        }

        // Міркування моделі: текст між <think> і </think> (або до кінця, якщо вона ще думає).
        public string Think
        {
            get
            {
                string s = Raw;
                if (IsUser || s.IndexOf("<think>", StringComparison.Ordinal) < 0) return "";
                var sb = new StringBuilder();
                int i = 0;
                while (true)
                {
                    int a = s.IndexOf("<think>", i, StringComparison.Ordinal);
                    if (a < 0) break;
                    int b = s.IndexOf("</think>", a, StringComparison.Ordinal);
                    if (sb.Length > 0) sb.Append('\n');
                    sb.Append(b < 0 ? s.Substring(a + 7) : s.Substring(a + 7, b - a - 7));
                    if (b < 0) break;
                    i = b + 8;
                }
                return sb.ToString().Replace("\r", "").Trim('\n', ' ');
            }
        }

        // Модель саме зараз міркує (є <think> без </think>).
        public bool InThink
        {
            get
            {
                int a = Raw.LastIndexOf("<think>", StringComparison.Ordinal);
                return a >= 0 && Raw.IndexOf("</think>", a, StringComparison.Ordinal) < 0;
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

    // Один рядок вихідного тексту (або шматок дуже довгого рядка), розкладений на рядки екрана.
    class Para
    {
        public const int Text = 0, CodeLine = 1, CodeHead = 2, CodeFoot = 3, Steps = 4, Images = 5, Think = 6, Table = 7, Rule = 8, Files = 9;
        public int Kind;
        public bool Step;                            // рядок зі списку кроків агента (з відступом, блідий)
        public bool Thought;                         // рядок міркувань моделі (блідий, не виділяється)
        public bool Quote;                           // цитата «> …» (смужка ліворуч)
        public int Fi;                               // шрифт абзацу (див. MessageList.F)
        public byte[] Sty;                           // шрифт кожної літери (жирний, `код`) або null — увесь абзац шрифтом Fi
        public int Indent, Hang, Pad;                // відступ ліворуч, відступ рядків-продовжень (списки), проміжок зверху
        public List<string[]> Rows;                  // для Table: рядки таблиці (перший — заголовок)
        public int[] ColWs, RowHs;
        public int TextH { get { return H - Pad; } }
        public List<string> StepList;                // для Steps: усі кроки групи
        public string Summary;                       // для Steps: підсумок «Створив файлів: 3 · …»
        public bool Live;                            // «Думає над відповіддю» — поки модель ще нічого не написала
        public string Str = "";
        public int Start;                            // позиція в ChatMessage.Shown
        public List<int> Lines = new List<int> { 0 };  // початки рядків екрана в Str
        public int H, W;
        public string Code, Lang;                    // для заголовка блоку коду
    }

    class MessageList : UiControl, IWheel
    {
        public readonly List<ChatMessage> Items = new List<ChatMessage>();
        public string EmptyTitle = "", EmptyText = "";
        public bool Compact;          // вузька колонка (Порівняння): без центрування, без великої заставки
        int offset, total;
        int dragFrom = -1, dragOffset;

        // Виділення: (номер повідомлення, позиція в його Shown).
        int selAM = -1, selAC, selBM = -1, selBC;
        bool selecting;
        readonly Timer autoScroll = new Timer { Interval = 40 };
        int autoDy;

        object copied;                // що щойно скопійовано (повідомлення або блок коду) — підпис «Скопійовано»
        readonly Timer copiedTimer = new Timer { Interval = 1600 };
        object hoverObj;
        string hoverKey;
        readonly ToolTip tip = new ToolTip { InitialDelay = 400 };

        const TextFormatFlags NF = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;
        const int ChunkChars = 2000;   // дуже довгі рядки ріжемо на шматки — так їх швидше міряти
        readonly Dictionary<string, int>[] widths = { new Dictionary<string, int>(), new Dictionary<string, int>(), new Dictionary<string, int>(), new Dictionary<string, int>(), new Dictionary<string, int>(), new Dictionary<string, int>() };

        public MessageList()
        {
            OnBg = true;
            copiedTimer.Tick += (s, e) => { copiedTimer.Stop(); copied = null; Invalidate(); };
            autoScroll.Tick += (s, e) =>
            {
                if (!selecting || autoDy == 0) { autoScroll.Stop(); return; }
                ScrollTo(offset + autoDy);
                ExtendSelection(PointToClient(Cursor.Position));
            };

            var menu = new ContextMenuStrip();
            var copySel = new ToolStripMenuItem("Копіювати");
            var copyMsg = new ToolStripMenuItem("Копіювати повідомлення");
            var all = new ToolStripMenuItem("Виділити все");
            var sep = new ToolStripSeparator();
            var edit = new ToolStripMenuItem("Редагувати");
            var del = new ToolStripMenuItem("Видалити повідомлення");
            menu.Items.AddRange(new ToolStripItem[] { copySel, copyMsg, all, sep, edit, del });
            ChatMessage target = null;
            menu.Opening += (s, e) =>
            {
                target = HitMessage(PointToClient(Cursor.Position));
                copySel.Enabled = HasSelection;
                copyMsg.Enabled = target != null && target.Display.Length > 0;
                all.Enabled = Items.Count > 0;
                bool own = Editable && target != null && !target.Streaming;
                sep.Visible = edit.Visible = del.Visible = Editable;
                edit.Enabled = own && target.IsUser;
                del.Enabled = own;
                del.Text = target != null && target.IsUser ? "Видалити повідомлення (і відповідь на нього)" : "Видалити повідомлення";
            };
            edit.Click += (s, e) => { if (target != null && EditMessage != null) EditMessage(target); };
            del.Click += (s, e) => { if (target != null && DeleteMessage != null) DeleteMessage(target); };
            copySel.Click += (s, e) => CopySelection();
            copyMsg.Click += (s, e) => { if (target != null) CopyToClipboard(target.Display, target); };
            all.Click += (s, e) => SelectAll();
            ContextMenuStrip = menu;
        }

        // ── розміри ───────────────────────────────────────────────────────────────

        int Gutter { get { return Ui.S(Compact ? 10 : 16); } }
        int ColW { get { return Compact ? Math.Max(Ui.S(80), Width - Ui.S(26)) : Math.Max(Ui.S(200), Math.Min(Ui.S(760), Width - 2 * Gutter - Ui.S(12))); } }
        int ColX { get { return Compact ? Ui.S(10) : (Width - Ui.S(12) - ColW) / 2 + Ui.S(6); } }
        int TopPad { get { return Ui.S(Compact ? 8 : 20); } }
        int PadX { get { return Ui.S(14); } }
        int PadY { get { return Ui.S(9); } }
        int CodePad { get { return Ui.S(14); } }
        int Gap { get { return Ui.S(18); } }
        int FootH { get { return Ui.S(26); } }
        int MaxOffset { get { return Math.Max(0, total - Height); } }
        public bool AtBottom { get { return offset >= MaxOffset - Ui.S(8); } }

        // Підвал під повідомленням: під відповіддю — завжди, під твоїм — місце для «Редагувати / Видалити» (видно при наведенні).
        bool HasFoot(ChatMessage m) { return (m.Role == "assistant" && !m.Streaming && m.Display.Length > 0) || (m.IsUser && Editable && !Compact); }

        public bool Editable;            // у чаті: можна редагувати й видаляти повідомлення, «Ще раз»
        ChatMessage hoverMsg;            // над яким повідомленням миша (для кнопок під твоїм повідомленням)

        // Остання відповідь моделі в розмові — під нею кнопка «Ще раз».
        bool IsLastAnswer(ChatMessage m)
        {
            for (int i = Items.Count - 1; i >= 0; i--)
            {
                if (Items[i].Role == "user") return false;
                if (Items[i].Role == "assistant") return Items[i] == m;
            }
            return false;
        }

        int TextW(ChatMessage m)
        {
            return m.IsUser ? (int)(ColW * (Compact ? 0.9f : 0.8f)) - 2 * PadX : m.Role == "error" ? ColW - 2 * PadX : ColW;
        }

        // ── вимірювання тексту ───────────────────────────────────────────────────

        // Шрифти: 0 — звичайний, 1 — код, 2 — жирний, 3 і 4 — заголовки, 5 — `код` усередині тексту.
        static Font h1Font, h2Font, inlineMono;
        internal static Font F(int fi)
        {
            switch (fi)
            {
                case 1: return Ui.Mono;
                case 2: return Ui.ChatSemi;
                case 3: return h1Font ?? (h1Font = new Font("Segoe UI Semibold", 14.5f));
                case 4: return h2Font ?? (h2Font = new Font("Segoe UI Semibold", 12.5f));
                case 5: return inlineMono ?? (inlineMono = new Font("Consolas", 9.75f));
                default: return Ui.Chat;
            }
        }

        readonly int[] lineHs = new int[6];
        int LineHF(int fi)
        {
            if (lineHs[fi] == 0) lineHs[fi] = TextRenderer.MeasureText("Ag", F(fi), Size.Empty, NF).Height + (fi == 1 ? Ui.S(1) : 0);
            return lineHs[fi];
        }
        int LineH(int kind) { return LineHF(kind == Para.CodeLine ? 1 : 0); }
        static int FiOf(Para p) { return p.Kind == Para.CodeLine ? 1 : p.Fi; }
        int LH(Para p) { int h = LineHF(FiOf(p)); return p.Sty != null ? Math.Max(h, LineHF(0)) : h; }
        int CodePadX { get { return Ui.S(3); } }   // поля навколо `коду` в тексті

        int TokW(string s, int fi)
        {
            int v;
            var c = widths[fi];
            if (c.TryGetValue(s, out v)) return v;
            if (c.Count > 50000) c.Clear();
            v = TextRenderer.MeasureText(s, F(fi), Size.Empty, NF).Width;
            c[s] = v;
            return v;
        }

        // Ширина рядка з урахуванням пробілів у кінці (MeasureText їх не рахує).
        int PrefixW(string s, int fi)
        {
            if (s.Length == 0) return 0;
            return TextRenderer.MeasureText(s + "|", F(fi), Size.Empty, NF).Width - TokW("|", fi);
        }

        int SpaceW(int fi) { return TokW("x x", fi) - TokW("xx", fi); }

        // Ширина p.Str[a..b) з урахуванням жирного й `коду`.
        int W(Para p, int a, int b)
        {
            if (b <= a) return 0;
            if (p.Sty == null) return PrefixW(p.Str.Substring(a, b - a), FiOf(p));
            int w = 0;
            for (int i = a; i < b; )
            {
                int j = i;
                byte st = p.Sty[i];
                while (j < b && p.Sty[j] == st) j++;
                w += PrefixW(p.Str.Substring(i, j - i), st) + (st == 5 ? 2 * CodePadX : 0);
                i = j;
            }
            return w;
        }

        int WordW(Para p, int a, int b)
        {
            if (b <= a) return 0;
            if (p.Sty == null) return TokW(p.Str.Substring(a, b - a), FiOf(p));
            byte st = p.Sty[a];
            for (int i = a + 1; i < b; i++) if (p.Sty[i] != st) return W(p, a, b);
            return TokW(p.Str.Substring(a, b - a), st) + (st == 5 ? 2 * CodePadX : 0);
        }

        // Розкласти рядок на рядки екрана шириною w (за словами; задовге слово ріжеться по літерах).
        void Wrap(Para p, int w)
        {
            p.Lines = new List<int> { 0 };
            string t = p.Str;
            int n = t.Length, i = 0, lineW = 0, maxW = 0, sp1 = SpaceW(FiOf(p));
            while (i < n)
            {
                int j = i;
                while (j < n && t[j] == ' ') j++;
                int k = j;
                while (k < n && t[k] != ' ') k++;
                int sw = (j - i) * sp1;
                int ww = WordW(p, j, k);
                if (lineW + sw + ww <= w) { lineW += sw + ww; i = k; maxW = Math.Max(maxW, lineW); continue; }
                if (ww <= w && lineW > 0)
                {
                    p.Lines.Add(j);   // слово — з нового рядка, пробіли перед ним лишаються в кінці попереднього
                    lineW = ww;
                    i = k;
                    maxW = Math.Max(maxW, lineW);
                    continue;
                }
                // Задовге слово: ріжемо по літерах.
                int pos = j, avail = w - lineW - (lineW > 0 ? sw : 0);
                while (pos < k)
                {
                    int c = Fit(p, pos, k - pos, avail);
                    if (c == 0)
                    {
                        if (pos == p.Lines[p.Lines.Count - 1]) c = 1;
                        else { p.Lines.Add(pos); avail = w; continue; }
                    }
                    if (pos + c >= k) { lineW = w - avail + W(p, pos, pos + c); break; }
                    pos += c;
                    p.Lines.Add(pos);
                    avail = w;
                }
                maxW = Math.Max(maxW, Math.Min(w, lineW));
                i = k;
            }
            p.W = maxW;
        }

        // Скільки літер з p.Str[pos..pos+len) влазить у ширину avail.
        int Fit(Para p, int pos, int len, int avail)
        {
            int lo = 0, hi = len;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) / 2;
                if (W(p, pos, pos + mid) <= avail) lo = mid; else hi = mid - 1;
            }
            return lo;
        }

        void Measure(ChatMessage m)
        {
            int w = TextW(m);
            string disp = m.Display;
            string think = m.Role == "assistant" ? m.Think : "";
            bool foot = HasFoot(m);
            int nImg = m.Images != null ? m.Images.Count : 0, nFiles = m.Files != null ? m.Files.Count : 0;
            int thinkKey = think.Length * 2 + (m.Streaming && m.InThink ? 1 : 0);
            if (disp == m.LayoutDisp && w == m.LayoutW && foot == m.LayoutFoot && m.StepsOpen == m.LayoutSteps && nImg == m.LayoutImages &&
                thinkKey == m.LayoutThink && m.ThinkOpen == m.LayoutThinkOpen && nFiles == m.LayoutFiles) return;
            m.LayoutImages = nImg;
            m.LayoutFiles = nFiles;
            bool waiting = disp.Length == 0 && m.Streaming && think.Length == 0;   // модель ще нічого не написала

            // 1. Розбити на абзаци: рядки тексту, блоки коду між ```, таблиці, заголовки, списки (лише у відповідях моделі).
            var paras = new List<Para>();
            int imagesW = 0;
            Para imagesPara = null;
            if (m.Images != null && m.Images.Count > 0)
            {
                if (m.Thumbs != null && m.Thumbs.Count != m.Images.Count) { foreach (var t in m.Thumbs) t.Dispose(); m.Thumbs = null; }
                if (m.Thumbs == null)
                {
                    m.Thumbs = new List<Bitmap>();
                    foreach (string b in m.Images)
                    {
                        var img = Pictures.Decode(b);
                        if (img == null) continue;
                        using (img) m.Thumbs.Add(Pictures.Thumb(img, Ui.S(110), Ui.S(220)));
                    }
                }
                foreach (var t in m.Thumbs) imagesW += t.Width + Ui.S(6);
                imagesPara = new Para { Kind = Para.Images, Str = "", Start = 0 };
                if (m.IsUser) paras.Add(imagesPara);   // у твоєму повідомленні — над текстом
            }
            if (nFiles > 0) paras.Add(new Para { Kind = Para.Files, Str = "", Start = 0 });

            // Міркування — згорнутий рядок «Думав 12 с» над відповіддю; клік розгортає текст.
            if (think.Length > 0)
            {
                paras.Add(new Para { Kind = Para.Think, Str = "", Start = 0 });
                if (m.ThinkOpen)
                    foreach (string tl in think.Split('\n'))
                        paras.Add(new Para { Kind = Para.Text, Str = tl.TrimEnd(), Start = 0, Thought = true, Indent = StepIndent });
                if (disp.Length > 0) paras.Add(new Para { Kind = Para.Rule, Str = "", Start = 0, H = Ui.S(6), Pad = -1 });   // маленький проміжок
            }

            bool md = m.Role == "assistant";
            bool inCode = false;
            Para head = null, steps = null;
            var code = new StringBuilder();
            var shown = new StringBuilder(disp.Length);
            string[] lines = disp.Length > 0 ? disp.Split('\n') : new string[0];
            for (int li = 0; li < lines.Length; li++)
            {
                string line = lines[li];
                int start = shown.Length;
                string trimmed = line.TrimStart();
                // Кроки агента («▸ …») згортаються в один рядок-підсумок; клік розгортає список.
                if (md && !inCode && trimmed.StartsWith("▸ "))
                {
                    string st = trimmed.Substring(2);
                    shown.Append(st).Append('\n');
                    if (steps == null)
                    {
                        steps = new Para { Kind = Para.Steps, Str = "", Start = start, StepList = new List<string>() };
                        paras.Add(steps);
                    }
                    steps.StepList.Add(st);
                    if (m.StepsOpen) paras.Add(new Para { Kind = Para.Text, Str = st, Start = start, Step = true, Indent = StepIndent });
                    continue;
                }
                if (trimmed.Length > 0) steps = null;
                if (md && trimmed.StartsWith("```"))
                {
                    shown.Append(line).Append('\n');
                    if (!inCode)
                    {
                        head = new Para { Kind = Para.CodeHead, Str = "", Start = start, Lang = trimmed.Substring(3).Trim() };
                        paras.Add(head);
                        code.Length = 0;
                        inCode = true;
                    }
                    else
                    {
                        head.Code = code.ToString().TrimEnd('\n');
                        paras.Add(new Para { Kind = Para.CodeFoot, Str = "", Start = start });
                        inCode = false;
                    }
                    continue;
                }
                if (inCode)
                {
                    code.Append(line).Append('\n');
                    shown.Append(line).Append('\n');
                    AddText(paras, new Para { Kind = Para.CodeLine, Fi = 1 }, line, null, start);
                    continue;
                }
                if (!md)
                {
                    shown.Append(line).Append('\n');
                    AddText(paras, new Para { Kind = Para.Text }, line, null, start);
                    continue;
                }

                // Таблиця: рядки з «|», другий — роздільник «|---|---|».
                if (trimmed.StartsWith("|") && li + 1 < lines.Length && IsTableSep(lines[li + 1]))
                {
                    var rows = new List<string[]>();
                    int j = li;
                    while (j < lines.Length && lines[j].TrimStart().StartsWith("|"))
                    {
                        if (j != li + 1) rows.Add(Cells(lines[j]));
                        shown.Append(lines[j]).Append('\n');
                        j++;
                    }
                    paras.Add(new Para { Kind = Para.Table, Str = "", Start = start, Rows = rows, Pad = paras.Count > 0 ? Ui.S(6) : 0 });
                    li = j - 1;
                    continue;
                }
                if (IsRule(trimmed)) { shown.Append('\n'); paras.Add(new Para { Kind = Para.Rule, Str = "", Start = start }); continue; }

                var proto = new Para { Kind = Para.Text };
                string body = line;
                int hl = 0;
                while (hl < trimmed.Length && hl < 6 && trimmed[hl] == '#') hl++;
                int lead = line.Length - trimmed.Length;
                if (hl > 0 && hl < trimmed.Length && trimmed[hl] == ' ')
                {
                    body = trimmed.Substring(hl + 1).Trim().TrimEnd('#').TrimEnd();
                    proto.Fi = hl == 1 ? 3 : hl == 2 ? 4 : 2;
                    proto.Pad = paras.Count == 0 ? 0 : Ui.S(proto.Fi == 2 ? 6 : 10);
                }
                else if (trimmed.StartsWith("> ") || trimmed == ">")
                {
                    body = trimmed.Substring(1).TrimStart();
                    proto.Quote = true;
                    proto.Indent = Ui.S(14);
                }
                else if (trimmed.Length > 1 && (trimmed[0] == '-' || trimmed[0] == '*' || trimmed[0] == '+') && trimmed[1] == ' ')
                {
                    body = "•  " + trimmed.Substring(2).TrimStart();
                    proto.Indent = Ui.S(6) + Math.Min(3, lead / 2) * Ui.S(20);
                    proto.Hang = TokW("•", 0) + 2 * SpaceW(0);
                }
                else
                {
                    var nm = System.Text.RegularExpressions.Regex.Match(trimmed, @"^\d{1,3}[.)] ");
                    if (nm.Success)
                    {
                        body = trimmed;
                        proto.Indent = Ui.S(6) + Math.Min(3, lead / 2) * Ui.S(20);
                        proto.Hang = PrefixW(nm.Value, 0);
                    }
                }
                byte[] sty;
                string clean = Clean(body, proto.Fi, out sty);
                shown.Append(clean).Append('\n');
                AddText(paras, proto, clean, sty, start);
            }
            if (imagesPara != null && !m.IsUser) paras.Add(imagesPara);   // скріншоти агента — під текстом
            if (waiting)
            {
                paras.Clear();
                paras.Add(new Para { Kind = Para.Steps, Str = "", Start = 0, Live = true, StepList = new List<string>(), Summary = "Думає над відповіддю" });
            }

            // Підсумки груп кроків.
            Para lastSteps = null;
            foreach (var p in paras) if (p.Kind == Para.Steps && !p.Live) { p.Summary = StepsSummary(p.StepList); lastSteps = p; }
            if (m.Streaming && lastSteps != null && lastSteps.StepList.Count > 0)
            {
                string cur = lastSteps.StepList[lastSteps.StepList.Count - 1];
                int mark = cur.IndexOf("  ");
                lastSteps.Summary = "Працює: " + (mark > 0 ? cur.Substring(0, mark) : cur);
            }
            if (inCode)   // блок ще пишеться — закриваємо його візуально
            {
                head.Code = code.ToString().TrimEnd('\n');
                paras.Add(new Para { Kind = Para.CodeFoot, Str = "", Start = shown.Length });
            }

            // 2. Розкласти на рядки екрана; незмінні абзаци беремо з минулого разу.
            var old = new Dictionary<string, Para>();
            if (w == m.LayoutW)
                foreach (var p in m.Paras)
                    if (p.Kind <= Para.CodeLine && p.Str.Length > 0) old[Key(p)] = p;
            m.Paras.Clear();
            int h = 0, maxW = 0;
            foreach (var p in paras)
            {
                if (p.Kind == Para.CodeHead) p.H = Ui.S(34);
                else if (p.Kind == Para.CodeFoot) p.H = Ui.S(20);
                else if (p.Kind == Para.Steps || p.Kind == Para.Think) p.H = Ui.S(36);
                else if (p.Kind == Para.Images) { p.H = Ui.S(110) + Ui.S(8); maxW = Math.Max(maxW, imagesW); }
                else if (p.Kind == Para.Files) { LayoutFiles(m, p, w); maxW = Math.Max(maxW, p.W); }
                else if (p.Kind == Para.Rule) { if (p.Pad < 0) p.Pad = 0; else p.H = Ui.S(20); }
                else if (p.Kind == Para.Table) { LayoutTable(p, w); maxW = Math.Max(maxW, p.W); }
                else
                {
                    int lw = p.Kind == Para.CodeLine ? w - 2 * CodePad : w - p.Indent - p.Hang;
                    Para prev;
                    string key = Key(p);
                    if (p.Str.Length > 0 && old.TryGetValue(key, out prev)) { p.Lines = prev.Lines; p.W = prev.W; }
                    else if (p.Str.Length > 0) Wrap(p, Math.Max(Ui.S(20), lw));
                    p.H = p.Pad + p.Lines.Count * LH(p);
                    maxW = Math.Max(maxW, p.W + p.Indent + p.Hang + (p.Kind == Para.CodeLine ? 2 * CodePad : 0));
                }
                m.Paras.Add(p);
                h += p.H;
            }
            if (shown.Length > 0) shown.Length--;   // без останнього переносу
            m.Shown = shown.ToString();
            m.LayoutDisp = disp;
            m.LayoutW = w;
            m.LayoutFoot = foot;
            m.LayoutSteps = m.StepsOpen;
            m.LayoutThink = thinkKey;
            m.LayoutThinkOpen = m.ThinkOpen;
            bool bubble = m.Role != "assistant";
            m.BubbleW = bubble ? Math.Min(w, maxW) + 2 * PadX + Ui.S(2) : w;
            m.Height = h + (bubble ? 2 * PadY : 0) + (foot ? FootH + Ui.S(4) : 0);
        }

        static string Key(Para p)
        {
            string k = p.Kind + "\u0001" + p.Fi + "\u0001" + p.Indent + "\u0001" + p.Hang + "\u0001" + (p.Thought ? "t" : p.Step ? "s" : "") + "\u0001" + p.Str;
            if (p.Sty != null) { var c = new char[p.Sty.Length]; for (int i = 0; i < c.Length; i++) c[i] = (char)('0' + p.Sty[i]); k += "\u0001" + new string(c); }
            return k;
        }

        // Додати рядок як абзац (дуже довгі ріжемо на шматки — так їх швидше міряти).
        static void AddText(List<Para> paras, Para proto, string t, byte[] sty, int start)
        {
            int off = 0;
            do
            {
                int len = t.Length - off;
                if (len > ChunkChars)
                {
                    int cut = t.LastIndexOf(' ', off + ChunkChars, ChunkChars);
                    len = cut > off + ChunkChars / 2 ? cut - off : ChunkChars;
                }
                var p = new Para
                {
                    Kind = proto.Kind, Fi = proto.Fi, Quote = proto.Quote, Indent = proto.Indent, Hang = off == 0 ? proto.Hang : 0, Pad = off == 0 ? proto.Pad : 0,
                    Str = t.Substring(off, len), Start = start + off,
                };
                if (sty != null) { p.Sty = new byte[len]; Array.Copy(sty, off, p.Sty, 0, len); }
                paras.Add(p);
                off += len;
            } while (off < t.Length);
        }

        // Прибрати розмітку **жирного** й `коду`, запам'ятавши шрифт кожної літери.
        static string Clean(string s, int fi, out byte[] sty)
        {
            sty = null;
            if (s.IndexOf("**", StringComparison.Ordinal) < 0 && s.IndexOf('`') < 0) return s;
            var sb = new StringBuilder(s.Length);
            var st = new List<byte>(s.Length);
            bool bold = false, any = false;
            byte boldFi = (byte)(fi == 0 ? 2 : fi);
            int i = 0;
            while (i < s.Length)
            {
                if (s[i] == '`')
                {
                    int e = s.IndexOf('`', i + 1);
                    if (e > i + 1)
                    {
                        for (int k = i + 1; k < e; k++) { sb.Append(s[k]); st.Add(5); }
                        i = e + 1;
                        any = true;
                        continue;
                    }
                }
                if (i + 1 < s.Length && s[i] == '*' && s[i + 1] == '*' && (bold || s.IndexOf("**", i + 2, StringComparison.Ordinal) > i + 2))
                {
                    bold = !bold;
                    i += 2;
                    any = true;
                    continue;
                }
                sb.Append(s[i]);
                st.Add(bold ? boldFi : (byte)fi);
                i++;
            }
            if (!any) return s;
            bool plain = true;
            foreach (byte b in st) if (b != fi) { plain = false; break; }
            if (!plain) sty = st.ToArray();
            return sb.ToString();
        }

        static bool IsTableSep(string line)
        {
            string t = line.Trim();
            return t.StartsWith("|") && t.Contains("-") && System.Text.RegularExpressions.Regex.IsMatch(t, @"^\|?(\s*:?-+:?\s*\|)+\s*(:?-+:?\s*)?\|?$");
        }

        static string[] Cells(string line)
        {
            string t = line.Trim();
            if (t.StartsWith("|")) t = t.Substring(1);
            if (t.EndsWith("|")) t = t.Substring(0, t.Length - 1);
            var cells = t.Split('|');
            for (int i = 0; i < cells.Length; i++) cells[i] = cells[i].Trim().Replace("**", "").Replace("`", "").Replace("<br>", " ");
            return cells;
        }

        static bool IsRule(string t)
        {
            t = t.Trim();
            if (t.Length < 3) return false;
            char c = t[0];
            if (c != '-' && c != '*' && c != '_') return false;
            foreach (char x in t) if (x != c && x != ' ') return false;
            return true;
        }

        int CellPadX { get { return Ui.S(10); } }
        int CellPadY { get { return Ui.S(6); } }
        const TextFormatFlags CellFlags = TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;

        // Ширини колонок (природні, а якщо не влазять — ділимо ширину порівну між широкими) і висоти рядків.
        void LayoutTable(Para p, int w)
        {
            int n = 1;
            foreach (var r in p.Rows) n = Math.Max(n, r.Length);
            var nat = new int[n];
            for (int ri = 0; ri < p.Rows.Count; ri++)
                for (int c = 0; c < p.Rows[ri].Length; c++)
                    nat[c] = Math.Max(nat[c], TextRenderer.MeasureText(p.Rows[ri][c], ri == 0 ? Ui.ChatSemi : Ui.Chat, Size.Empty, NF).Width + 2 * CellPadX + Ui.S(2));
            var cols = new int[n];
            var open = new List<int>();
            for (int c = 0; c < n; c++) open.Add(c);
            int left = w;
            while (open.Count > 0)
            {
                int share = left / open.Count;
                var fit = open.Where(c => nat[c] <= share).ToList();
                if (fit.Count == 0) { foreach (int c in open) cols[c] = Math.Max(Ui.S(56), share); break; }
                foreach (int c in fit) { cols[c] = nat[c]; left -= nat[c]; open.Remove(c); }
            }
            p.ColWs = cols;
            p.RowHs = new int[p.Rows.Count];
            int h = 0;
            for (int ri = 0; ri < p.Rows.Count; ri++)
            {
                int rh = LineHF(0);
                for (int c = 0; c < p.Rows[ri].Length && c < n; c++)
                    if (p.Rows[ri][c].Length > 0)
                        rh = Math.Max(rh, TextRenderer.MeasureText(p.Rows[ri][c], ri == 0 ? Ui.ChatSemi : Ui.Chat, new Size(Math.Max(Ui.S(10), cols[c] - 2 * CellPadX), 100000), CellFlags).Height);
                p.RowHs[ri] = rh + 2 * CellPadY;
                h += p.RowHs[ri];
            }
            p.W = cols.Sum();
            p.H = p.Pad + h + Ui.S(10);
        }

        // Значки прикріплених файлів: «назва» в рамці, у кілька рядків, якщо не влазять.
        List<Rectangle> FileChips(ChatMessage m, int x, int y, int w)
        {
            var res = new List<Rectangle>();
            int cx = x, cy = y, ch = Ui.S(30);
            foreach (var f in m.Files)
            {
                int cw = Math.Min(w, TextRenderer.MeasureText(f.Name, Ui.Small, Size.Empty, NF).Width + Ui.S(40));
                if (cx > x && cx + cw > x + w) { cx = x; cy += ch + Ui.S(6); }
                res.Add(new Rectangle(cx, cy, cw, ch));
                cx += cw + Ui.S(6);
            }
            return res;
        }

        void LayoutFiles(ChatMessage m, Para p, int w)
        {
            var chips = FileChips(m, 0, 0, w);
            p.W = chips.Max(r => r.Right);
            p.H = chips.Max(r => r.Bottom) + Ui.S(8);
        }

        int StepIndent { get { return Ui.S(22); } }

        // «Створив файлів: 3 · виконав команд: 1 · помилок: 1»
        static string StepsSummary(List<string> list)
        {
            int create = 0, read = 0, look = 0, cmd = 0, del = 0, fail = 0, other = 0, shot = 0, run = 0;
            foreach (string st in list)
            {
                if (st.Contains("✗")) fail++;
                if (st.StartsWith("Створює файл")) create++;
                else if (st.StartsWith("Читає")) read++;
                else if (st.StartsWith("Дивиться файли")) look++;
                else if (st.StartsWith("Виконує команду")) cmd++;
                else if (st.StartsWith("Видаляє")) del++;
                else if (st.StartsWith("Робить скріншот") || st.StartsWith("Фотографує вікно")) shot++;
                else if (st.StartsWith("Запускає програму")) run++;
                else other++;
            }
            var parts = new List<string>();
            if (create > 0) parts.Add("створив файлів: " + create);
            if (read > 0) parts.Add("прочитав: " + read);
            if (look > 0) parts.Add("переглянув папку: " + look);
            if (cmd > 0) parts.Add("виконав команд: " + cmd);
            if (del > 0) parts.Add("видалив: " + del);
            if (run > 0) parts.Add("запустив програм: " + run);
            if (shot > 0) parts.Add("скріншотів: " + shot);
            if (other > 0) parts.Add("інших кроків: " + other);
            if (fail > 0) parts.Add("не вдалося: " + fail);
            string s = string.Join(" · ", parts.ToArray());
            return s.Length > 0 ? char.ToUpper(s[0]) + s.Substring(1) : "Кроки";
        }

        void Recount()
        {
            int y = TopPad;
            foreach (var m in Items) { Measure(m); y += m.Height + Gap; }
            total = y + Ui.S(4);
            if (selAM >= Items.Count || selBM >= Items.Count) ClearSelection();
        }

        // ── зміни ззовні ─────────────────────────────────────────────────────────

        public void Add(ChatMessage m)
        {
            Items.Add(m);
            Recount();
            ScrollTo(MaxOffset);
        }

        public void Remove(ChatMessage m)
        {
            Items.Remove(m);
            ClearSelection();
            Recount();
            ScrollTo(offset);
        }

        public void Clear()
        {
            Items.Clear();
            ClearSelection();
            Recount();
            ScrollTo(0);
        }

        // Повідомлення змінилося (новий текст або кінець відповіді). Якщо ми були внизу — лишаємося внизу.
        public void Changed(ChatMessage m)
        {
            bool stick = AtBottom && !selecting;
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

        // ── геометрія видимих абзаців ────────────────────────────────────────────

        class PV
        {
            public ChatMessage M;
            public int Mi;
            public Para P;
            public int X, Y, W;
            public Rectangle Box;   // прямокутник усього повідомлення
        }

        Rectangle MsgRect(ChatMessage m, int y) { return new Rectangle(m.IsUser ? ColX + ColW - m.BubbleW : ColX, y, m.BubbleW, m.Height); }

        IEnumerable<KeyValuePair<ChatMessage, Rectangle>> OnScreen()
        {
            int y = TopPad - offset;
            foreach (var m in Items)
            {
                var r = MsgRect(m, y);
                if (r.Bottom >= 0 && r.Top <= Height) yield return new KeyValuePair<ChatMessage, Rectangle>(m, r);
                y += m.Height + Gap;
                if (y > Height) yield break;
            }
        }

        IEnumerable<PV> VisibleParas()
        {
            int y = TopPad - offset;
            for (int mi = 0; mi < Items.Count; mi++)
            {
                var m = Items[mi];
                var r = MsgRect(m, y);
                y += m.Height + Gap;
                if (r.Bottom < 0) continue;
                if (r.Top > Height) yield break;
                bool bubble = m.Role != "assistant";
                int tx = r.X + (bubble ? PadX : 0), ty = r.Y + (bubble ? PadY : 0);
                foreach (var p in m.Paras)
                {
                    if (ty + p.H >= 0 && ty <= Height)
                    {
                        bool code = p.Kind == Para.CodeLine;
                        int ind = code ? CodePad : p.Indent;
                        yield return new PV { M = m, Mi = mi, P = p, X = tx + ind, Y = ty + p.Pad, W = m.LayoutW - (code ? 2 * CodePad : ind), Box = r };
                    }
                    ty += p.H;
                    if (ty > Height) break;
                }
            }
        }

        // Прямокутник кнопки «Копіювати» в заголовку блоку коду.
        Rectangle CodeCopyRect(PV v)
        {
            string label = copied == v.P ? "Скопійовано" : "Копіювати";
            int w = TextRenderer.MeasureText(label, Ui.Small, Size.Empty, NF).Width + Ui.S(34);
            return new Rectangle(v.X + v.W - w - Ui.S(8), v.Y + Ui.S(6), w, Ui.S(24));
        }

        // Посилання під відповіддю: «Копіювати» і (для агента) «Відкрити папку».
        List<KeyValuePair<string, Rectangle>> FootLinks(ChatMessage m, Rectangle r)
        {
            var list = new List<KeyValuePair<string, Rectangle>>();
            int y = r.Bottom - FootH + Ui.S(4), h = FootH - Ui.S(6);
            if (m.IsUser)
            {
                // Під твоїм повідомленням — праворуч, тільки значки: копіювати, редагувати, видалити.
                int x = r.Right + Ui.S(2);
                foreach (string key in new[] { "delete", "edit", "copy" })
                {
                    x -= h + Ui.S(2);
                    list.Add(new KeyValuePair<string, Rectangle>(key, new Rectangle(x, y, h, h)));
                }
                list.Reverse();
                return list;
            }
            var keys = new List<string> { "copy", "speak" };
            if (Editable && IsLastAnswer(m)) keys.Add("regen");
            if (m.Folder != null)
            {
                if (!m.SiteChecked)
                {
                    m.SiteChecked = true;
                    try { string site = System.IO.Path.Combine(m.Folder, "index.html"); if (System.IO.File.Exists(site)) m.Site = site; } catch { }
                }
                if (m.Site != null) keys.Add("site");
                keys.Add("folder");
            }
            int lx = r.X - Ui.S(4);
            foreach (string key in keys)
            {
                int w = TextRenderer.MeasureText(FootLabel(m, key), Ui.Small, Size.Empty, NF).Width + Ui.S(30);
                list.Add(new KeyValuePair<string, Rectangle>(key, new Rectangle(lx, y, w, h)));
                lx += w + Ui.S(4);
            }
            return list;
        }

        string FootLabel(ChatMessage m, string key)
        {
            switch (key)
            {
                case "speak": return Speaker.Current == m ? (Speaker.Status ?? "Зупинити") : "Слухати";
                case "folder": return "Відкрити папку";
                case "site": return "Переглянути сайт";
                case "regen": return "Ще раз";
                case "edit": return "Редагувати";
                case "delete": return "Видалити";
                default: return copied == m ? "Скопійовано" : "Копіювати";
            }
        }

        static string FootIcon(ChatMessage m, string key)
        {
            switch (key)
            {
                case "folder": return "folder";
                case "speak": return Speaker.Current == m ? "stop" : "speaker";
                case "site": return "eye";
                case "regen": return "refresh";
                case "edit": return "new";
                case "delete": return "trash";
                default: return "copy";
            }
        }

        // ── малювання ───────────────────────────────────────────────────────────

        Color CodeBg { get { return Ui.Dark ? Ui.Mix(Ui.Bg, Color.Black, 0.35f) : Ui.Mix(Ui.Bg, Ui.Ink, 0.045f); } }

        Rectangle BubbleRect(ChatMessage m, Rectangle r)
        {
            return HasFoot(m) && m.Role != "assistant" ? new Rectangle(r.X, r.Y, r.Width, r.Height - FootH - Ui.S(4)) : r;
        }

        static bool IsHtml(Para head)
        {
            string l = (head.Lang ?? "").ToLowerInvariant();
            if (l == "html" || l == "htm" || l == "svg" || l == "xhtml") return true;
            string c = (head.Code ?? "").TrimStart();
            return l.Length == 0 && (c.StartsWith("<!DOCTYPE html", StringComparison.OrdinalIgnoreCase) || c.StartsWith("<html", StringComparison.OrdinalIgnoreCase));
        }

        // Кнопка «Переглянути» в заголовку блоку HTML-коду — ліворуч від «Копіювати».
        Rectangle CodePreviewRect(PV v)
        {
            var cr = CodeCopyRect(v);
            int w = TextRenderer.MeasureText("Переглянути", Ui.Small, Size.Empty, NF).Width + Ui.S(34);
            return new Rectangle(cr.X - w - Ui.S(4), cr.Y, w, cr.Height);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Prep(e);
            if (Items.Count == 0) { PaintEmpty(g); return; }

            // Тло повідомлень (бульбашки) і підвали.
            foreach (var kv in OnScreen())
            {
                var m = kv.Key;
                var r = kv.Value;
                var br = BubbleRect(m, r);
                if (m.IsUser) Ui.FillRound(g, br, Ui.S(18), Ui.Blue);
                else if (m.Role == "error") Ui.FillRound(g, br, Ui.S(14), Color.FromArgb(Ui.Dark ? 46 : 26, Ui.Red));
                if (HasFoot(m)) PaintFoot(g, m, r);
            }

            // Тло блоків коду — окремим проходом, щоб заокруглені краї не налазили на текст.
            foreach (var v in VisibleParas())
            {
                var p = v.P;
                int rad = Ui.S(10);
                if (p.Kind == Para.CodeHead) Ui.FillRound(g, new Rectangle(v.X, v.Y, v.W, p.H + rad), rad, CodeBg);
                else if (p.Kind == Para.CodeLine)
                    using (var b = new SolidBrush(CodeBg)) g.FillRectangle(b, v.X - CodePad, v.Y, v.W + 2 * CodePad, p.H);
                else if (p.Kind == Para.CodeFoot)
                {
                    int inner = p.H - Ui.S(8);
                    Ui.FillRound(g, new Rectangle(v.X, v.Y - rad, v.W, inner + rad), rad, CodeBg);
                }
            }

            // Текст, заголовки блоків коду, виділення.
            Color selUser = Color.FromArgb(90, 255, 255, 255), sel = Color.FromArgb(Ui.Dark ? 110 : 80, Ui.Blue);
            foreach (var v in VisibleParas())
            {
                var p = v.P;
                var m = v.M;
                if (p.Kind == Para.CodeHead)
                {
                    Ui.Text(g, string.IsNullOrEmpty(p.Lang) ? "код" : p.Lang, Ui.Small, new Rectangle(v.X + Ui.S(14), v.Y + Ui.S(6), v.W / 3, Ui.S(24)), Ui.Ink3, Ui.Left);
                    var cr = CodeCopyRect(v);
                    if (hoverObj == p && hoverKey == "code") Ui.FillRound(g, cr, Ui.S(7), Ui.Mix(CodeBg, Ui.Ink, 0.08f));
                    float s = Ui.S(13);
                    Icons.Draw(g, "copy", new RectangleF(cr.X + Ui.S(8), cr.Y + (cr.Height - s) / 2f, s, s), Ui.Ink2);
                    Ui.Text(g, copied == p ? "Скопійовано" : "Копіювати", Ui.Small, new Rectangle(cr.X + Ui.S(26), cr.Y, cr.Width - Ui.S(26), cr.Height), Ui.Ink2, Ui.Left);
                    if (Editable && IsHtml(p) && (!m.Streaming || p.Code != null))
                    {
                        var pr = CodePreviewRect(v);
                        if (hoverObj == p && hoverKey == "preview") Ui.FillRound(g, pr, Ui.S(7), Ui.Mix(CodeBg, Ui.Ink, 0.08f));
                        Icons.Draw(g, "eye", new RectangleF(pr.X + Ui.S(8), pr.Y + (pr.Height - s) / 2f, s, s), Ui.Blue);
                        Ui.Text(g, "Переглянути", Ui.Small, new Rectangle(pr.X + Ui.S(26), pr.Y, pr.Width - Ui.S(26), pr.Height), Ui.Blue, Ui.Left);
                    }
                    continue;
                }
                if (p.Kind == Para.CodeFoot) continue;
                if (p.Kind == Para.Images)
                {
                    int ix = v.X;
                    foreach (var t in m.Thumbs)
                    {
                        var ir = new Rectangle(ix, v.Y, t.Width, t.Height);
                        using (var path = Ui.Round(ir, Ui.S(10)))
                        {
                            g.SetClip(path);
                            g.DrawImage(t, ir);
                            g.ResetClip();
                        }
                        ix += t.Width + Ui.S(6);
                    }
                    continue;
                }
                if (p.Kind == Para.Files)
                {
                    var chips = FileChips(m, v.X, v.Y, v.W);
                    for (int i = 0; i < chips.Count && i < m.Files.Count; i++)
                    {
                        var cr = chips[i];
                        Ui.FillRound(g, cr, Ui.S(9), m.IsUser ? Color.FromArgb(46, 255, 255, 255) : Ui.Fill);
                        float s = Ui.S(14);
                        Color c = m.IsUser ? Color.White : Ui.Ink2;
                        Icons.Draw(g, "file", new RectangleF(cr.X + Ui.S(10), cr.Y + (cr.Height - s) / 2f, s, s), c);
                        Ui.Text(g, m.Files[i].Name, Ui.Small, new Rectangle(cr.X + Ui.S(30), cr.Y, cr.Width - Ui.S(38), cr.Height), c, Ui.Left);
                    }
                    continue;
                }
                if (p.Kind == Para.Rule)
                {
                    if (p.H >= Ui.S(12))
                        using (var pen = new Pen(Ui.Separator, Math.Max(1f, Ui.Scale))) g.DrawLine(pen, v.X, v.Y + p.H / 2, v.X + v.W, v.Y + p.H / 2);
                    continue;
                }
                if (p.Kind == Para.Table) { PaintTable(g, v); continue; }
                if (p.Kind == Para.Steps || p.Kind == Para.Think)
                {
                    bool th = p.Kind == Para.Think;
                    var sr = StepsRect(v);
                    string key = th ? "think" : "steps";
                    Ui.FillRound(g, sr, sr.Height / 2f, hoverObj == m && hoverKey == key ? Ui.FillHover : Ui.Fill);
                    float ic = Ui.S(13);
                    if (IsLive(m, p))
                    {
                        // Три крапки, що «дихають», — модель працює.
                        int phase = (int)(DateTime.UtcNow.Ticks / TimeSpan.TicksPerMillisecond / 280 % 3);
                        for (int k = 0; k < 3; k++)
                        {
                            float d = Ui.S(4);
                            using (var b = new SolidBrush(Color.FromArgb(k == phase ? 255 : 90, th ? Ui.Purple : Ui.Orange)))
                                g.FillEllipse(b, sr.X + Ui.S(10) + k * Ui.S(6), sr.Y + (sr.Height - d) / 2f, d, d);
                        }
                    }
                    else Icons.Draw(g, th ? "spark" : "folder", new RectangleF(sr.X + Ui.S(11), sr.Y + (sr.Height - ic) / 2f, ic, ic), th ? Ui.Purple : Ui.Ink2);
                    Ui.Text(g, SummaryText(m, p), Ui.Small, new Rectangle(sr.X + Ui.S(36), sr.Y, sr.Width - Ui.S(62), sr.Height), Ui.Ink2, Ui.Left);
                    if (!p.Live)
                    {
                        bool open = th ? m.ThinkOpen : m.StepsOpen;
                        Icons.Draw(g, open ? "chevron" : "chevron-right", new RectangleF(sr.Right - Ui.S(22), sr.Y + (sr.Height - ic) / 2f, ic, ic), Ui.Ink3);
                    }
                    continue;
                }

                Color fore = m.IsUser ? Color.White : m.Role == "error" ? Ui.Red : p.Thought ? Ui.Ink3 : (p.Step || p.Quote) ? Ui.Ink2 : Ui.Ink;
                int lh = LH(p);
                if (p.Quote)
                    using (var b = new SolidBrush(Ui.Mix(Ui.Bg, Ui.Ink, 0.22f))) g.FillRectangle(b, v.X - Ui.S(12), v.Y, Ui.S(3), p.TextH);
                if (p.Thought)
                    using (var b = new SolidBrush(Ui.Mix(Ui.Bg, Ui.Purple, 0.35f))) g.FillRectangle(b, v.X - Ui.S(10), v.Y, Math.Max(1, Ui.S(2)), p.TextH);
                int s0 = 0, s1 = 0;
                bool hasSel = !p.Thought && SelRange(v.Mi, out s0, out s1);
                for (int li = 0; li < p.Lines.Count; li++)
                {
                    int y = v.Y + li * lh;
                    if (y + lh < 0 || y > Height) continue;
                    int a = p.Lines[li], b = li + 1 < p.Lines.Count ? p.Lines[li + 1] : p.Str.Length;
                    int lx = v.X + (li > 0 ? p.Hang : 0);
                    if (hasSel)
                    {
                        int ga = p.Start + a, gb = p.Start + b;
                        int x0 = Math.Max(s0, ga), x1 = Math.Min(s1, gb);
                        bool eol = s1 > gb && li == p.Lines.Count - 1;   // виділення йде далі — підсвітити й «перенос рядка»
                        if (x0 < x1 || (eol && s0 <= gb))
                        {
                            int px0 = W(p, a, a + Math.Max(0, x0 - ga));
                            int px1 = W(p, a, a + Math.Max(0, Math.Min(b - a, x1 - ga))) + (eol ? Ui.S(5) : 0);
                            using (var br = new SolidBrush(m.IsUser ? selUser : sel)) g.FillRectangle(br, lx + px0, y, Math.Max(1, px1 - px0), lh);
                        }
                    }
                    if (b > a) DrawLine(g, p, a, b, lx, y, lh, fore);
                }
            }
            // М'яке затухання вгорі: прокручений текст не обрізається різко під кнопками.
            if (offset > 0)
            {
                int fh = Ui.S(18);
                using (var br = new LinearGradientBrush(new Rectangle(0, 0, Width, fh + 1), Back, Color.FromArgb(0, Back), LinearGradientMode.Vertical))
                    g.FillRectangle(br, 0, 0, Width - Ui.S(12), fh);
            }
            PaintBar(g);
            PaintDownButton(g);
        }

        // Один рядок екрана: звичайний текст одним викликом, а з розміткою — шматками різними шрифтами.
        void DrawLine(Graphics g, Para p, int a, int b, int x, int y, int lh, Color fore)
        {
            if (p.Sty == null)
            {
                var f = F(FiOf(p));
                TextRenderer.DrawText(g, p.Str.Substring(a, b - a), f, new Point(x, y + (lh - LineHF(FiOf(p))) / 2), fore, NF);
                return;
            }
            for (int i = a; i < b; )
            {
                int j = i;
                byte st = p.Sty[i];
                while (j < b && p.Sty[j] == st) j++;
                string run = p.Str.Substring(i, j - i);
                int rw = W(p, i, j);
                if (st == 5)
                {
                    var cr = new Rectangle(x, y + Ui.S(1), rw, lh - Ui.S(2));
                    Ui.FillRound(g, cr, Ui.S(4), Ui.Dark ? Ui.Mix(Ui.Bg, Color.White, 0.08f) : Ui.Mix(Ui.Bg, Ui.Ink, 0.07f));
                    TextRenderer.DrawText(g, run, F(5), new Point(x + CodePadX, y + (lh - LineHF(5)) / 2), fore, NF);
                }
                else TextRenderer.DrawText(g, run, F(st), new Point(x, y + (lh - LineHF(st)) / 2), fore, NF);
                x += rw;
                i = j;
            }
        }

        void PaintTable(Graphics g, PV v)
        {
            var p = v.P;
            int tw = p.ColWs.Sum(), th = p.RowHs.Sum();
            var box = new Rectangle(v.X, v.Y, tw, th);
            float k = Math.Max(1f, Ui.Scale);
            using (var path = Ui.Round(box, Ui.S(8)))
            {
                g.SetClip(path);
                if (p.RowHs.Length > 0) using (var b = new SolidBrush(Ui.Mix(Ui.Bg, Ui.Ink, Ui.Dark ? 0.08f : 0.045f))) g.FillRectangle(b, box.X, box.Y, tw, p.RowHs[0]);
                using (var pen = new Pen(Ui.Separator, k))
                {
                    int y = box.Y;
                    for (int ri = 0; ri < p.Rows.Count; ri++)
                    {
                        int x = box.X;
                        for (int c = 0; c < p.ColWs.Length; c++)
                        {
                            string t = c < p.Rows[ri].Length ? p.Rows[ri][c] : "";
                            if (t.Length > 0)
                                TextRenderer.DrawText(g, t, ri == 0 ? Ui.ChatSemi : Ui.Chat,
                                    new Rectangle(x + CellPadX, y + CellPadY, Math.Max(1, p.ColWs[c] - 2 * CellPadX), p.RowHs[ri] - 2 * CellPadY + Ui.S(2)), Ui.Ink, CellFlags);
                            x += p.ColWs[c];
                            if (c < p.ColWs.Length - 1) g.DrawLine(pen, x, box.Y, x, box.Bottom);
                        }
                        y += p.RowHs[ri];
                        if (ri < p.Rows.Count - 1) g.DrawLine(pen, box.X, y, box.Right, y);
                    }
                }
                g.ResetClip();
                using (var pen = new Pen(Ui.Separator, k)) g.DrawPath(pen, path);
            }
        }

        // Кругла кнопка «вниз»: з'являється, коли прокрутив угору від кінця розмови.
        bool ShowDown { get { return total > Height && offset < MaxOffset - Ui.S(60); } }

        Rectangle DownRect
        {
            get { int d = Ui.S(36); return new Rectangle(ColX + ColW / 2 - d / 2, Height - d - Ui.S(10), d, d); }
        }

        void PaintDownButton(Graphics g)
        {
            if (!ShowDown) return;
            var r = DownRect;
            bool hov = hoverKey == "down";
            using (var sh = new SolidBrush(Color.FromArgb(Ui.Dark ? 90 : 28, 0, 0, 0))) g.FillEllipse(sh, r.X, r.Y + Ui.S(2), r.Width, r.Height);
            using (var b = new SolidBrush(hov ? Ui.Mix(Ui.Card, Ui.Ink, 0.07f) : Ui.Card)) g.FillEllipse(b, r);
            using (var pen = new Pen(Ui.CardLine, Math.Max(1f, Ui.Scale))) g.DrawEllipse(pen, r);
            float s = Ui.S(15);
            Icons.Draw(g, "arrow-down", new RectangleF(r.X + (r.Width - s) / 2f, r.Y + (r.Height - s) / 2f, s, s), Ui.Ink);
        }

        // Рядок-підсумок, що «живе», поки агент працює: поточний крок і час.
        static bool IsLive(ChatMessage m, Para p)
        {
            if (p.Kind == Para.Think) return m.Streaming && m.InThink;
            return m.Streaming && (p.Live || (p.Summary != null && p.Summary.StartsWith("Працює: ")));
        }

        static string Dur(double sec)
        {
            int s = (int)Math.Round(sec);
            return s >= 60 ? s / 60 + " хв " + s % 60 + " с" : s + " с";
        }

        static string SummaryText(ChatMessage m, Para p)
        {
            if (p.Kind == Para.Think)
            {
                if (IsLive(m, p)) return "Міркує   " + Dur((DateTime.UtcNow - m.StartedAt).TotalSeconds);
                return m.ThinkSec >= 1 ? "Думав " + Dur(m.ThinkSec) : "Міркування моделі";
            }
            if (!IsLive(m, p)) return p.Summary ?? "";
            return p.Summary + "   " + Dur((DateTime.UtcNow - m.StartedAt).TotalSeconds);
        }

        Rectangle StepsRect(PV v)
        {
            int tw = TextRenderer.MeasureText(SummaryText(v.M, v.P), Ui.Small, Size.Empty, NF).Width;
            return new Rectangle(v.X, v.Y + Ui.S(4), Math.Min(v.W, tw + Ui.S(68)), Ui.S(28));
        }

        void PaintFoot(Graphics g, ChatMessage m, Rectangle r)
        {
            var links = FootLinks(m, r);
            if (m.IsUser)
            {
                if (hoverMsg != m) return;
                foreach (var kv in links)
                {
                    var cr = kv.Value;
                    if (hoverObj == m && hoverKey == kv.Key) Ui.FillRound(g, cr, Ui.S(7), Ui.Fill);
                    float s = Ui.S(13);
                    Icons.Draw(g, copied == m && kv.Key == "copy" ? "chevron" : FootIcon(m, kv.Key), new RectangleF(cr.X + (cr.Width - s) / 2f, cr.Y + (cr.Height - s) / 2f, s, s),
                        kv.Key == "delete" && hoverObj == m && hoverKey == "delete" ? Ui.Red : Ui.Ink2);
                }
                return;
            }
            foreach (var kv in links)
            {
                var cr = kv.Value;
                if (hoverObj == m && hoverKey == kv.Key) Ui.FillRound(g, cr, Ui.S(7), Ui.Fill);
                float s = Ui.S(13);
                Icons.Draw(g, FootIcon(m, kv.Key), new RectangleF(cr.X + Ui.S(7), cr.Y + (cr.Height - s) / 2f, s, s), Ui.Ink2);
                Ui.Text(g, FootLabel(m, kv.Key), Ui.Small, new Rectangle(cr.X + Ui.S(25), cr.Y, cr.Width - Ui.S(25), cr.Height), Ui.Ink2, Ui.Left);
            }
            var last = links[links.Count - 1].Value;
            string stats = (m.Model ?? "") + (string.IsNullOrEmpty(m.Stats) ? "" : (string.IsNullOrEmpty(m.Model) ? "" : " · ") + m.Stats);
            if (stats.Length > 0)
                Ui.Text(g, stats, Ui.Small, new Rectangle(last.Right + Ui.S(10), last.Y, Math.Max(0, r.Right - last.Right - Ui.S(10)), last.Height), Ui.Ink3, Ui.Left);
        }

        void PaintEmpty(Graphics g)
        {
            if (Compact)
            {
                TextRenderer.DrawText(g, EmptyText, Ui.Body, new Rectangle(Ui.S(12), Ui.S(12), Width - Ui.S(24), Height - Ui.S(24)), Ui.Ink3,
                    TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
                return;
            }
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

        // ── виділення ──────────────────────────────────────────────────────────────

        public bool HasSelection { get { return selAM >= 0 && (selAM != selBM || selAC != selBC); } }

        public void ClearSelection() { selAM = selBM = -1; selecting = false; Invalidate(); }

        void Ordered(out int am, out int ac, out int bm, out int bc)
        {
            bool fwd = selAM < selBM || (selAM == selBM && selAC <= selBC);
            am = fwd ? selAM : selBM; ac = fwd ? selAC : selBC;
            bm = fwd ? selBM : selAM; bc = fwd ? selBC : selAC;
        }

        bool SelRange(int mi, out int s0, out int s1)
        {
            s0 = s1 = 0;
            if (!HasSelection) return false;
            int am, ac, bm, bc;
            Ordered(out am, out ac, out bm, out bc);
            if (mi < am || mi > bm) return false;
            s0 = mi == am ? ac : 0;
            s1 = mi == bm ? bc : (Items[mi].Shown ?? "").Length;
            return s1 > s0;
        }

        public string SelectedText
        {
            get
            {
                if (!HasSelection) return "";
                var sb = new StringBuilder();
                for (int mi = 0; mi < Items.Count; mi++)
                {
                    int s0, s1;
                    if (!SelRange(mi, out s0, out s1)) continue;
                    string s = Items[mi].Shown ?? "";
                    s0 = Math.Min(s0, s.Length);
                    s1 = Math.Min(s1, s.Length);
                    if (sb.Length > 0) sb.Append("\r\n\r\n");
                    sb.Append(s.Substring(s0, s1 - s0).Replace("\n", "\r\n"));
                }
                return sb.ToString();
            }
        }

        public void CopySelection() { if (HasSelection) CopyToClipboard(SelectedText, null); }

        public void SelectAll()
        {
            if (Items.Count == 0) return;
            selAM = 0; selAC = 0;
            selBM = Items.Count - 1; selBC = (Items[Items.Count - 1].Shown ?? "").Length;
            Invalidate();
        }

        // Позиція в тексті під точкою (найближча, якщо точка між рядками чи повідомленнями).
        bool HitPos(Point pt, out int mi, out int ch)
        {
            mi = -1; ch = 0;
            PV best = null;
            int bestD = int.MaxValue;
            foreach (var v in VisibleParas())
            {
                if (v.P.Kind > Para.CodeLine || v.P.Thought) continue;
                int d = pt.Y < v.Y ? v.Y - pt.Y : pt.Y >= v.Y + v.P.TextH ? pt.Y - (v.Y + v.P.TextH) + 1 : 0;
                if (d < bestD) { bestD = d; best = v; }
                if (d == 0) break;
            }
            if (best == null) return false;
            var p = best.P;
            int lh = LH(p);
            int li = Math.Max(0, Math.Min(p.Lines.Count - 1, (pt.Y - best.Y) / Math.Max(1, lh)));
            if (pt.Y < best.Y) li = 0;
            int a = p.Lines[li], b = li + 1 < p.Lines.Count ? p.Lines[li + 1] : p.Str.Length;
            int len = b - a;
            int x = pt.X - best.X - (li > 0 ? p.Hang : 0), k;
            if (pt.Y < best.Y && best.Y > 0) k = 0;
            else if (x <= 0) k = 0;
            else
            {
                int lo = 0, hi = len;
                while (lo < hi)
                {
                    int mid = (lo + hi + 1) / 2;
                    if (W(p, a, a + mid) <= x) lo = mid; else hi = mid - 1;
                }
                k = lo;
                if (k < len)
                {
                    int w0 = W(p, a, a + k), w1 = W(p, a, a + k + 1);
                    if (x - w0 > w1 - x) k++;
                }
            }
            mi = best.Mi;
            ch = p.Start + a + k;
            return true;
        }

        void ExtendSelection(Point pt)
        {
            int mi, ch;
            if (!HitPos(pt, out mi, out ch)) return;
            if (mi != selBM || ch != selBC) { selBM = mi; selBC = ch; Invalidate(); }
        }

        void SelectWord(int mi, int ch)
        {
            string s = Items[mi].Shown ?? "";
            int a = Math.Min(ch, s.Length), b = a;
            Func<char, bool> word = c => char.IsLetterOrDigit(c) || c == '_' || c == '\'' || c == '’';
            while (a > 0 && word(s[a - 1])) a--;
            while (b < s.Length && word(s[b])) b++;
            selAM = selBM = mi;
            selAC = a;
            selBC = b;
            Invalidate();
        }

        // ── миша й клавіатура ──────────────────────────────────────────────────────

        ChatMessage HitMessage(Point p)
        {
            foreach (var kv in OnScreen())
                if (p.Y >= kv.Value.Top && p.Y <= kv.Value.Bottom) return kv.Key;
            return null;
        }

        // Кнопка під курсором: (об'єкт, ключ) — підвал повідомлення або «Копіювати» блоку коду.
        bool HitButton(Point p, out object obj, out string key)
        {
            obj = null; key = null;
            foreach (var kv in OnScreen())
                if (HasFoot(kv.Key) && (!kv.Key.IsUser || kv.Key == hoverMsg))
                    foreach (var l in FootLinks(kv.Key, kv.Value))
                        if (l.Value.Contains(p)) { obj = kv.Key; key = l.Key; return true; }
            if (ShowDown && DownRect.Contains(p)) { obj = this; key = "down"; return true; }
            foreach (var v in VisibleParas())
            {
                if (v.P.Kind == Para.Images && v.M.Thumbs != null)
                {
                    int ix = v.X;
                    for (int i = 0; i < v.M.Thumbs.Count; i++)
                    {
                        if (new Rectangle(ix, v.Y, v.M.Thumbs[i].Width, v.M.Thumbs[i].Height).Contains(p) && i < v.M.Images.Count) { obj = v.M.Images[i]; key = "img"; return true; }
                        ix += v.M.Thumbs[i].Width + Ui.S(6);
                    }
                }
                if (v.P.Kind == Para.CodeHead && CodeCopyRect(v).Contains(p)) { obj = v.P; key = "code"; return true; }
                if (v.P.Kind == Para.CodeHead && Editable && IsHtml(v.P) && CodePreviewRect(v).Contains(p)) { obj = v.P; key = "preview"; return true; }
                if (v.P.Kind == Para.Steps && !v.P.Live && StepsRect(v).Contains(p)) { obj = v.M; key = "steps"; return true; }
                if (v.P.Kind == Para.Think && StepsRect(v).Contains(p)) { obj = v.M; key = "think"; return true; }
            }
            return false;
        }

        bool OverText(Point p)
        {
            foreach (var v in VisibleParas())
                if (v.P.Kind <= Para.CodeLine && !v.P.Thought && p.Y >= v.Y && p.Y < v.Y + v.P.TextH && p.X >= v.X - Ui.S(4) && p.X <= v.X + v.W) return true;
            return false;
        }

        void CopyToClipboard(string text, object what)
        {
            if (string.IsNullOrEmpty(text)) return;
            try { Clipboard.SetText(text.Contains("\r\n") ? text : text.Replace("\n", "\r\n")); } catch { return; }
            if (what != null)
            {
                copied = what;
                copiedTimer.Stop();
                copiedTimer.Start();
            }
            Invalidate();
        }

        public event Action<string> OpenFolder;
        public event Action<ChatMessage> Speak;
        public event Action<ChatMessage> EditMessage, DeleteMessage, Regenerate;
        public event Action<string> PreviewHtml, PreviewFile;

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
            Focus();   // щоб коліщатко, Ctrl+C і клавіші прокрутки йшли сюди
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
                return;
            }
            object obj;
            string key;
            if (HitButton(e.Location, out obj, out key)) return;   // спрацює на відпусканні
            int mi, ch;
            if (OverText(e.Location) && HitPos(e.Location, out mi, out ch))
            {
                if (e.Clicks == 2) { SelectWord(mi, ch); return; }
                if ((ModifierKeys & Keys.Shift) != 0 && selAM >= 0) { selBM = mi; selBC = ch; }
                else { selAM = selBM = mi; selAC = selBC = ch; }
                selecting = true;
                Capture = true;
                Invalidate();
            }
            else ClearSelection();
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
            if (selecting)
            {
                ExtendSelection(e.Location);
                autoDy = e.Y < 0 ? Math.Max(-Ui.S(60), e.Y) : e.Y > Height ? Math.Min(Ui.S(60), e.Y - Height) : 0;
                if (autoDy != 0 && !autoScroll.Enabled) autoScroll.Start();
                return;
            }
            var hm = HitMessage(e.Location);
            if (hm != hoverMsg) { hoverMsg = hm; Invalidate(); }
            object obj;
            string key;
            bool btn = HitButton(e.Location, out obj, out key);
            if (obj != hoverObj || key != hoverKey)
            {
                hoverObj = obj;
                hoverKey = key;
                Invalidate();
                var um = obj as ChatMessage;
                tip.SetToolTip(this, um != null && um.IsUser ? (key == "copy" && copied == um ? "Скопійовано" : FootLabel(um, key)) : null);
            }
            Cursor = btn ? Cursors.Hand : OverText(e.Location) ? Cursors.IBeam : Cursors.Default;
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (dragFrom >= 0) { dragFrom = -1; Capture = false; Invalidate(); return; }
            if (selecting) { selecting = false; Capture = false; autoScroll.Stop(); return; }
            if (e.Button != MouseButtons.Left) return;
            object obj;
            string key;
            if (!HitButton(e.Location, out obj, out key)) return;
            if (key == "code") CopyToClipboard(((Para)obj).Code, obj);
            else if (key == "preview") { if (PreviewHtml != null) PreviewHtml(((Para)obj).Code); }
            else if (key == "copy") CopyToClipboard(((ChatMessage)obj).Display, obj);
            else if (key == "edit") { if (EditMessage != null) EditMessage((ChatMessage)obj); }
            else if (key == "delete") { if (DeleteMessage != null) DeleteMessage((ChatMessage)obj); }
            else if (key == "regen") { if (Regenerate != null) Regenerate((ChatMessage)obj); }
            else if (key == "site") { if (PreviewFile != null) PreviewFile(((ChatMessage)obj).Site); }
            else if (key == "think")
            {
                var tm = (ChatMessage)obj;
                tm.ThinkOpen = !tm.ThinkOpen;
                ClearSelection();
                Recount();
                ScrollTo(offset);
            }
            else if (key == "folder" && OpenFolder != null) OpenFolder(((ChatMessage)obj).Folder);
            else if (key == "speak" && Speak != null) Speak((ChatMessage)obj);
            else if (key == "down") ScrollToEnd();
            else if (key == "img") Pictures.Open((string)obj);
            else if (key == "steps")
            {
                var m = (ChatMessage)obj;
                m.StepsOpen = !m.StepsOpen;
                ClearSelection();
                Recount();
                ScrollTo(offset);
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (hoverObj != null || hoverMsg != null) { hoverObj = null; hoverKey = null; hoverMsg = null; Invalidate(); }
        }

        protected override bool IsInputKey(Keys k)
        {
            return k == Keys.Up || k == Keys.Down || k == Keys.PageUp || k == Keys.PageDown || k == Keys.Home || k == Keys.End || base.IsInputKey(k);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.Control && e.KeyCode == Keys.C) { CopySelection(); e.Handled = true; return; }      // за кодом клавіші — на будь-якій розкладці
            if (e.Control && e.KeyCode == Keys.A) { SelectAll(); e.Handled = true; return; }
            switch (e.KeyCode)
            {
                case Keys.Up: ScrollTo(offset - Ui.S(40)); break;
                case Keys.Down: ScrollTo(offset + Ui.S(40)); break;
                case Keys.PageUp: ScrollTo(offset - Height * 9 / 10); break;
                case Keys.PageDown: ScrollTo(offset + Height * 9 / 10); break;
                case Keys.Home: if (e.Control) ScrollTo(0); break;
                case Keys.End: if (e.Control) ScrollToEnd(); break;
                case Keys.Escape: ClearSelection(); break;
            }
        }
    }
}
