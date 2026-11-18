// Open Forge — налаштування: простий текстовий файл «ключ=значення» у %APPDATA%\OpenForge.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace OpenForge
{
    static class Settings
    {
        public static readonly string Dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OpenForge");
        static readonly string FilePath = Path.Combine(Dir, "settings.txt");
        static Dictionary<string, string> data;
        static readonly object sync = new object();
        public static bool Frozen;   // після видалення програми нічого не записуємо

        static void Load()
        {
            if (data != null) return;
            data = new Dictionary<string, string>();
            try
            {
                if (!File.Exists(FilePath)) return;
                foreach (string line in File.ReadAllLines(FilePath))
                {
                    int i = line.IndexOf('=');
                    if (i > 0) data[line.Substring(0, i).Trim()] = Unescape(line.Substring(i + 1).Trim());
                }
            }
            catch { }
        }

        // Значення може бути багаторядковим (інструкція для моделі), тож переноси рядків екрануємо.
        static string Escape(string v) { return (v ?? "").Replace("\\", "\\\\").Replace("\r", "").Replace("\n", "\\n"); }

        static string Unescape(string v)
        {
            var sb = new System.Text.StringBuilder(v.Length);
            for (int i = 0; i < v.Length; i++)
            {
                if (v[i] == '\\' && i + 1 < v.Length)
                {
                    char n = v[++i];
                    sb.Append(n == 'n' ? '\n' : n);
                }
                else sb.Append(v[i]);
            }
            return sb.ToString();
        }

        public static string Get(string key, string def)
        {
            lock (sync)
            {
                Load();
                string v;
                return data.TryGetValue(key, out v) ? v : def;
            }
        }

        public static bool GetBool(string key, bool def) { return Get(key, def ? "1" : "0") == "1"; }

        public static int GetInt(string key, int def)
        {
            int v;
            return int.TryParse(Get(key, ""), out v) ? v : def;
        }

        public static void Set(string key, string value)
        {
            if (Frozen) return;
            lock (sync)
            {
                Load();
                data[key] = value ?? "";
                try
                {
                    Directory.CreateDirectory(Dir);
                    File.WriteAllLines(FilePath, data.Select(kv => kv.Key + "=" + Escape(kv.Value)).ToArray());
                }
                catch { }
            }
        }

        public static void SetBool(string key, bool value) { Set(key, value ? "1" : "0"); }
        public static void SetInt(string key, int value) { Set(key, value.ToString()); }
    }
}
