// Open Forge — файли в чаті: текст, код, Word (.docx) і PDF. Модель отримує текст файлу разом із повідомленням.
// Усе читається тут, на ПК, без сторонніх бібліотек: .docx — це zip з XML, а з PDF текст дістається
// власним невеликим розбором (стиснені потоки, шрифти з таблицею ToUnicode, оператори Tj/TJ).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;

namespace OpenForge
{
    class FileAttachment
    {
        public string Name, Text;
        public int Chars;   // скільки символів було у файлі (Text може бути обрізаний)
    }

    static class FileText
    {
        public const int MaxPerMessage = 5;
        public const string DialogFilter = "Документи й код|*.txt;*.md;*.pdf;*.docx;*.csv;*.json;*.xml;*.html;*.htm;*.css;*.js;*.ts;*.py;*.cs;*.java;*.c;*.cpp;*.h;*.go;*.rs;*.php;*.rb;*.sql;*.yaml;*.yml;*.ini;*.log;*.bat;*.ps1;*.sh|Усі файли|*.*";

        static readonly HashSet<string> TextExt = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".txt", ".md", ".markdown", ".csv", ".tsv", ".json", ".xml", ".html", ".htm", ".css", ".scss", ".js", ".jsx", ".ts", ".tsx", ".mjs",
            ".py", ".cs", ".java", ".kt", ".c", ".cpp", ".cc", ".h", ".hpp", ".go", ".rs", ".php", ".rb", ".swift", ".lua", ".sql", ".yaml", ".yml",
            ".toml", ".ini", ".cfg", ".conf", ".log", ".bat", ".cmd", ".ps1", ".sh", ".vue", ".svelte", ".dart", ".r", ".m", ".tex", ".srt", ".env", ".gitignore",
        };

        public static bool CanRead(string path)
        {
            string ext = Path.GetExtension(path ?? "");
            return TextExt.Contains(ext) || ext.Equals(".pdf", StringComparison.OrdinalIgnoreCase) || ext.Equals(".docx", StringComparison.OrdinalIgnoreCase);
        }

        // Скільки символів тексту файлів можна дати моделі за раз — щоб розмова влізла в її пам'ять.
        public static int Budget { get { return Ollama.NumCtx * 2; } }

        // Прочитати файл. Помилка — текстом у error.
        public static FileAttachment Read(string path, out string error)
        {
            error = null;
            string name = Path.GetFileName(path);
            try
            {
                var fi = new FileInfo(path);
                if (fi.Length > 60L * 1024 * 1024) { error = name + ": файл завеликий (понад 60 МБ)."; return null; }
                string ext = fi.Extension.ToLowerInvariant(), text;
                if (ext == ".pdf") text = Pdf(File.ReadAllBytes(path));
                else if (ext == ".docx") text = Docx(path);
                else
                {
                    byte[] b = File.ReadAllBytes(path);
                    if (b.Take(4096).Count(x => x == 0) > 8) { error = name + ": це не текстовий файл."; return null; }
                    text = Decode(b);
                }
                text = (text ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Trim();
                if (text.Length == 0)
                {
                    error = name + (ext == ".pdf" ? ": у PDF немає тексту — схоже, це скан або картинки. Зроби скріншот сторінки й додай як фото." : ": файл порожній.");
                    return null;
                }
                return new FileAttachment { Name = name, Text = text, Chars = text.Length };
            }
            catch (Exception ex)
            {
                error = name + ": не вдалося прочитати (" + ex.Message + ").";
                return null;
            }
        }

        // UTF-8 (з BOM чи без), UTF-16 з BOM; якщо це не UTF-8 — кирилиця Windows-1251.
        static string Decode(byte[] b)
        {
            if (b.Length >= 2 && b[0] == 0xFF && b[1] == 0xFE) return Encoding.Unicode.GetString(b, 2, b.Length - 2);
            if (b.Length >= 2 && b[0] == 0xFE && b[1] == 0xFF) return Encoding.BigEndianUnicode.GetString(b, 2, b.Length - 2);
            int skip = b.Length >= 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF ? 3 : 0;
            try { return new UTF8Encoding(false, true).GetString(b, skip, b.Length - skip); }
            catch (DecoderFallbackException) { return Encoding.GetEncoding(1251).GetString(b); }
        }

        // Текст для моделі: файли з назвами, обрізані, щоб разом не перевищили бюджет.
        public static string ForModel(List<FileAttachment> files, string message)
        {
            if (files == null || files.Count == 0) return message;
            var sb = new StringBuilder();
            int left = Budget;
            foreach (var f in files)
            {
                string t = f.Text;
                int share = Math.Max(2000, left / Math.Max(1, files.Count - files.IndexOf(f)));
                bool cut = t.Length > share;
                if (cut) t = t.Substring(0, share);
                left -= t.Length;
                sb.Append("Файл «").Append(f.Name).Append("»");
                if (cut) sb.Append(" (показано перші ").Append(t.Length).Append(" з ").Append(f.Chars).Append(" символів)");
                sb.Append(":\n```\n").Append(t).Append("\n```\n\n");
            }
            sb.Append(message);
            return sb.ToString();
        }

        // ── Word (.docx) ─────────────────────────────────────────────────────────

        static string Docx(string path)
        {
            using (var zip = ZipFile.OpenRead(path))
            {
                var entry = zip.GetEntry("word/document.xml");
                if (entry == null) return "";
                var sb = new StringBuilder();
                using (var s = entry.Open())
                using (var r = XmlReader.Create(s, new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null }))
                {
                    while (r.Read())
                    {
                        if (r.NodeType == XmlNodeType.Element)
                        {
                            switch (r.LocalName)
                            {
                                case "t": if (!r.IsEmptyElement) sb.Append(r.ReadElementContentAsString()); break;
                                case "tab": sb.Append('\t'); break;
                                case "br": case "cr": sb.Append('\n'); break;
                                case "tc": if (sb.Length > 0 && sb[sb.Length - 1] != '\n') sb.Append(" | "); break;
                            }
                        }
                        else if (r.NodeType == XmlNodeType.EndElement && r.LocalName == "p") sb.Append('\n');
                    }
                }
                return sb.ToString();
            }
        }

