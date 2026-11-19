// Open Forge — запуск, зупинка й перезапуск Ollama з програми (кнопка біля «Ollama працює»).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace OpenForge
{
    static class OllamaControl
    {
        public const string DownloadUrl = "https://ollama.com/download/windows";

        // «ollama app.exe» (значок біля годинника, сам запускає сервер) або ollama.exe. null — Ollama не встановлена.
        public static string FindApp(out bool tray)
        {
            tray = false;
            var dirs = new List<string>
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Ollama"),
                Path.Combine(Environment.GetEnvironmentVariable("ProgramFiles") ?? "C:\\Program Files", "Ollama"),
            };
            dirs.AddRange((Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator));
            foreach (string d in dirs)
            {
                try
                {
                    if (d.Trim().Length == 0) continue;
                    string app = Path.Combine(d.Trim(), "ollama app.exe");
                    if (File.Exists(app)) { tray = true; return app; }
                }
                catch { }
            }
            foreach (string d in dirs)
            {
                try
                {
                    if (d.Trim().Length == 0) continue;
                    string exe = Path.Combine(d.Trim(), "ollama.exe");
                    if (File.Exists(exe)) return exe;
                }
                catch { }
            }
            return null;
        }

        public static bool Installed { get { bool t; return FindApp(out t) != null; } }

        // Запустити. Повертає текст помилки або null.
        public static string Start()
        {
            bool tray;
            string app = FindApp(out tray);
            if (app == null) return "Ollama не встановлена.";
            try
            {
                if (tray) Process.Start(new ProcessStartInfo(app) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(app) });
                else Process.Start(new ProcessStartInfo(app, "serve") { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Path.GetDirectoryName(app) });
                return null;
            }
            catch (Exception ex) { return ex.Message; }
        }

        // Зупинити: спершу значок у треї (інакше він одразу підніме сервер знову), потім сервер і моделі, що працюють.
        public static void Stop()
        {
            foreach (string name in new[] { "ollama app", "ollama", "ollama_llama_server" })
                foreach (var p in Process.GetProcessesByName(name))
                    try { p.Kill(); p.WaitForExit(3000); } catch { } finally { p.Dispose(); }
        }
    }
}
