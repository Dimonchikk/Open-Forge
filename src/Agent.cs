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
            return tool == "run_command" || tool == "delete_file";
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

        // ── цикл ─────────────────────────────────────────────────────────────────

        Dictionary<string, object> Ask(List<object> messages)
        {
            var body = new Dictionary<string, object> { { "model", model }, { "messages", messages }, { "tools", Tools }, { "stream", false }, { "options", Ollama.Options() } };
            byte[] data = Encoding.UTF8.GetBytes(Json.Serialize(body));
            var r = (HttpWebRequest)WebRequest.Create(Ollama.Host + "/api/chat");
            r.Method = "POST";
            r.Proxy = null;
            r.ContentType = "application/json";
            r.ContentLength = data.Length;
            r.Timeout = r.ReadWriteTimeout = 15 * 60 * 1000;
            req = r;
            using (var rs = r.GetRequestStream()) rs.Write(data, 0, data.Length);
            try
            {
                using (var resp = r.GetResponse())
                using (var sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                    return Json.DeserializeObject(sr.ReadToEnd()) as Dictionary<string, object>;
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
                    "reply with a short summary of what you did, in the user's language.\nProject folder: " + root;
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
                    string content = Arg(msg, "content");
                    content = StripThink(content).Trim();
                    if (content.Length > 0) Write((Text.Length > 0 ? "\n\n" : "") + content);

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
                        messages.Add(new Dictionary<string, object> { { "role", "tool" }, { "content", result }, { "tool_name", name } });
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