        // ── PDF ────────────────────────────────────────────────────────────────────

        class PdfObj { public string Dict; public byte[] Stream; }

        static readonly Encoding Latin1 = Encoding.GetEncoding(28591);

        static string Pdf(byte[] data)
        {
            string s = Latin1.GetString(data);
            if (Regex.IsMatch(s, @"/Encrypt\s")) throw new Exception("PDF захищений паролем");
            var objs = new Dictionary<int, PdfObj>();

            // 1. Усі об'єкти «N 0 obj … endobj» (і потоки в них).
            foreach (Match m in Regex.Matches(s, @"(\d+)\s+\d+\s+obj\b"))
            {
                int id = int.Parse(m.Groups[1].Value);
                int start = m.Index + m.Length;
                int end = s.IndexOf("endobj", start, StringComparison.Ordinal);
                if (end < 0) continue;
                int st = s.IndexOf("stream", start, StringComparison.Ordinal);
                var o = new PdfObj();
                if (st >= 0 && st < end && (st == 0 || s[st - 1] != 'd'))
                {
                    o.Dict = s.Substring(start, st - start);
                    int ds = st + 6;
                    if (ds < s.Length && s[ds] == '\r') ds++;
                    if (ds < s.Length && s[ds] == '\n') ds++;
                    int len = -1;
                    var lm = Regex.Match(o.Dict, @"/Length\s+(\d+)(\s+\d+\s+R)?");
                    if (lm.Success && !lm.Groups[2].Success) len = int.Parse(lm.Groups[1].Value);
                    int es = s.IndexOf("endstream", ds, StringComparison.Ordinal);
                    if (len < 0 || ds + len > data.Length) len = Math.Max(0, (es >= 0 ? es : end) - ds);
                    o.Stream = new byte[len];
                    Array.Copy(data, ds, o.Stream, 0, len);
                    if (es > end) end = s.IndexOf("endobj", es, StringComparison.Ordinal);
                }
                else o.Dict = s.Substring(start, end - start);
                objs[id] = o;
            }

            // 2. Об'єкти, стиснені всередині потоків /ObjStm.
            foreach (var o in objs.Values.ToList())
            {
                if (o.Stream == null || !o.Dict.Contains("/ObjStm")) continue;
                string body = Latin1.GetString(Decoded(o));
                int n = DictInt(o.Dict, "/N"), first = DictInt(o.Dict, "/First");
                if (n <= 0 || first <= 0 || first > body.Length) continue;
                var nums = body.Substring(0, first).Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                for (int i = 0; i + 1 < nums.Length && i / 2 < n; i += 2)
                {
                    int id, off, nextOff;
                    if (!int.TryParse(nums[i], out id) || !int.TryParse(nums[i + 1], out off)) continue;
                    nextOff = i + 3 < nums.Length && int.TryParse(nums[i + 3], out nextOff) ? nextOff : body.Length - first;
                    int a = first + off, b = Math.Min(body.Length, first + nextOff);
                    if (a < b && !objs.ContainsKey(id)) objs[id] = new PdfObj { Dict = body.Substring(a, b - a) };
                }
            }

            // 3. Сторінки по порядку: Catalog → Pages → Kids.
            var pages = new List<PdfObj>();
            var catalog = objs.Values.FirstOrDefault(o => Regex.IsMatch(o.Dict, @"/Type\s*/Catalog"));
            if (catalog != null) CollectPages(objs, Ref(catalog.Dict, "/Pages"), pages, 0);
            if (pages.Count == 0) pages = objs.OrderBy(kv => kv.Key).Select(kv => kv.Value).Where(o => Regex.IsMatch(o.Dict, @"/Type\s*/Page\b")).ToList();

            var sb = new StringBuilder();
            foreach (var page in pages)
            {
                var fonts = PageFonts(objs, page);
                var content = new List<byte>();
                foreach (int cid in Refs(page.Dict, "/Contents", objs))
                {
                    PdfObj c;
                    if (objs.TryGetValue(cid, out c) && c.Stream != null) { content.AddRange(Decoded(c)); content.Add((byte)'\n'); }
                }
                string pageText = ContentText(content.ToArray(), fonts);
                if (pageText.Trim().Length > 0) sb.Append(pageText.Trim()).Append("\n\n");
            }
            return sb.ToString();
        }

