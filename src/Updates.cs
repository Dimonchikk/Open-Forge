// Open Forge — перевірка оновлень: питає GitHub (одразу після запуску й щогодини), який номер останнього релізу.
// Програма нічого сама не запускає: кнопка «Оновити» відкриває завантаження інсталятора в браузері,
// а інсталятор сам закриває стару версію й ставить нову поверх (налаштування зберігаються).
using System;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text.RegularExpressions;

namespace OpenForge
{
    static class Updates
    {
        public const string Repo = "Dimonchikk/Open-Forge";
        public const string Download = "https://github.com/" + Repo + "/releases/latest/download/OpenForge-setup.exe";
        public const string ReleasesPage = "https://github.com/" + Repo + "/releases/latest";
        public static readonly TimeSpan Every = TimeSpan.FromMinutes(55);

        // Новіша версія, якщо вона є (запам'ятовується між запусками), інакше null.
        public static string Latest
        {
            get
            {
                string v = Settings.Get("UpdateLatest", "");
                return v != "" && Newer(v, AppVersion.Text) ? v : null;
            }
        }

        // a новіша за b? Порівнюються числа: 2.10 новіша за 2.9.
        public static bool Newer(string a, string b)
        {
            int[] x = Parts(a), y = Parts(b);
            for (int i = 0; i < Math.Max(x.Length, y.Length); i++)
            {
                int p = i < x.Length ? x[i] : 0, q = i < y.Length ? y[i] : 0;
                if (p != q) return p > q;
            }
            return false;
        }

        static int[] Parts(string v)
        {
            var list = new System.Collections.Generic.List<int>();
            foreach (string s in (v ?? "").TrimStart('v', 'V').Split('.'))
            {
                int n;
                list.Add(int.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out n) ? n : 0);
            }
            return list.ToArray();
        }

        // Номер останнього опублікованого релізу або null (немає інтернету тощо). Викликати з фонового потоку.
        public static string CheckNow()
        {
            try
            {
                try { ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072; } catch { }   // TLS 1.2
                var req = (HttpWebRequest)WebRequest.Create("https://api.github.com/repos/" + Repo + "/releases/latest");
                req.UserAgent = "OpenForge/" + AppVersion.Text;
                req.Accept = "application/vnd.github+json";
                req.Timeout = 15000;
                req.ReadWriteTimeout = 15000;
                using (var resp = req.GetResponse())
                using (var sr = new StreamReader(resp.GetResponseStream()))
                {
                    var m = Regex.Match(sr.ReadToEnd(), "\"tag_name\"\\s*:\\s*\"[vV]?([0-9]+(?:\\.[0-9]+)*)\"");
                    if (!m.Success) return null;
                    Settings.Set("UpdateLatest", m.Groups[1].Value);
                    return m.Groups[1].Value;
                }
            }
            catch { return null; }
        }
    }
}
