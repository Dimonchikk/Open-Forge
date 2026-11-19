// Open Forge — агент: модель працює з файлами в ОДНІЙ обраній папці через інструменти Ollama (tool calling).
// Інструменти: створити/змінити файл, прочитати, список файлів, видалити, виконати команду (cmd).
// Рівні дозволів:
//   Manual — кожну дію підтверджуєш ти;
//   Auto   — файли створює сам, а видалення й команди — лише з твого дозволу;
//   Full   — без підтверджень (лише для папок, які не шкода).
// Усі шляхи перевіряються: вийти за межі обраної папки агент не може.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace OpenForge
{
    enum AgentPermission { Manual, Auto, Full }

    class AgentRun
    {
        public const int MaxSteps = 30;
        readonly string root, model, instruction;
        readonly AgentPermission permission;
        readonly IList<Turn> history;
        static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };

        // Підтвердження дії: (назва інструмента, опис) → дозволено? Викликається з фонового потоку.
        public Func<string, string, bool> Confirm = (n, d) => false;

        // Що бачить користувач: текст відповіді, кроки з результатом. Пише фоновий потік, читає вікно.
        readonly object sync = new object();
        readonly StringBuilder log = new StringBuilder();
        int version;
        public volatile bool Done;
        public string Error;
        public bool ToolsUnsupported;
        public readonly List<string> Changed = new List<string>();
        volatile bool cancel;
        HttpWebRequest req;
        Process proc;

        public AgentRun(string folder, string model, AgentPermission permission, IList<Turn> history, string instruction)
        {
            root = Path.GetFullPath(folder).TrimEnd('\\', '/');
            this.model = model;
            this.permission = permission;
            this.history = history;
            this.instruction = instruction;
        }

        public string Folder { get { return root; } }
        public List<string> TaskImages;   // картинки до завдання (якщо модель їх розуміє)
        public bool Cancelled { get { return cancel; } }

        public void Cancel()
        {
            cancel = true;
            var r = req;
            if (r != null) try { r.Abort(); } catch { }
            var p = proc;
            if (p != null) try { if (!p.HasExited) p.Kill(); } catch { }
        }

        public string TakeIfChanged(ref int seen)
        {
            lock (sync)
            {
                if (version == seen) return null;
                seen = version;
                return log.ToString();
            }
        }

        public string Text { get { lock (sync) return log.ToString(); } }

        void Write(string s)
        {
            lock (sync) { log.Append(s); version++; }
        }

        // ── безпека ──────────────────────────────────────────────────────────────

        bool NeedsConfirm(string tool)
        {
            if (permission == AgentPermission.Full) return false;
            if (permission == AgentPermission.Manual) return true;
            return tool == "run_command" || tool == "delete_file" || tool == "start_program";
        }

        string SafePath(string rel)
        {
            if (string.IsNullOrEmpty(rel)) throw new Exception("не вказано шлях");
            string full = Path.GetFullPath(Path.Combine(root, rel.TrimStart('\\', '/')));
            // Порівнюємо з роздільником у кінці: інакше «Project2» пройшов би як усередині «Project».
            if (!full.Equals(root, StringComparison.OrdinalIgnoreCase) &&
                !full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new Exception("шлях «" + rel + "» виходить за межі папки проєкту");
            return full;
        }

        // ── інструменти ──────────────────────────────────────────────────────────

        static Dictionary<string, object> Tool(string name, string desc, Dictionary<string, object> props, params string[] required)
        {
            return new Dictionary<string, object>
            {
                { "type", "function" },
                { "function", new Dictionary<string, object>
                    {
                        { "name", name }, { "description", desc },
                        { "parameters", new Dictionary<string, object> { { "type", "object" }, { "properties", props }, { "required", required } } },
                    } },
            };
        }

        static Dictionary<string, object> Str(string desc)
        {
            var d = new Dictionary<string, object> { { "type", "string" } };
            if (desc != null) d["description"] = desc;
            return d;
        }

        static readonly object[] Tools =
        {
            Tool("create_file", "Create or overwrite a text file in the project folder (folders are created automatically).",
                new Dictionary<string, object> { { "path", Str("Path relative to the project folder.") }, { "content", Str("Full file content.") } }, "path", "content"),
            Tool("read_file", "Read a text file from the project folder.", new Dictionary<string, object> { { "path", Str(null) } }, "path"),
            Tool("list_files", "List files in the project folder (or a subfolder).", new Dictionary<string, object> { { "subdir", Str("Optional subfolder.") } }),
            Tool("run_command", "Run a shell command in the project folder (Windows cmd). Use for installing packages, running scripts, tests.",
                new Dictionary<string, object> { { "command", Str(null) } }, "command"),
            Tool("delete_file", "Delete a file from the project folder.", new Dictionary<string, object> { { "path", Str(null) } }, "path"),
            Tool("screenshot_page", "Take a screenshot of a web page or local HTML file (rendered in a headless browser) to check how it looks. " +
                 "The screenshot is shown to the user and, if you can see images, to you.",
                new Dictionary<string, object> { { "target", Str("Path to an .html file in the project folder, or an http(s) URL (e.g. http://localhost:5173).") },
                                                 { "width", new Dictionary<string, object> { { "type", "integer" }, { "description", "Viewport width in px (default 1280; use 390 for a phone)." } } } }, "target"),
            Tool("start_program", "Start a program or server in the project folder WITHOUT waiting for it to finish (e.g. a GUI app or 'npm run dev'). " +
                 "Use screenshot_window afterwards to see a GUI app.",
                new Dictionary<string, object> { { "command", Str(null) } }, "command"),
            Tool("screenshot_window", "Take a screenshot of an open window whose title contains the given text (e.g. a program you started).",
                new Dictionary<string, object> { { "title", Str("Part of the window title.") } }, "title"),
        };

        static string Arg(Dictionary<string, object> a, string key)
        {
            object v;
            return a != null && a.TryGetValue(key, out v) && v != null ? Convert.ToString(v) : "";
        }

        public static string Describe(string name, Dictionary<string, object> a)
        {
            switch (name)
            {
                case "create_file": return "Створює файл " + Arg(a, "path");
                case "read_file": return "Читає " + Arg(a, "path");
                case "list_files": return "Дивиться файли" + (Arg(a, "subdir").Length > 0 ? " у " + Arg(a, "subdir") : " в папці");
                case "run_command": return "Виконує команду: " + Arg(a, "command");
                case "delete_file": return "Видаляє " + Arg(a, "path");
                case "screenshot_page": return "Робить скріншот " + Arg(a, "target");
                case "start_program": return "Запускає програму: " + Arg(a, "command");
                case "screenshot_window": return "Фотографує вікно «" + Arg(a, "title") + "»";
            }
            return name;
        }

        bool Execute(string name, Dictionary<string, object> a, out string result)
        {
            try
            {
                switch (name)
                {
                    case "create_file":
                        {
                            string p = SafePath(Arg(a, "path"));
                            Directory.CreateDirectory(Path.GetDirectoryName(p));
                            string content = Arg(a, "content");
                            File.WriteAllText(p, content, new UTF8Encoding(false));
                            Changed.Add(Arg(a, "path"));
                            result = "створено (" + Encoding.UTF8.GetByteCount(content) + " байт)";
                            return true;
                        }
                    case "read_file":
                        {
                            string text = File.ReadAllText(SafePath(Arg(a, "path")));
                            // Великі файли обрізаємо, щоб не переповнити пам'ять розмови моделі.
                            int max = Math.Max(4000, Ollama.NumCtx);   // ≈ чверть контексту в токенах
                            result = text.Length > max ? text.Substring(0, max) + "\n…(обрізано: файл довший за " + max + " символів)" : text;
                            return true;
                        }
                    case "list_files":
                        {
                            string sub = Arg(a, "subdir");
                            string dir = sub.Length > 0 ? SafePath(sub) : root;
                            var sb = new StringBuilder();
                            int n = 0;
                            foreach (string f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
                            {
                                string rel = f.Substring(root.Length).TrimStart('\\', '/');
                                if (rel.StartsWith(".git\\") || rel.Contains("\\node_modules\\") || rel.StartsWith("node_modules\\")) continue;
                                sb.AppendLine(rel);
                                if (++n >= 500) { sb.AppendLine("…"); break; }
                            }
                            result = n == 0 ? "(порожньо)" : sb.ToString().TrimEnd();
                            return true;
                        }
                    case "run_command": return RunCommand(Arg(a, "command"), out result);
                    case "screenshot_page": return ShotPage(Arg(a, "target"), Arg(a, "width"), out result);
                    case "screenshot_window": return ShotWindow(Arg(a, "title"), out result);
                    case "start_program":
                        {
                            var psi = new ProcessStartInfo("cmd.exe", "/d /c \"" + Arg(a, "command") + "\"");
                            psi.WorkingDirectory = root;
                            psi.UseShellExecute = false;
                            psi.CreateNoWindow = true;
                            var sp = Process.Start(psi);
                            started.Add(sp);
                            Thread.Sleep(2500);   // дати програмі відкрити вікно / серверу піднятися
                            result = sp.HasExited ? "програма вже завершилась (код " + sp.ExitCode + ")" : "запущено (працює у фоні)";
                            return !sp.HasExited || sp.ExitCode == 0;
                        }
                    case "delete_file":
                        File.Delete(SafePath(Arg(a, "path")));
                        Changed.Add(Arg(a, "path"));
                        result = "видалено";
                        return true;
                }
                result = "невідомий інструмент";
                return false;
            }
            catch (Exception ex)
            {
                result = "помилка: " + ex.Message;
                return false;
            }
        }

        bool RunCommand(string command, out string result)
        {
            var psi = new ProcessStartInfo("cmd.exe", "/d /s /c \"chcp 65001 >nul & " + command + "\"");
            psi.WorkingDirectory = root;
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = psi.RedirectStandardError = true;
            psi.StandardOutputEncoding = psi.StandardErrorEncoding = Encoding.UTF8;
            var output = new StringBuilder();
            using (var p = new Process { StartInfo = psi })
            {
                p.OutputDataReceived += (s, e) => { if (e.Data != null) lock (output) output.AppendLine(e.Data); };
                p.ErrorDataReceived += (s, e) => { if (e.Data != null) lock (output) output.AppendLine(e.Data); };
                p.Start();
                proc = p;
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();
                bool finished = p.WaitForExit(120000);
                if (!finished) try { p.Kill(); } catch { }
                p.WaitForExit(2000);
                proc = null;
                string o;
                lock (output) o = output.ToString().Trim();
                if (o.Length > 6000) o = "…" + o.Substring(o.Length - 6000);
                if (!finished) { result = (o.Length > 0 ? o + "\n" : "") + "команда працювала довше 2 хвилин і була зупинена"; return false; }
                result = (o.Length > 0 ? o : "(без виводу)") + "\n[код виходу " + p.ExitCode + "]";
                return p.ExitCode == 0;
            }
        }

        // ── скріншоти ────────────────────────────────────────────────────────────

        public bool ModelSeesImages = true;            // false — модель точно не бачить картинок
        readonly List<string> shots = new List<string>();
        readonly List<Process> started = new List<Process>();
        string pendingShot;                             // картинка, яку треба показати моделі після відповіді інструмента
        readonly List<StepDetail> stepDetails = new List<StepDetail>();
        int stepsVersion;

        // Подробиці кроків (команда й вивід, вміст файлу, скріншот) — для розгортання кроку в чаті.
        public List<StepDetail> StepDetails(ref int seenVersion)
        {
            lock (sync)
            {
                if (seenVersion == stepsVersion) return null;
                seenVersion = stepsVersion;
                return new List<StepDetail>(stepDetails);
            }
        }

        static string Cap(string s, int max)
        {
            s = (s ?? "").Replace("\r\n", "\n").TrimEnd();
            return s.Length > max ? s.Substring(0, max) + "\n… (обрізано, ще " + (s.Length - max) + " символів)" : s;
        }

        void AddDetail(string name, Dictionary<string, object> args, bool ok, string result)
        {
            string body;
            if (!ok) body = result;
            else switch (name)
            {
                case "create_file": body = Arg(args, "content"); break;
                case "run_command": case "start_program":
                    body = "> " + Arg(args, "command") + (result.Trim().Length > 0 ? "\n" + result.Trim() : ""); break;
                default: body = result; break;
            }
            var d = new StepDetail { Kind = name, Ok = ok, Text = Cap(body, 8000), Path = Arg(args, "path") };
            lock (sync)
            {
                if (pendingShot != null) d.Shot = shots.Count - 1;
                stepDetails.Add(d);
                stepsVersion++;
            }
        }
        int shotsVersion;

        // Скріншоти, зроблені агентом (для показу в чаті).
        public List<string> Shots(ref int seenVersion)
        {
            lock (sync)
            {
                if (seenVersion == shotsVersion) return null;
                seenVersion = shotsVersion;
                return new List<string>(shots);
            }
        }

        void AddShot(string b64)
        {
            lock (sync) { shots.Add(b64); shotsVersion++; }
            pendingShot = b64;
        }

        internal static string FindBrowser()
        {
            string env = Environment.GetEnvironmentVariable("OPENFORGE_BROWSER");
            if (!string.IsNullOrEmpty(env) && File.Exists(env)) return env;
            var dirs = new[] { Environment.GetEnvironmentVariable("ProgramFiles(x86)"), Environment.GetEnvironmentVariable("ProgramFiles"),
                               Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) };
            foreach (string rel in new[] { "Microsoft\\Edge\\Application\\msedge.exe", "Google\\Chrome\\Application\\chrome.exe" })
                foreach (string d in dirs)
                    if (!string.IsNullOrEmpty(d) && File.Exists(Path.Combine(d, rel))) return Path.Combine(d, rel);
            return null;
        }

        bool ShotPage(string target, string widthArg, out string result)
        {
            string browser = FindBrowser();
            if (browser == null) { result = "не знайдено Microsoft Edge чи Chrome для скріншота"; return false; }
            string url = target.Trim();
            if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) &&
                !url.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
            {
                string path = SafePath(url);
                if (!File.Exists(path)) { result = "файл не знайдено: " + target; return false; }
                url = new Uri(path).AbsoluteUri;
            }
            int width;
            if (!int.TryParse(widthArg, out width) || width < 320 || width > 2560) width = 1280;
            int height = width <= 500 ? 844 : 800;
            string png = Path.Combine(Path.GetTempPath(), "openforge_shot_" + Guid.NewGuid().ToString("N") + ".png");
            string profile = Path.Combine(Path.GetTempPath(), "openforge_browser");
            var psi = new ProcessStartInfo(browser, "--headless=new --disable-gpu --hide-scrollbars --no-first-run --user-data-dir=\"" + profile +
                "\" --window-size=" + width + "," + height + " --screenshot=\"" + png + "\" \"" + url + "\"");
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardError = psi.RedirectStandardOutput = true;
            using (var p = Process.Start(psi))
            {
                p.ErrorDataReceived += (s, e) => { };
                p.OutputDataReceived += (s, e) => { };
                p.BeginErrorReadLine();
                p.BeginOutputReadLine();
                if (!p.WaitForExit(45000)) try { p.Kill(); } catch { }
            }
            string b64 = File.Exists(png) ? Pictures.FromFile(png) : null;
            try { File.Delete(png); } catch { }
            if (b64 == null) { result = "не вдалося зробити скріншот " + target; return false; }
            AddShot(b64);
            result = "скріншот зроблено (" + width + "×" + height + ") і показано користувачу" + (ModelSeesImages ? "; зображення надіслано тобі наступним повідомленням" : "");
            return true;
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd, out RECT r);
        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)] struct RECT { public int L, T, R, B; }

        bool ShotWindow(string title, out string result)
        {
            Process found = null;
            foreach (var p in Process.GetProcesses())
            {
                try
                {
                    if (p.MainWindowHandle != IntPtr.Zero && p.MainWindowTitle.IndexOf(title, StringComparison.OrdinalIgnoreCase) >= 0) { found = p; break; }
                }
                catch { }
            }
            if (found == null) { result = "вікно з назвою «" + title + "» не знайдено"; return false; }
            RECT r;
            if (!GetWindowRect(found.MainWindowHandle, out r) || r.R - r.L < 10 || r.B - r.T < 10) { result = "вікно згорнуте або порожнє"; return false; }
            using (var bmp = new System.Drawing.Bitmap(r.R - r.L, r.B - r.T))
            {
                using (var g = System.Drawing.Graphics.FromImage(bmp))
                {
                    IntPtr hdc = g.GetHdc();
                    try { PrintWindow(found.MainWindowHandle, hdc, 2); }   // 2 — PW_RENDERFULLCONTENT: працює й для сучасних вікон
                    finally { g.ReleaseHdc(hdc); }
                }
                AddShot(Pictures.Encode(bmp));
            }
            result = "знімок вікна «" + found.MainWindowTitle + "» зроблено й показано користувачу" + (ModelSeesImages ? "; зображення надіслано тобі наступним повідомленням" : "");
            return true;
        }

        // ── цикл ─────────────────────────────────────────────────────────────────

        Dictionary<string, object> Ask(List<object> messages)
        {
            // Потоком, як у чаті: думки й текст моделі видно одразу, а не лише після всієї відповіді
            // (на великій моделі один крок може тривати хвилини).
            var body = new Dictionary<string, object> { { "model", model }, { "messages", messages }, { "tools", Tools }, { "stream", true }, { "options", Ollama.Options() } };
            var think = Ollama.ThinkValue(model);
            if (think != null) body["think"] = think;
            byte[] data = Encoding.UTF8.GetBytes(Json.Serialize(body));
            var r = (HttpWebRequest)WebRequest.Create(Ollama.Host + "/api/chat");
            r.Method = "POST";
            r.Proxy = null;
            r.ContentType = "application/json";
            r.ContentLength = data.Length;
            // Велика модель, що частково в RAM, може довго «читати» розмову перед першим словом — чекаємо до 60 хв;
            // далі слова йдуть потоком, і між ними — до 30 хв.
            r.Timeout = 60 * 60 * 1000;
            r.ReadWriteTimeout = 30 * 60 * 1000;
            req = r;
            using (var rs = r.GetRequestStream()) rs.Write(data, 0, data.Length);
            try
            {
                var content = new StringBuilder();
                var calls = new List<object>();
                bool thinking = false, wroteContent = false;
                using (var resp = r.GetResponse())
                using (var sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                {
                    string line;
                    while (!cancel && (line = sr.ReadLine()) != null)
                    {
                        if (line.Length == 0) continue;
                        var d = Json.DeserializeObject(line) as Dictionary<string, object>;
                        if (d == null) continue;
                        if (d.ContainsKey("error")) throw new Exception(Convert.ToString(d["error"]));
                        var msg = d.ContainsKey("message") ? d["message"] as Dictionary<string, object> : null;
                        string th = Arg(msg, "thinking"), c = Arg(msg, "content");
                        if (th.Length > 0)
                        {
                            if (!thinking) { Write("<think>"); thinking = true; }   // без переносу — щоб на місці думок не лишалось порожнього рядка
                            Write(th);
                        }
                        var tc = msg != null && msg.ContainsKey("tool_calls") ? msg["tool_calls"] as object[] : null;
                        if ((c.Length > 0 || tc != null) && thinking) { Write("</think>"); thinking = false; }
                        if (c.Length > 0)
                        {
                            if (!wroteContent && content.Length == 0) Write(Text.Length > 0 ? "\n\n" : "");
                            wroteContent = true;
                            content.Append(c);
                            Write(c);
                        }
                        if (tc != null) calls.AddRange(tc);
                        if (d.ContainsKey("done") && d["done"] is bool && (bool)d["done"]) break;
                    }
                }
                if (thinking) Write("</think>");
                var result = new Dictionary<string, object> { { "role", "assistant" }, { "content", StripThink(content.ToString()).Trim() } };
                if (calls.Count > 0) result["tool_calls"] = calls.ToArray();
                return new Dictionary<string, object> { { "message", result } };
            }
            catch (WebException ex)
            {
                var er = ex.Response as HttpWebResponse;
                if (er == null) throw;
                string err;
                using (var sr = new StreamReader(er.GetResponseStream(), Encoding.UTF8)) err = sr.ReadToEnd();
                try
                {
                    var d = Json.DeserializeObject(err) as Dictionary<string, object>;
                    if (d != null && d.ContainsKey("error")) err = Convert.ToString(d["error"]);
                }
                catch { }
                if (err.IndexOf("does not support tools", StringComparison.OrdinalIgnoreCase) >= 0) ToolsUnsupported = true;
                throw new Exception(err);
            }
            finally { req = null; }
        }

        // Блокує до кінця роботи — викликати з фонового потоку.
        public void Run(string task)
        {
            try
            {
                Directory.CreateDirectory(root);
                string system = "You are a coding agent working on the user's Windows computer inside ONE project folder. " +
                    "Use the tools to actually create and edit files and run commands — do not just print code. " +
                    "Paths are relative to the project folder. Work step by step, check results, and when the task is finished " +
                    "reply with a short summary of what you did, in the user's language.\n" +
                    "After you build or change a web page, call screenshot_page to check how it looks" + (ModelSeesImages ? " and fix what looks wrong" : "") + ". " +
                    "For a desktop app, use start_program and then screenshot_window.\nProject folder: " + root;
                if (!string.IsNullOrEmpty(instruction) && instruction.Trim().Length > 0) system += "\n\nUser instruction: " + instruction;
                var messages = new List<object> { new Dictionary<string, object> { { "role", "system" }, { "content", system } } };
                foreach (var t in history) messages.Add(t.ToJson());
                messages.Add(new Turn("user", task) { Images = TaskImages }.ToJson());

                for (int step = 0; step < MaxSteps; step++)
                {
                    if (cancel) return;
                    var resp = Ask(messages);
                    var msg = resp != null && resp.ContainsKey("message") ? resp["message"] as Dictionary<string, object> : null;
                    if (msg == null) throw new Exception("Ollama повернула порожню відповідь");
                    messages.Add(msg);
                    // Текст моделі вже показано потоком у Ask.

                    var calls = msg.ContainsKey("tool_calls") ? msg["tool_calls"] as object[] : null;
                    if (calls == null || calls.Length == 0) return;
                    foreach (object co in calls)
                    {
                        if (cancel) return;
                        var call = co as Dictionary<string, object>;
                        var fn = call != null && call.ContainsKey("function") ? call["function"] as Dictionary<string, object> : null;
                        string name = Arg(fn, "name");
                        object rawArgs = fn != null && fn.ContainsKey("arguments") ? fn["arguments"] : null;
                        var args = rawArgs as Dictionary<string, object>;
                        if (args == null && rawArgs is string)
                            try { args = Json.DeserializeObject((string)rawArgs) as Dictionary<string, object>; } catch { }
                        if (args == null) args = new Dictionary<string, object>();

                        string title = Describe(name, args);
                        Write((Text.Length > 0 ? "\n" : "") + "▸ " + title);
                        bool ok;
                        string result;
                        if (NeedsConfirm(name) && !Confirm(name, title)) { ok = false; result = "користувач не дозволив цю дію"; }
                        else ok = Execute(name, args, out result);
                        if (cancel) return;
                        string first = result.Trim();
                        int nl = first.IndexOf('\n');
                        if (nl >= 0) first = first.Substring(0, nl);
                        if (first.Length > 140) first = first.Substring(0, 140) + "…";
                        Write(ok ? "  ✓" + (name == "run_command" ? "  " + first : "") : "  ✗ " + first);
                        AddDetail(name, args, ok, result);
                        messages.Add(new Dictionary<string, object> { { "role", "tool" }, { "content", result }, { "tool_name", name } });
                        if (pendingShot != null && ModelSeesImages)   // показати моделі, що вийшло
                            messages.Add(new Turn("user", "Ось скріншот результату (" + title + "). Перевір, чи все виглядає правильно.") { Images = new List<string> { pendingShot } }.ToJson());
                        pendingShot = null;
                    }
                }
                Write("\n\nЗупинився: досягнуто ліміту кроків (" + MaxSteps + ").");
            }
            catch (Exception ex)
            {
                if (!cancel) Error = ex is WebException && ((WebException)ex).Status == WebExceptionStatus.ConnectFailure
                    ? "Ollama не запущена. Запусти її й спробуй ще раз." : Ollama.Friendly(ex.Message);
            }
            finally { Done = true; }
        }

        static string StripThink(string s)
        {
            int a;
            while ((a = s.IndexOf("<think>", StringComparison.Ordinal)) >= 0)
            {
                int b = s.IndexOf("</think>", a, StringComparison.Ordinal);
                s = b < 0 ? s.Substring(0, a) : s.Substring(0, a) + s.Substring(b + 8);
            }
            return s;
        }
    }
}
