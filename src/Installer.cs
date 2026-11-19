// Open Forge — встановлення й видалення. Інсталятор OpenForge-setup.exe — це сама програма (без обгорток):
// якщо файл називається «…setup…» або запущений з ключем /install, він ставить себе в папку користувача.
// Без прав адміністратора й без автозапуску. Моделі Ollama ніколи не чіпаємо.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace OpenForge
{
    static class Installer
    {
        const string Title = "Встановлення Open Forge";
        const string ExeName = "OpenForge.exe";
        const string UninstallKey = "Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\OpenForge";

        public static string Dir
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs\\OpenForge"); }
        }

        static string DesktopLink { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Open Forge.lnk"); } }
        static string StartLink { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Open Forge.lnk"); } }

        static void Fail(string text)
        {
            MessageBox.Show(text, Title, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        // Закрити інші копії програми, щоб можна було замінити файл.
        static void CloseOthers()
        {
            int self = Process.GetCurrentProcess().Id;
            foreach (var p in Process.GetProcessesByName("OpenForge"))
            {
                using (p)
                {
                    if (p.Id == self) continue;
                    try
                    {
                        if (!p.CloseMainWindow() || !p.WaitForExit(3000)) { p.Kill(); p.WaitForExit(3000); }
                    }
                    catch { }
                }
            }
        }

        static bool CopyWithRetry(string from, string to)
        {
            for (int i = 0; i < 10; i++)
            {
                try { File.Copy(from, to, true); return true; }
                catch { Thread.Sleep(500); }
            }
            return false;
        }

        static void Shortcut(string lnkPath, string target)
        {
            object sh = null, lnk = null;
            try
            {
                var t = Type.GetTypeFromProgID("WScript.Shell");
                sh = Activator.CreateInstance(t);
                lnk = t.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, sh, new object[] { lnkPath });
                var lt = lnk.GetType();
                lt.InvokeMember("TargetPath", BindingFlags.SetProperty, null, lnk, new object[] { target });
                lt.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, lnk, new object[] { Path.GetDirectoryName(target) });
                lt.InvokeMember("Description", BindingFlags.SetProperty, null, lnk, new object[] { "Open Forge" });
                lt.InvokeMember("Save", BindingFlags.InvokeMethod, null, lnk, null);
            }
            catch { }
            finally
            {
                if (lnk != null) Marshal.ReleaseComObject(lnk);
                if (sh != null) Marshal.ReleaseComObject(sh);
            }
        }

        // Версії 1.x (на Python) ставились інсталятором Inno Setup з цим AppId.
        const string PythonKey = "Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\{A1B2C3D4-E5F6-7890-ABCD-EF1234567890}_is1";

        // Команда видалення старої версії 1.x або null, якщо її немає.
        static string PythonUninstaller()
        {
            foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
                foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
                {
                    try
                    {
                        using (var root = RegistryKey.OpenBaseKey(hive, view))
                        using (var k = root.OpenSubKey(PythonKey))
                        {
                            string u = k != null ? k.GetValue("UninstallString") as string : null;
                            if (!string.IsNullOrEmpty(u) && File.Exists(u.Trim('"'))) return u.Trim('"');
                        }
                    }
                    catch { }
                }
            return null;
        }

        // Тихо прибрати версію 1.x: програму і її ярлики. Налаштування, Ollama й моделі не чіпаються.
        static void RemovePython(string uninstaller)
        {
            try
            {
                var psi = new ProcessStartInfo(uninstaller, "/SILENT /SUPPRESSMSGBOXES /NORESTART");
                psi.UseShellExecute = true;   // якщо стару ставили «для всіх», Windows попросить дозвіл
                using (var p = Process.Start(psi)) if (p != null) p.WaitForExit(120000);
                Thread.Sleep(1500);   // Inno Setup дочищає папку вже після виходу
            }
            catch { }
        }

        public static void Install()
        {
            string me = Application.ExecutablePath;
            string exe = Path.Combine(Dir, ExeName);
            bool update = File.Exists(exe);
            string old = PythonUninstaller();
            string oldNote = old != null ? "\n\nСтару версію Open Forge 1.x буде видалено — Ollama, моделі й налаштування залишаться." : "";
            if (MessageBox.Show(update
                    ? "Оновити Open Forge до версії " + AppVersion.Text + "?\n\nПрограма закриється й запуститься знову. Налаштування, чати й моделі збережуться." + oldNote
                    : "Встановити Open Forge " + AppVersion.Text + "?\n\nПрограма встановиться в " + Dir + ", з'являться ярлики на Робочому столі й у меню «Пуск»." + oldNote,
                    Title, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            CloseOthers();
            if (old != null) RemovePython(old);
            try { Directory.CreateDirectory(Dir); }
            catch (Exception ex) { Fail("Не вдалося створити папку " + Dir + ":\n" + ex.Message); return; }
            if (!string.Equals(Path.GetFullPath(me), Path.GetFullPath(exe), StringComparison.OrdinalIgnoreCase) && !CopyWithRetry(me, exe))
            {
                Fail("Не вдалося скопіювати програму в " + Dir + ". Закрий Open Forge і спробуй ще раз.");
                return;
            }

            Shortcut(DesktopLink, exe);
            Shortcut(StartLink, exe);

            // Пункт у «Параметри → Програми».
            try
            {
                using (var k = Registry.CurrentUser.CreateSubKey(UninstallKey))
                {
                    k.SetValue("DisplayName", "Open Forge");
                    k.SetValue("DisplayIcon", exe);
                    k.SetValue("DisplayVersion", AppVersion.Text);
                    k.SetValue("Publisher", "Дмитро Бричнік");
                    k.SetValue("InstallLocation", Dir);
                    k.SetValue("UninstallString", "\"" + exe + "\" /uninstall");
                    k.SetValue("NoModify", 1, RegistryValueKind.DWord);
                    k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                    k.SetValue("EstimatedSize", (int)(new FileInfo(exe).Length / 1024), RegistryValueKind.DWord);
                }
            }
            catch { }

            try { Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true, WorkingDirectory = Dir }); } catch { }
        }

        public static void Uninstall()
        {
            const string t = "Видалення Open Forge";
            if (MessageBox.Show("Видалити Open Forge з комп'ютера?\n\nПрограма, ярлики й налаштування будуть видалені.\n" +
                    "Ollama і завантажені моделі залишаться — їх можна прибрати окремо.",
                    t, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            CloseOthers();
            var problems = new List<string>();
            foreach (string lnk in new[] { DesktopLink, StartLink })
                try { if (File.Exists(lnk)) File.Delete(lnk); } catch { problems.Add("не вдалося видалити ярлик " + lnk); }
            try { Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, false); } catch { }
            Settings.Frozen = true;
            try { if (Directory.Exists(Settings.Dir)) Directory.Delete(Settings.Dir, true); }
            catch { problems.Add("не вдалося видалити налаштування в " + Settings.Dir); }
            // Голос для озвучки (Piper), якщо його завантажували.
            string local = Path.GetDirectoryName(Speaker.VoiceDir);
            try { Speaker.Stop(); if (Directory.Exists(local)) Directory.Delete(local, true); }
            catch { problems.Add("не вдалося видалити " + local); }

            // Сам файл програми видаляємо після виходу: cmd чекає дві секунди й прибирає папку.
            string dir = Path.GetDirectoryName(Application.ExecutablePath);
            if (string.Equals(Path.GetFullPath(dir).TrimEnd('\\'), Path.GetFullPath(Dir).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var psi = new ProcessStartInfo("cmd.exe", "/c ping 127.0.0.1 -n 3 > nul & rmdir /s /q \"" + Dir + "\"");
                    psi.WindowStyle = ProcessWindowStyle.Hidden;
                    psi.CreateNoWindow = true;
                    psi.UseShellExecute = false;
                    psi.WorkingDirectory = Path.GetTempPath();
                    Process.Start(psi);
                }
                catch { problems.Add("папку " + Dir + " треба видалити вручну"); }
            }
            MessageBox.Show(problems.Count == 0 ? "Open Forge видалено." : "Open Forge видалено, але є зауваження:\n• " + string.Join("\n• ", problems),
                t, MessageBoxButtons.OK, problems.Count == 0 ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        }
    }
}
