// Open Forge — перегляд сайту чи HTML-коду з чату: окреме вікно Edge (або Chrome) в режимі «застосунку»,
// без адресного рядка й вкладок, праворуч від Open Forge. Це справжній браузер, тож працюють CSS, JavaScript і анімації.
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace OpenForge
{
    static class Preview
    {
        static string Dir { get { return Path.Combine(Path.GetTempPath(), "OpenForge-preview"); } }

        // Код із чату: зберегти в тимчасовий файл і відкрити.
        public static void OpenHtml(Form owner, string code)
        {
            if (string.IsNullOrEmpty(code)) return;
            try
            {
                Directory.CreateDirectory(Dir);
                string path = Path.Combine(Dir, "preview-" + DateTime.Now.ToString("HHmmss-fff") + ".html");
                string html = code;
                if (html.IndexOf("<html", StringComparison.OrdinalIgnoreCase) < 0 && html.IndexOf("<svg", StringComparison.OrdinalIgnoreCase) < 0)
                    html = "<!DOCTYPE html><html><head><meta charset=\"utf-8\"></head><body>" + html + "</body></html>";
                else if (html.IndexOf("charset", StringComparison.OrdinalIgnoreCase) < 0 && html.IndexOf("<head>", StringComparison.OrdinalIgnoreCase) >= 0)
                    html = html.Replace("<head>", "<head><meta charset=\"utf-8\">");
                File.WriteAllText(path, html, new UTF8Encoding(true));
                OpenFile(owner, path);
            }
            catch (Exception ex) { MessageBox.Show(owner, "Не вдалося відкрити перегляд:\n" + ex.Message, "Open Forge"); }
        }

        // Файл на диску (наприклад index.html, який зробив агент).
        public static void OpenFile(Form owner, string path)
        {
            if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path)) { MessageBox.Show(owner, "Файл не знайдено:\n" + path, "Open Forge"); return; }
            string url = new Uri(path).AbsoluteUri;
            string browser = AgentRun.FindBrowser();
            if (browser == null)
            {
                try { Process.Start(path); } catch (Exception ex) { MessageBox.Show(owner, "Не вдалося відкрити:\n" + ex.Message, "Open Forge"); }
                return;
            }
            // Праворуч від вікна Open Forge (або на правій половині екрана, якщо вікно на весь екран).
            var area = Screen.FromControl(owner).WorkingArea;
            var b = owner.WindowState == FormWindowState.Normal ? owner.Bounds : area;
            int w = Math.Max(480, Math.Min(1100, area.Width * 9 / 20)), h = area.Height, x, y = area.Top;
            if (b.Right + w <= area.Right) x = b.Right;
            else x = area.Right - w;
            string profile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenForge", "preview-browser");
            string args = "--app=\"" + url + "\" --user-data-dir=\"" + profile + "\" --no-first-run --no-default-browser-check" +
                          " --window-size=" + w + "," + h + " --window-position=" + x + "," + y;
            try { Process.Start(new ProcessStartInfo(browser, args) { UseShellExecute = false }); }
            catch (Exception ex) { MessageBox.Show(owner, "Не вдалося відкрити перегляд:\n" + ex.Message, "Open Forge"); }
        }
    }
}