        static int DictInt(string dict, string key)
        {
            var m = Regex.Match(dict, Regex.Escape(key) + @"\s+(\d+)");
            return m.Success ? int.Parse(m.Groups[1].Value) : -1;
        }

        static int Ref(string dict, string key)
        {
            var m = Regex.Match(dict, Regex.Escape(key) + @"\s+(\d+)\s+\d+\s+R");
            return m.Success ? int.Parse(m.Groups[1].Value) : -1;
        }

        // Посилання або масив посилань під ключем (масив може бути й окремим об'єктом).
        static List<int> Refs(string dict, string key, Dictionary<int, PdfObj> objs)
        {
            var res = new List<int>();
            var m = Regex.Match(dict, Regex.Escape(key) + @"\s*\[([^\]]*)\]");
            string body = null;
            if (m.Success) body = m.Groups[1].Value;
            else
            {
                int r = Ref(dict, key);
                if (r < 0) return res;
                PdfObj o;
                if (objs.TryGetValue(r, out o) && o.Stream == null && o.Dict.TrimStart().StartsWith("[")) body = o.Dict;
                else { res.Add(r); return res; }
            }
            foreach (Match x in Regex.Matches(body, @"(\d+)\s+\d+\s+R")) res.Add(int.Parse(x.Groups[1].Value));
            return res;
        }

        static void CollectPages(Dictionary<int, PdfObj> objs, int id, List<PdfObj> pages, int depth)
        {
            PdfObj o;
            if (depth > 30 || id < 0 || !objs.TryGetValue(id, out o)) return;
            if (Regex.IsMatch(o.Dict, @"/Type\s*/Pages")) { foreach (int k in Refs(o.Dict, "/Kids", objs)) CollectPages(objs, k, pages, depth + 1); }
            else if (Regex.IsMatch(o.Dict, @"/Type\s*/Page\b")) pages.Add(o);
        }

        static byte[] Decoded(PdfObj o)
        {
            if (o.Stream == null) return new byte[0];
            if (!o.Dict.Contains("/FlateDecode")) return o.Stream;
            try
            {
                using (var ms = new MemoryStream(o.Stream, 2, Math.Max(0, o.Stream.Length - 2)))   // 2 байти — заголовок zlib
                using (var ds = new DeflateStream(ms, CompressionMode.Decompress))
                using (var outp = new MemoryStream())
                {
                    var buf = new byte[65536];
                    int n;
                    try { while ((n = ds.Read(buf, 0, buf.Length)) > 0) outp.Write(buf, 0, n); } catch (InvalidDataException) { }
                    return outp.ToArray();
                }
            }
            catch { return new byte[0]; }
        }

