// Open Forge — історія чатів: кожен чат — окремий файл JSON у %APPDATA%\OpenForge\chats.
// Нічого не йде в інтернет. Список тримається в пам'яті й оновлюється при кожному збереженні.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace OpenForge
{
    class ChatEntry
    {
        public string Id, Title;
        public DateTime Updated;
        public string Text;   // увесь текст чату (для пошуку)
    }

    static class ChatStore
    {
        public static string Dir { get { return Path.Combine(Settings.Dir, "chats"); } }
        static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
        static List<ChatEntry> cache;

        static string FileOf(string id) { return Path.Combine(Dir, id + ".json"); }

        public static string NewId() { return DateTime.Now.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture); }

        // Назва з першого повідомлення: один рядок, до 60 символів.
        public static string TitleFrom(string text)
        {
            string t = (text ?? "").Replace('\r', ' ').Replace('\n', ' ').Trim();
            while (t.Contains("  ")) t = t.Replace("  ", " ");
            if (t.Length > 60) t = t.Substring(0, 57).TrimEnd() + "…";
            return t.Length > 0 ? t : "Новий чат";
        }

        static Dictionary<string, object> ReadFile(string path)
        {
            try { return Json.DeserializeObject(File.ReadAllText(path, Encoding.UTF8)) as Dictionary<string, object>; }
            catch { return null; }
        }

        static string S(Dictionary<string, object> d, string k)
        {
            object v;
            return d != null && d.TryGetValue(k, out v) && v != null ? Convert.ToString(v, CultureInfo.InvariantCulture) : null;
        }

        // Усі чати, найновіші зверху.
        public static List<ChatEntry> All()
        {
            if (cache == null)
            {
                cache = new List<ChatEntry>();
                try
                {
                    if (Directory.Exists(Dir))
                        foreach (string f in Directory.GetFiles(Dir, "*.json"))
                        {
                            var d = ReadFile(f);
                            if (d == null) continue;
                            DateTime upd;
                            if (!DateTime.TryParse(S(d, "updated"), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out upd)) upd = File.GetLastWriteTime(f);
                            cache.Add(new ChatEntry { Id = Path.GetFileNameWithoutExtension(f), Title = S(d, "title") ?? "Чат", Updated = upd, Text = TextOf(d) });
                        }
                }
                catch { }
            }
            return cache.OrderByDescending(e => e.Updated).ToList();
        }

        static string TextOf(Dictionary<string, object> d)
        {
            var sb = new StringBuilder();
            object arr;
            if (d.TryGetValue("messages", out arr) && arr is object[])
                foreach (object o in (object[])arr) { var m = o as Dictionary<string, object>; if (m != null) sb.Append(S(m, "content")).Append('\n'); }
            return sb.ToString();
        }

        // Чати, у назві чи тексті яких є запит (без урахування регістру).
        public static List<ChatEntry> Search(string q)
        {
            q = (q ?? "").Trim();
            if (q.Length == 0) return All();
            return All().Where(e => (e.Title ?? "").IndexOf(q, StringComparison.CurrentCultureIgnoreCase) >= 0 ||
                                    (e.Text ?? "").IndexOf(q, StringComparison.CurrentCultureIgnoreCase) >= 0).ToList();
        }

        public static void Save(string id, string title, IEnumerable<ChatMessage> messages)
        {
            var list = new List<object>();
            foreach (var m in messages)
            {
                if (m.Streaming || (m.Role != "user" && m.Role != "assistant")) continue;
                var d = new Dictionary<string, object> { { "role", m.Role }, { "content", m.Raw } };
                if (m.Model != null) d["model"] = m.Model;
                if (m.Stats != null) d["stats"] = m.Stats;
                if (m.Folder != null) d["folder"] = m.Folder;
                if (m.Images != null && m.Images.Count > 0) d["images"] = m.Images.ToArray();
                if (m.Files != null && m.Files.Count > 0)
                    d["files"] = m.Files.Select(f => (object)new Dictionary<string, object> { { "name", f.Name }, { "text", f.Text }, { "chars", f.Chars } }).ToArray();
                if (m.ThinkSec > 0) d["think_sec"] = Math.Round(m.ThinkSec, 1);
                if (m.StepDetails != null && m.StepDetails.Count > 0)
                    d["steps"] = m.StepDetails.Select(x => (object)new Dictionary<string, object> { { "kind", x.Kind }, { "text", x.Text ?? "" }, { "path", x.Path ?? "" }, { "ok", x.Ok }, { "shot", x.Shot } }).ToArray();
                list.Add(d);
            }
            if (list.Count == 0) { Delete(id); return; }   // усе видалили — чату більше немає
            var now = DateTime.Now;
            var body = new Dictionary<string, object> { { "title", title }, { "updated", now.ToString("o", CultureInfo.InvariantCulture) }, { "messages", list } };
            try
            {
                Directory.CreateDirectory(Dir);
                string path = FileOf(id), tmp = path + ".tmp";
                File.WriteAllText(tmp, Json.Serialize(body), new UTF8Encoding(false));
                if (File.Exists(path)) File.Replace(tmp, path, null); else File.Move(tmp, path);
            }
            catch { return; }
            All();
            var e = cache.Find(x => x.Id == id);
            string text = TextOf(body);
            if (e == null) cache.Add(new ChatEntry { Id = id, Title = title, Updated = now, Text = text });
            else { e.Title = title; e.Updated = now; e.Text = text; }
        }

        public static List<ChatMessage> Load(string id, out string title)
        {
            title = "Чат";
            var res = new List<ChatMessage>();
            var d = ReadFile(FileOf(id));
            if (d == null) return res;
            title = S(d, "title") ?? title;
            object arr;
            if (d.TryGetValue("messages", out arr) && arr is object[])
                foreach (object o in (object[])arr)
                {
                    var m = o as Dictionary<string, object>;
                    if (m == null) continue;
                    var cm = new ChatMessage(S(m, "role") ?? "user", S(m, "content") ?? "") { Model = S(m, "model"), Stats = S(m, "stats"), Folder = S(m, "folder") };
                    object imgs, files, ts;
                    if (m.TryGetValue("images", out imgs) && imgs is object[])
                        cm.Images = ((object[])imgs).Select(x => Convert.ToString(x)).Where(x => !string.IsNullOrEmpty(x)).ToList();
                    if (m.TryGetValue("files", out files) && files is object[])
                        cm.Files = ((object[])files).OfType<Dictionary<string, object>>()
                            .Select(f => new FileAttachment { Name = S(f, "name") ?? "файл", Text = S(f, "text") ?? "", Chars = (S(f, "text") ?? "").Length })
                            .ToList();
                    if (cm.Files != null && cm.Files.Count == 0) cm.Files = null;
                    if (m.TryGetValue("think_sec", out ts) && ts != null) try { cm.ThinkSec = Convert.ToDouble(ts, CultureInfo.InvariantCulture); } catch { }
                    object steps;
                    if (m.TryGetValue("steps", out steps) && steps is object[])
                        cm.StepDetails = ((object[])steps).OfType<Dictionary<string, object>>().Select(x =>
                        {
                            object ok, shot;
                            var sd = new StepDetail { Kind = S(x, "kind") ?? "", Text = S(x, "text") ?? "", Path = S(x, "path") ?? "" };
                            sd.Ok = !x.TryGetValue("ok", out ok) || !(ok is bool) || (bool)ok;
                            if (x.TryGetValue("shot", out shot) && shot != null) try { sd.Shot = Convert.ToInt32(shot, CultureInfo.InvariantCulture); } catch { }
                            return sd;
                        }).ToList();
                    res.Add(cm);
                }
            return res;
        }

        public static void Rename(string id, string title)
        {
            var d = ReadFile(FileOf(id));
            if (d == null) return;
            d["title"] = title;
            try { File.WriteAllText(FileOf(id), Json.Serialize(d), new UTF8Encoding(false)); } catch { return; }
            All();
            var e = cache.Find(x => x.Id == id);
            if (e != null) e.Title = title;
        }

        public static void Delete(string id)
        {
            try { File.Delete(FileOf(id)); } catch { }
            All();
            cache.RemoveAll(x => x.Id == id);
        }
    }
}
