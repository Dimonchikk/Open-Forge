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
                            cache.Add(new ChatEntry { Id = Path.GetFileNameWithoutExtension(f), Title = S(d, "title") ?? "Чат", Updated = upd });
                        }
                }
                catch { }
            }
            return cache.OrderByDescending(e => e.Updated).ToList();
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
                list.Add(d);
            }
            if (list.Count == 0) return;
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
            if (e == null) cache.Add(new ChatEntry { Id = id, Title = title, Updated = now });
            else { e.Title = title; e.Updated = now; }
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
                    object imgs;
                    if (m.TryGetValue("images", out imgs) && imgs is object[])
                        cm.Images = ((object[])imgs).Select(x => Convert.ToString(x)).Where(x => !string.IsNullOrEmpty(x)).ToList();
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