        class PdfFont
        {
            public Dictionary<int, string> Map;   // код → текст (з ToUnicode)
            public int Bytes = 1;                 // скільки байтів на один код
        }

        // Шрифти сторінки (ресурси можуть бути успадковані від батьківського вузла Pages).
        static Dictionary<string, PdfFont> PageFonts(Dictionary<int, PdfObj> objs, PdfObj page)
        {
            var res = new Dictionary<string, PdfFont>();
            PdfObj node = page;
            for (int depth = 0; node != null && depth < 20; depth++)
            {
                string resDict = ResolveDict(objs, node.Dict, "/Resources");
                if (resDict != null)
                {
                    string fontDict = ResolveDict(objs, resDict, "/Font");
                    if (fontDict != null)
                    {
                        foreach (Match m in Regex.Matches(fontDict, @"/([^\s/<>\[\]()]+)\s+(\d+)\s+\d+\s+R"))
                        {
                            PdfObj f;
                            if (res.ContainsKey(m.Groups[1].Value) || !objs.TryGetValue(int.Parse(m.Groups[2].Value), out f)) continue;
                            var pf = new PdfFont { Bytes = f.Dict.Contains("/Type0") ? 2 : 1 };
                            PdfObj cmap;
                            int tu = Ref(f.Dict, "/ToUnicode");
                            if (tu >= 0 && objs.TryGetValue(tu, out cmap)) pf.Map = ParseCMap(Latin1.GetString(Decoded(cmap)), pf);
                            res[m.Groups[1].Value] = pf;
                        }
                        return res;
                    }
                }
                PdfObj parent;
                int pid = Ref(node.Dict, "/Parent");
                node = pid >= 0 && objs.TryGetValue(pid, out parent) ? parent : null;
            }
            return res;
        }

        // Вміст словника під ключем: вкладений << … >> або посилання на окремий об'єкт.
        static string ResolveDict(Dictionary<int, PdfObj> objs, string dict, string key)
        {
            int i = dict.IndexOf(key, StringComparison.Ordinal);
            while (i >= 0)
            {
                int j = i + key.Length;
                if (j < dict.Length && (char.IsLetterOrDigit(dict[j]))) { i = dict.IndexOf(key, j, StringComparison.Ordinal); continue; }
                while (j < dict.Length && char.IsWhiteSpace(dict[j])) j++;
                if (j + 1 < dict.Length && dict[j] == '<' && dict[j + 1] == '<')
                {
                    int depth = 0;
                    for (int k = j; k + 1 < dict.Length; k++)
                    {
                        if (dict[k] == '<' && dict[k + 1] == '<') { depth++; k++; }
                        else if (dict[k] == '>' && dict[k + 1] == '>') { depth--; k++; if (depth == 0) return dict.Substring(j, k + 1 - j); }
                    }
                    return dict.Substring(j);
                }
                var m = Regex.Match(dict.Substring(j), @"^(\d+)\s+\d+\s+R");
                PdfObj o;
                if (m.Success && objs.TryGetValue(int.Parse(m.Groups[1].Value), out o)) return o.Dict;
                return null;
            }
            return null;
        }

        static Dictionary<int, string> ParseCMap(string s, PdfFont font)
        {
            var map = new Dictionary<int, string>();
            var cs = Regex.Match(s, @"begincodespacerange\s*<([0-9A-Fa-f]+)>");
            if (cs.Success) font.Bytes = Math.Max(1, cs.Groups[1].Value.Length / 2);
            foreach (Match blk in Regex.Matches(s, @"beginbfchar(.*?)endbfchar", RegexOptions.Singleline))
                foreach (Match m in Regex.Matches(blk.Groups[1].Value, @"<([0-9A-Fa-f]+)>\s*<([0-9A-Fa-f]*)>"))
                    map[Convert.ToInt32(m.Groups[1].Value, 16)] = Utf16(m.Groups[2].Value);
            foreach (Match blk in Regex.Matches(s, @"beginbfrange(.*?)endbfrange", RegexOptions.Singleline))
                foreach (Match m in Regex.Matches(blk.Groups[1].Value, @"<([0-9A-Fa-f]+)>\s*<([0-9A-Fa-f]+)>\s*(<([0-9A-Fa-f]*)>|\[([^\]]*)\])"))
                {
                    int a = Convert.ToInt32(m.Groups[1].Value, 16), b = Convert.ToInt32(m.Groups[2].Value, 16);
                    if (b - a > 65536) continue;
                    if (m.Groups[4].Success)
                    {
                        string dst = m.Groups[4].Value;
                        if (dst.Length < 4) continue;
                        int baseCode = Convert.ToInt32(dst.Substring(dst.Length - 4), 16);
                        string prefix = Utf16(dst.Substring(0, dst.Length - 4));
                        for (int c = a; c <= b; c++) map[c] = prefix + (char)(baseCode + c - a);
                    }
                    else
                    {
                        var items = Regex.Matches(m.Groups[5].Value, @"<([0-9A-Fa-f]*)>");
                        for (int c = a; c <= b && c - a < items.Count; c++) map[c] = Utf16(items[c - a].Groups[1].Value);
                    }
                }
            return map;
        }

