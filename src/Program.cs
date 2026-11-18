// Open Forge — точка входу.
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace OpenForge
{
    static class Program
    {
        public const string Name = "Open Forge";

        [DllImport("user32.dll")]
        static extern bool SetProcessDPIAware();

        static bool Has(string[] args, string key)
        {
            return args.Any(a => a.Equals(key, StringComparison.OrdinalIgnoreCase));
        }

        // Ключі запуску: /dark — темна тема, /install — встановити, /uninstall — видалити програму.
        // Права адміністратора не потрібні: Open Forge працює лише з Ollama і папкою користувача.
        [STAThread]
        static void Main(string[] args)
        {
            try { SetProcessDPIAware(); } catch { }   // на Linux (перевірка під Mono) цієї функції немає
            try
            {
                using (var g = Graphics.FromHwnd(IntPtr.Zero))
                    Ui.Scale = Math.Max(1f, g.DpiX / 96f);
            }
            catch { }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // Інсталятор — це сама програма під назвою «OpenForge-setup.exe» (або ключ /install).
            bool asSetup = Path.GetFileNameWithoutExtension(Application.ExecutablePath).IndexOf("setup", StringComparison.OrdinalIgnoreCase) >= 0;
            if (asSetup || Has(args, "/install"))
            {
                Installer.Install();
                return;
            }

            Ui.ApplyTheme(Has(args, "/dark") || MainForm.WantDark());

            // /uninstall — видалення з «Параметри → Програми».
            if (Has(args, "/uninstall"))
            {
                Installer.Uninstall();
                return;
            }

            Mutex mutex;
            try { mutex = new Mutex(false, "OpenForge_SingleInstance"); }
            catch (UnauthorizedAccessException)
            {
                MessageBox.Show("Open Forge уже працює.", Name);
                return;
            }
            bool owned;
            try { owned = mutex.WaitOne(0); }
            catch (AbandonedMutexException) { owned = true; }
            EventWaitHandle show = null;
            try { show = new EventWaitHandle(false, EventResetMode.AutoReset, "OpenForge_Show"); } catch { }
            if (!owned)
            {
                // Друга копія лише показує першу.
                if (show != null) show.Set();
                return;
            }
            Application.ThreadException += (s, e) =>
            {
                Console.Error.WriteLine(e.Exception);
                MessageBox.Show("Щось пішло не так:\n" + e.Exception.Message, Name, MessageBoxButtons.OK, MessageBoxIcon.Error);
            };
            try { Application.Run(new MainForm(show)); }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
                MessageBox.Show("Щось пішло не так:\n" + ex.Message, Name, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            GC.KeepAlive(mutex);
        }

        public static void Open(string url)
        {
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
        }
    }
}
