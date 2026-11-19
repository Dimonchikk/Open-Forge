// Open Forge — розмова з Ollama через її HTTP API (http://127.0.0.1:11434).
// Усі виклики блокуючі й робляться лише з фонових потоків. Відповідь моделі (NDJSON, рядок на токен)
// складається в ChatStream, а вікно забирає накопичений текст таймером кілька разів на секунду —
// тому навіть дуже довга відповідь не підвішує інтерфейс.
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace OpenForge
{
    class ModelInfo
    {
        public string Name;
        public long Size;              // байти на диску
        public string Family, Params, Quant;
        public double SizeGb { get { return Size / 1073741824.0; } }
    }

    class Turn
    {
        public string Role, Content;   // "system", "user", "assistant"
        public List<string> Images;    // картинки (JPEG base64) — лише для моделей, що їх розуміють
        public Turn(string role, string content) { Role = role; Content = content; }

        public Dictionary<string, object> ToJson()
        {
            var d = new Dictionary<string, object> { { "role", Role }, { "content", Content } };
            if (Images != null && Images.Count > 0) d["images"] = Images.ToArray();
            return d;
        }
    }

    // Одна відповідь моделі, що надходить частинами. Пише фоновий потік, читає вікно.
    class ChatStream
    {
        readonly StringBuilder buf = new StringBuilder();
        readonly object sync = new object();
        int version;
        volatile bool cancel;
        HttpWebRequest req;

        public volatile bool Done;
        public string Error;
        public long EvalCount, EvalNs, TotalNs;
        public DateTime Started = DateTime.UtcNow, FirstToken, Finished;

        public void Append(string s)
        {
            if (string.IsNullOrEmpty(s)) return;
            lock (sync)
            {
                if (buf.Length == 0) FirstToken = DateTime.UtcNow;
                buf.Append(s);
                version++;
            }
        }

        // Текст, якщо він змінився з минулого разу (seen — номер, який бачило вікно).
        public string TakeIfChanged(ref int seen)
        {
            lock (sync)
            {
                if (version == seen) return null;
                seen = version;
                return buf.ToString();
            }
        }

        public string Text { get { lock (sync) return buf.ToString(); } }

        public bool Cancelled { get { return cancel; } }

        public void Cancel()
        {
            cancel = true;
            var r = req;
            if (r != null) try { r.Abort(); } catch { }
        }

        internal void Attach(HttpWebRequest r) { req = r; if (cancel) try { r.Abort(); } catch { } }

        // Швидкість генерації за даними самої Ollama (токенів за секунду), 0 — невідомо.
        public double TokensPerSecond { get { return EvalNs > 0 ? EvalCount / (EvalNs / 1e9) : 0; } }
    }

    // Завантаження однієї моделі (ollama pull). Пише фоновий потік, читає вікно.
    class PullJob
    {
        public readonly string Model;
        public volatile bool Done;
        public volatile string Status = "Підключаюся…";
        public string Error;
        public long Total, Completed;
        volatile bool cancel;
        HttpWebRequest req;
        internal readonly Dictionary<string, long[]> Layers = new Dictionary<string, long[]>();

        public PullJob(string model) { Model = model; }

        public bool Cancelled { get { return cancel; } }
        public double Fraction { get { return Total > 0 ? Math.Min(1.0, (double)Completed / Total) : 0; } }

        public void Cancel()
        {
            cancel = true;
            var r = req;
            if (r != null) try { r.Abort(); } catch { }
        }

        internal void Attach(HttpWebRequest r) { req = r; if (cancel) try { r.Abort(); } catch { } }
    }

    static class Ollama
    {
        public static string Host = "http://127.0.0.1:11434";

        // «Пам'ять розмови» (контекст) у токенах. Ollama за замовчуванням дає лише 4096 — агенту й довгим чатам
        // цього замало, тож просимо явно. Більше контексту — більше відеопам'яті (на 8B-моделі 16K ≈ +2 ГБ).
        public static readonly int[] ContextSizes = { 4096, 8192, 16384, 32768 };
        public static int NumCtx { get { int v = Settings.GetInt("NumCtx", 16384); return Array.IndexOf(ContextSizes, v) >= 0 ? v : 16384; } }

        public static Dictionary<string, object> Options()
        {
            return new Dictionary<string, object> { { "num_ctx", NumCtx } };
        }

        // Зрозуміле пояснення поширених помилок Ollama.
        public static string Friendly(string err)
        {
            if (string.IsNullOrEmpty(err)) return err;
            if (err.IndexOf("context size", StringComparison.OrdinalIgnoreCase) >= 0 || err.IndexOf("context length", StringComparison.OrdinalIgnoreCase) >= 0)
                return "Розмова не влазить у пам'ять моделі (контекст " + (NumCtx / 1024) + "K токенів). Збільш «Пам'ять розмови» в «Налаштуваннях» або почни новий чат.";
            if (err.IndexOf("not found", StringComparison.OrdinalIgnoreCase) >= 0 && err.IndexOf("model", StringComparison.OrdinalIgnoreCase) >= 0)
                return "Модель не знайдено в Ollama — можливо, її видалили. Обери іншу або завантаж знову в розділі «Моделі».";
            if (err.IndexOf("memory", StringComparison.OrdinalIgnoreCase) >= 0 && err.IndexOf("requires more", StringComparison.OrdinalIgnoreCase) >= 0)
                return "Моделі не вистачає пам'яті. Зменш «Пам'ять розмови» в «Налаштуваннях» або обери меншу модель.\n(" + err + ")";
            if (err.IndexOf("timed out", StringComparison.OrdinalIgnoreCase) >= 0 || err.IndexOf("timeout", StringComparison.OrdinalIgnoreCase) >= 0)
                return "Модель надто довго не відповідала, і запит обірвався. Найчастіше це означає, що модель завелика для цього ПК " +
                       "(частина її в оперативній пам'яті, і вона рахує дуже повільно). Обери модель з позначкою «Швидко» або «Нормально (MoE)» " +
                       "у «Налаштування → Моделі», або зменш «Пам'ять розмови».";
            if (err.IndexOf("image", StringComparison.OrdinalIgnoreCase) >= 0 && (err.IndexOf("support", StringComparison.OrdinalIgnoreCase) >= 0 || err.IndexOf("vision", StringComparison.OrdinalIgnoreCase) >= 0))
                return "Ця модель не бачить картинок, а в розмові є картинка (можливо, в одному з попередніх повідомлень). " +
                       "Обери модель з позначкою «Розуміє картинки» або почни новий чат.\n(Ollama: " + err + ")";
            return err;
        }
        static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };

        static HttpWebRequest Request(string path, string method, int timeoutMs)
        {
            var req = (HttpWebRequest)WebRequest.Create(Host + path);
            req.Method = method;
            req.Proxy = null;   // локальний сервер — без системного проксі (інакше бувають секундні затримки)
            req.UserAgent = "OpenForge/" + AppVersion.Text;
            req.Timeout = timeoutMs;
            req.ReadWriteTimeout = timeoutMs;
            req.KeepAlive = false;
            return req;
        }

        static string GetText(string path, int timeoutMs)
        {
            var req = Request(path, "GET", timeoutMs);
            using (var resp = req.GetResponse())
            using (var sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                return sr.ReadToEnd();
        }

        static Dictionary<string, object> Obj(object o) { return o as Dictionary<string, object>; }

        static string Str(Dictionary<string, object> d, string key)
        {
            object v;
            return d != null && d.TryGetValue(key, out v) && v != null ? Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture) : null;
        }

        static long Num(Dictionary<string, object> d, string key)
        {
            object v;
            if (d == null || !d.TryGetValue(key, out v) || v == null) return 0;
            try { return Convert.ToInt64(v, System.Globalization.CultureInfo.InvariantCulture); } catch { return 0; }
        }

        // ── міркування («Думати перед відповіддю») ──────────────────────────────

        public class ThinkInfo
        {
            public bool Thinks;                               // модель уміє міркувати
            public List<string> Levels = new List<string>();  // рівні (наприклад low/medium/high у GPT-OSS); порожньо — лише увімк./вимк.
        }

        static readonly Dictionary<string, ThinkInfo> thinkCache = new Dictionary<string, ThinkInfo>(StringComparer.OrdinalIgnoreCase);

        public static ThinkInfo CachedThink(string model)
        {
            lock (thinkCache) { ThinkInfo t; return model != null && thinkCache.TryGetValue(model, out t) ? t : null; }
        }

        // Що вміє модель — з /api/show (capabilities і thinking.values). Якщо Ollama не відповіла — з каталогу.
        public static ThinkInfo Think(string model)
        {
            if (string.IsNullOrEmpty(model)) return new ThinkInfo();
            var cached = CachedThink(model);
            if (cached != null) return cached;
            var info = new ThinkInfo();
            try
            {
                var req = Request("/api/show", "POST", 8000);
                req.ContentType = "application/json";
                byte[] data = Encoding.UTF8.GetBytes(Json.Serialize(new Dictionary<string, object> { { "model", model } }));
                using (var rs = req.GetRequestStream()) rs.Write(data, 0, data.Length);
                Dictionary<string, object> d;
                using (var resp = req.GetResponse())
                using (var sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                    d = Obj(Json.DeserializeObject(sr.ReadToEnd()));
                object caps, th;
                if (d != null && d.TryGetValue("capabilities", out caps) && caps is object[])
                    foreach (object c in (object[])caps) if (Convert.ToString(c) == "thinking") info.Thinks = true;
                if (d != null && d.TryGetValue("thinking", out th) && Obj(th) != null)
                {
                    object vals;
                    if (Obj(th).TryGetValue("values", out vals) && vals is object[])
                        foreach (object v in (object[])vals)
                        {
                            if (v is string) info.Levels.Add((string)v);
                            else if (v is bool && (bool)v) info.Thinks = true;
                        }
                    if (info.Levels.Count > 0) info.Thinks = true;
                }
            }
            catch
            {
                var c = Catalog.Find(model);
                info.Thinks = c != null && c.Thinks;
                if (model.IndexOf("gpt-oss", StringComparison.OrdinalIgnoreCase) >= 0) info.Levels.AddRange(new[] { "low", "medium", "high" });
                return info;   // не кешуємо — спробуємо ще, коли Ollama запрацює
            }
            lock (thinkCache) thinkCache[model] = info;
            return info;
        }

        // Режим з налаштувань: auto — як вирішить модель, off — не думати, on — думати, або рівень (low/medium/high).
        public static string ThinkMode { get { return Settings.Get("ThinkMode", "auto"); } }

        // Значення поля «think» для запиту (null — не надсилати).
        public static object ThinkValue(string model)
        {
            string mode = ThinkMode;
            if (mode == "auto") return null;
            var info = CachedThink(model);
            if (info == null || !info.Thinks) return null;   // модель не міркує — нічого не надсилаємо (інакше Ollama може відмовити)
            if (mode == "off") return false;
            if (info.Levels.Count > 0)
            {
                if (mode == "on") return null;
                return info.Levels.Contains(mode) ? (object)mode : null;
            }
            return true;
        }

        // Версія запущеної Ollama або null, якщо вона не відповідає.
        public static string Version()
        {
            try { return Str(Obj(Json.DeserializeObject(GetText("/api/version", 2500))), "version"); }
            catch { return null; }
        }

        // Встановлені моделі (за абеткою). null — Ollama не відповіла.
        public static List<ModelInfo> Models()
        {
            try
            {
                var root = Obj(Json.DeserializeObject(GetText("/api/tags", 5000)));
                var list = new List<ModelInfo>();
                object arr;
                if (root != null && root.TryGetValue("models", out arr) && arr is object[])
                {
                    foreach (object o in (object[])arr)
                    {
                        var m = Obj(o);
                        if (m == null) continue;
                        var det = Obj(m.ContainsKey("details") ? m["details"] : null);
                        list.Add(new ModelInfo
                        {
                            Name = Str(m, "name") ?? Str(m, "model") ?? "?",
                            Size = Num(m, "size"),
                            Family = Str(det, "family"),
                            Params = Str(det, "parameter_size"),
                            Quant = Str(det, "quantization_level"),
                        });
                    }
                }
                list.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
                return list;
            }
            catch { return null; }
        }

        // Надіслати розмову й складати відповідь у stream. Блокує до кінця відповіді — викликати з фонового потоку.
        // Інструкція для моделі (system) іде першим повідомленням з роллю «system»: окреме поле «system»
        // в /api/chat Ollama ігнорує.
        public static void Chat(string model, string system, IList<Turn> history, ChatStream stream)
        {
            try
            {
                var msgs = new List<object>();
                if (!string.IsNullOrEmpty(system) && system.Trim().Length > 0)
                    msgs.Add(new Dictionary<string, object> { { "role", "system" }, { "content", system } });
                foreach (var t in history) msgs.Add(t.ToJson());
                var body = new Dictionary<string, object> { { "model", model }, { "messages", msgs }, { "stream", true }, { "options", Options() } };
                var think = ThinkValue(model);
                if (think != null) body["think"] = think;
                byte[] data = Encoding.UTF8.GetBytes(Json.Serialize(body));

                // Перша відповідь може йти довго (модель вантажиться у відеопам'ять), тож таймаут великий.
                var req = Request("/api/chat", "POST", 30 * 60 * 1000);
                req.Timeout = 60 * 60 * 1000;   // до першого слова: завантаження великої моделі й «читання» довгої розмови
                req.ContentType = "application/json";
                req.ContentLength = data.Length;
                stream.Attach(req);
                using (var rs = req.GetRequestStream()) rs.Write(data, 0, data.Length);
                using (var resp = req.GetResponse())
                using (var sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                {
                    string line;
                    bool thinking = false;   // нові Ollama віддають міркування окремим полем «thinking» — складаємо їх у <think>…</think>
                    while (!stream.Cancelled && (line = sr.ReadLine()) != null)
                    {
                        if (line.Length == 0) continue;
                        var d = Obj(Json.DeserializeObject(line));
                        if (d == null) continue;
                        string err = Str(d, "error");
                        if (err != null) { stream.Error = Friendly(err); break; }
                        var msg = Obj(d.ContainsKey("message") ? d["message"] : null);
                        string th = Str(msg, "thinking"), content = Str(msg, "content");
                        if (!string.IsNullOrEmpty(th)) { if (!thinking) { stream.Append("<think>"); thinking = true; } stream.Append(th); }
                        if (!string.IsNullOrEmpty(content) && thinking) { stream.Append("</think>"); thinking = false; }
                        stream.Append(content);
                        if (d.ContainsKey("done") && d["done"] is bool && (bool)d["done"])
                        {
                            stream.EvalCount = Num(d, "eval_count");
                            stream.EvalNs = Num(d, "eval_duration");
                            stream.TotalNs = Num(d, "total_duration");
                            break;
                        }
                    }
                }
            }
            catch (WebException ex)
            {
                if (!stream.Cancelled) stream.Error = Explain(ex);
            }
            catch (Exception ex)
            {
                if (!stream.Cancelled) stream.Error = ex.Message;
            }
            finally
            {
                stream.Finished = DateTime.UtcNow;
                stream.Done = true;
            }
        }

        static HttpWebRequest PostJson(string path, string method, object body, int timeoutMs)
        {
            byte[] data = Encoding.UTF8.GetBytes(Json.Serialize(body));
            var req = Request(path, method, timeoutMs);
            req.ContentType = "application/json";
            req.ContentLength = data.Length;
            using (var rs = req.GetRequestStream()) rs.Write(data, 0, data.Length);
            return req;
        }

        // Завантажити модель. Блокує до кінця — викликати з фонового потоку. Перерване завантаження
        // Ollama продовжує з того ж місця, тож «Скасувати» нічого не губить.
        public static void Pull(PullJob job)
        {
            try
            {
                var req = Request("/api/pull", "POST", 30 * 60 * 1000);
                job.Attach(req);
                byte[] data = Encoding.UTF8.GetBytes(Json.Serialize(new Dictionary<string, object> { { "model", job.Model }, { "stream", true } }));
                req.ContentType = "application/json";
                req.ContentLength = data.Length;
                using (var rs = req.GetRequestStream()) rs.Write(data, 0, data.Length);
                using (var resp = req.GetResponse())
                using (var sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                {
                    string line;
                    while (!job.Cancelled && (line = sr.ReadLine()) != null)
                    {
                        if (line.Length == 0) continue;
                        var d = Obj(Json.DeserializeObject(line));
                        if (d == null) continue;
                        string err = Str(d, "error");
                        if (err != null) { job.Error = err; break; }
                        string status = Str(d, "status") ?? "";
                        string digest = Str(d, "digest");
                        long total = Num(d, "total"), done = Num(d, "completed");
                        if (digest != null && total > 0)
                        {
                            lock (job.Layers) job.Layers[digest] = new[] { total, done };
                            long t = 0, c = 0;
                            lock (job.Layers) foreach (var v in job.Layers.Values) { t += v[0]; c += v[1]; }
                            job.Total = t;
                            job.Completed = c;
                        }
                        if (status == "success") { job.Status = "Готово"; break; }
                        job.Status = status.StartsWith("pulling") ? "Завантаження" : status.StartsWith("verifying") ? "Перевірка" :
                            status.StartsWith("writing") ? "Запис" : "Підготовка";
                    }
                }
            }
            catch (WebException ex) { if (!job.Cancelled) job.Error = Explain(ex); }
            catch (Exception ex) { if (!job.Cancelled) job.Error = ex.Message; }
            finally { job.Done = true; }
        }

        // Видалити модель з диска. null — вдалося, інакше текст помилки.
        public static string Delete(string model)
        {
            try
            {
                var req = PostJson("/api/delete", "DELETE", new Dictionary<string, object> { { "model", model } }, 30000);
                using (req.GetResponse()) { }
                return null;
            }
            catch (WebException ex) { return Explain(ex); }
            catch (Exception ex) { return ex.Message; }
        }

        // Зрозуміле пояснення помилки замість технічного тексту.
        static string Explain(WebException ex)
        {
            var resp = ex.Response as HttpWebResponse;
            if (resp != null)
            {
                try
                {
                    using (var sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                    {
                        string e = Str(Obj(Json.DeserializeObject(sr.ReadToEnd())), "error");
                        if (!string.IsNullOrEmpty(e)) return Friendly(e);
                    }
                }
                catch { }
                return "Ollama відповіла помилкою " + (int)resp.StatusCode + ".";
            }
            if (ex.Status == WebExceptionStatus.ConnectFailure) return "Ollama не запущена. Запусти її й спробуй ще раз.";
            if (ex.Status == WebExceptionStatus.Timeout) return Friendly("timed out");
            return ex.Message;
        }
    }
}