        static string Utf16(string hex)
        {
            var sb = new StringBuilder();
            for (int i = 0; i + 4 <= hex.Length; i += 4) sb.Append((char)Convert.ToInt32(hex.Substring(i, 4), 16));
            if (hex.Length == 2) sb.Append((char)Convert.ToInt32(hex, 16));
            return sb.ToString();
        }

        static readonly Encoding Win1252 = Encoding.GetEncoding(1252);

        static string DecodeString(byte[] raw, PdfFont f)
        {
            if (f == null || f.Map == null)
            {
                if (f != null && f.Bytes == 2) return "";
                return Win1252.GetString(raw);
            }
            var sb = new StringBuilder();
            for (int i = 0; i + f.Bytes <= raw.Length; i += f.Bytes)
            {
                int code = f.Bytes == 2 ? (raw[i] << 8) | raw[i + 1] : raw[i];
                string v;
                if (f.Map.TryGetValue(code, out v)) sb.Append(v);
                else if (f.Bytes == 1 && code >= 32) sb.Append(Win1252.GetString(raw, i, 1));
            }
            return sb.ToString();
        }

        // Пройти оператори сторінки й зібрати текст.
        static string ContentText(byte[] c, Dictionary<string, PdfFont> fonts)
        {
            var sb = new StringBuilder();
            var stack = new List<object>();   // операнди: double, string (ім'я), byte[] (рядок), List<object> (масив)
            PdfFont font = null;
            double lastY = double.NaN, lineY = 0;
            int i = 0, n = c.Length;
            List<object> arr = null;
            Action newline = () => { if (sb.Length > 0 && sb[sb.Length - 1] != '\n') sb.Append('\n'); };
            Action<string> emit = t => { if (t.Length > 0) sb.Append(t); };
            while (i < n)
            {
                byte b = c[i];
                if (b == ' ' || b == '\n' || b == '\r' || b == '\t' || b == '\f' || b == 0) { i++; continue; }
                if (b == '%') { while (i < n && c[i] != '\n' && c[i] != '\r') i++; continue; }
                var target = arr ?? stack;
                if (b == '(')
                {
                    var bytes = new List<byte>();
                    int depth = 1;
                    i++;
                    while (i < n && depth > 0)
                    {
                        byte x = c[i];
                        if (x == '\\' && i + 1 < n)
                        {
                            byte e = c[++i];
                            if (e >= '0' && e <= '7')
                            {
                                int v = 0, k = 0;
                                while (k < 3 && i < n && c[i] >= '0' && c[i] <= '7') { v = v * 8 + (c[i] - '0'); i++; k++; }
                                bytes.Add((byte)v);
                                continue;
                            }
                            if (e == 'n') bytes.Add((byte)'\n'); else if (e == 'r') bytes.Add((byte)'\r'); else if (e == 't') bytes.Add((byte)'\t');
                            else if (e == 'b' || e == 'f') { }
                            else if (e == '\r' || e == '\n') { if (e == '\r' && i + 1 < n && c[i + 1] == '\n') i++; }
                            else bytes.Add(e);
                            i++;
                            continue;
                        }
                        if (x == '(') depth++;
                        else if (x == ')') { depth--; if (depth == 0) { i++; break; } }
                        bytes.Add(x);
                        i++;
                    }
                    target.Add(bytes.ToArray());
                    continue;
                }
                if (b == '<' && i + 1 < n && c[i + 1] != '<')
                {
                    int e = i + 1;
                    var hex = new StringBuilder();
                    while (e < n && c[e] != '>') { if (Uri.IsHexDigit((char)c[e])) hex.Append((char)c[e]); e++; }
                    if (hex.Length % 2 == 1) hex.Append('0');
                    var bytes = new byte[hex.Length / 2];
                    for (int k = 0; k < bytes.Length; k++) bytes[k] = Convert.ToByte(hex.ToString(k * 2, 2), 16);
                    target.Add(bytes);
                    i = e + 1;
                    continue;
                }
                if (b == '<' || b == '>') { i += 2; continue; }   // словники << >> (у вмісті — лише параметри)
                if (b == '[') { arr = new List<object>(); i++; continue; }
                if (b == ']') { if (arr != null) { stack.Add(arr); arr = null; } i++; continue; }
                if (b == '/')
                {
                    int e = i + 1;
                    while (e < n && !IsDelim(c[e])) e++;
                    target.Add("/" + Latin1.GetString(c, i + 1, e - i - 1));
                    i = e;
                    continue;
                }
                if ((b >= '0' && b <= '9') || b == '-' || b == '+' || b == '.')
                {
                    int e = i + 1;
                    while (e < n && ((c[e] >= '0' && c[e] <= '9') || c[e] == '.')) e++;
                    double v;
                    double.TryParse(Latin1.GetString(c, i, e - i), NumberStyles.Float, CultureInfo.InvariantCulture, out v);
                    target.Add(v);
                    i = e;
                    continue;
                }
                // Оператор.
                int oe = i;
                while (oe < n && !IsDelim(c[oe])) oe++;
                if (oe == i) { i++; continue; }
                string op = Latin1.GetString(c, i, oe - i);
                i = oe;
                switch (op)
                {
                    case "BI":   // вбудована картинка — пропускаємо до EI
                        while (i + 2 < n && !(c[i] == 'E' && c[i + 1] == 'I' && IsDelim(c[i + 2]) && IsDelim(c[i - 1]))) i++;
                        i += 2;
                        break;
                    case "Tf":
                        if (stack.Count >= 2 && stack[stack.Count - 2] is string)
                        {
                            PdfFont f;
                            fonts.TryGetValue(((string)stack[stack.Count - 2]).Substring(1), out f);
                            font = f;
                        }
                        break;
                    case "Tj": case "'": case "\"":
                        if (op != "Tj") newline();
                        if (stack.Count > 0 && stack[stack.Count - 1] is byte[]) emit(DecodeString((byte[])stack[stack.Count - 1], font));
                        break;
                    case "TJ":
                        if (stack.Count > 0 && stack[stack.Count - 1] is List<object>)
                            foreach (object o in (List<object>)stack[stack.Count - 1])
                            {
                                if (o is byte[]) emit(DecodeString((byte[])o, font));
                                else if (o is double && (double)o < -200 && sb.Length > 0 && sb[sb.Length - 1] != ' ' && sb[sb.Length - 1] != '\n') sb.Append(' ');
                            }
                        break;
                    case "Td": case "TD":
                        if (stack.Count >= 2 && stack[stack.Count - 1] is double)
                        {
                            double dy = (double)stack[stack.Count - 1];
                            lineY += dy;
                            if (Math.Abs(dy) > 0.5) newline();   // зсув лише по x — це кернінг усередині рядка, пробіли й так є в тексті
                        }
                        break;
                    case "Tm":
                        if (stack.Count >= 6 && stack[stack.Count - 1] is double)
                        {
                            double y = (double)stack[stack.Count - 1];
                            if (!double.IsNaN(lastY) && Math.Abs(y - lastY) > 0.5) newline();
                            else if (!double.IsNaN(lastY) && sb.Length > 0 && sb[sb.Length - 1] != ' ' && sb[sb.Length - 1] != '\n') sb.Append(' ');
                            lastY = y;
                            lineY = y;
                        }
                        break;
                    case "T*": newline(); break;
                    case "ET": lastY = lineY; break;
                }
                stack.Clear();
                arr = null;
            }
            return sb.ToString();
        }

        static bool IsDelim(byte x)
        {
            return x == ' ' || x == '\n' || x == '\r' || x == '\t' || x == '\f' || x == 0 || x == '(' || x == ')' || x == '<' || x == '>' ||
                   x == '[' || x == ']' || x == '/' || x == '%' || x == '{' || x == '}';
        }
    }
}
