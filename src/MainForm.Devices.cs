// Open Forge — пристрої, підключені кабелем (розділ «Пристрої» в бічному меню): меню дій і передача опису пристрою моделі.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace OpenForge
{
    partial class MainForm
    {
        bool installingTools;

        void StartDevices()
        {
            sidebar.DeviceClicked += OnDeviceClicked;
            // Стежити лише після створення вікна: інакше перший список (те, що вже підключено при запуску) губився.
            Action go = () => Devices.Watch(list => { try { BeginInvoke((Action)(() => sidebar.Devices = list)); } catch { } });
            if (IsHandleCreated) go(); else HandleCreated += (s, e) => go();
        }

        void OnDeviceClicked(DeviceInfo d, Point at)
        {
            var items = new List<PopupItem>();
            if (d != null)
            {
                items.Add(new PopupItem("Показати моделі", false, () => ShowDeviceToModel(d)) { Sub = "додати опис пристрою в чат" });
                string adb = Devices.Adb, fb = Devices.Fastboot;
                if (d.Transport == "adb" && (d.AdbState == "device" || d.AdbState == "recovery") && adb != null)
                {
                    items.Add(new PopupItem("Перезавантажити", false, () => DeviceCommand(d, adb, "reboot", "звичайне перезавантаження")));
                    items.Add(new PopupItem("Перезавантажити в Recovery", false, () => DeviceCommand(d, adb, "reboot recovery", "режим відновлення (Recovery)")));
                    items.Add(new PopupItem("Перезавантажити в Fastboot", false, () => DeviceCommand(d, adb, "reboot bootloader", "Fastboot / Bootloader")));
                }
                if (d.Transport == "fastboot" && fb != null)
                {
                    items.Add(new PopupItem("Вийти з Fastboot (перезавантажити)", false, () => DeviceCommand(d, fb, "reboot", "звичайне перезавантаження")));
                    items.Add(new PopupItem("Перезавантажити в Recovery", false, () => DeviceCommand(d, fb, "reboot recovery", "режим відновлення (Recovery)")));
                }
                if (d.Kind == "board" && d.Com.Length > 0)
                    items.Add(new PopupItem("Скопіювати " + d.Com, false, () => { try { Clipboard.SetText(d.Com); } catch { } }) { Sub = "назва порту для Arduino IDE / esptool" });
                if (d.Kind == "android" && Devices.ToolsDir == null)
                    items.Add(new PopupItem("Встановити інструменти Android", false, InstallTools) { Sub = "adb і fastboot від Google" });
            }
            else
            {
                items.Add(new PopupItem("Як підключити телефон", false, ShowConnectHelp));
                if (Devices.ToolsDir == null)
                    items.Add(new PopupItem("Встановити інструменти Android", false, InstallTools) { Sub = "adb і fastboot від Google" });
            }
            PopupMenu.ShowAt(sidebar, at, items);
        }

        // Опис пристрою — як файл у поточному чаті: модель отримає його разом із наступним повідомленням.
        void ShowDeviceToModel(DeviceInfo d)
        {
            ShowPage(0);
            string text = d.Describe();
            attach.AddFile(new FileAttachment { Name = "Пристрій — " + d.Name + ".txt", Text = text, Chars = text.Length });
            if (chatInput.Box.Text.Trim().Length == 0)
                chatInput.Box.Text = d.Kind == "board" ? "Ось моя плата. Допоможи з прошивкою." :
                    d.Kind == "apple" ? "Ось мій пристрій. Що з ним можна зробити в цьому режимі?" : "Ось мій телефон. Що з ним можна зробити в цьому режимі?";
            chatInput.Box.SelectionStart = chatInput.Box.TextLength;
            chatInput.Box.Focus();
        }

        void DeviceCommand(DeviceInfo d, string exe, string args, string what)
        {
            if (MessageBox.Show(this, "Перезавантажити «" + d.Name + "»: " + what + "?\n\nНезбережені дані на телефоні можуть загубитися.",
                    "Open Forge", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            string serial = d.Serial;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                string o = Devices.Run(exe, "-s " + serial + " " + args, 15000).Trim();
                bool bad = o.IndexOf("error", StringComparison.OrdinalIgnoreCase) >= 0 || o.IndexOf("failed", StringComparison.OrdinalIgnoreCase) >= 0;
                if (bad) try { BeginInvoke((Action)(() => MessageBox.Show(this, "Не вдалося:\n" + o, "Open Forge"))); } catch { }
            });
        }

        void ShowConnectHelp()
        {
            MessageBox.Show(this,
                "Android:\n" +
                "1. Налаштування → Про телефон → 7 разів натисни «Номер збірки» — з'являться «Параметри розробника».\n" +
                "2. Параметри розробника → увімкни «Налагодження USB».\n" +
                "3. Підключи кабелем і на телефоні натисни «Дозволити».\n" +
                "Для подробиць (версія, завантажувач, режими) встанови інструменти Android — клік по «Підключи телефон кабелем».\n\n" +
                "iPhone / iPad:\n" +
                "Підключи кабелем і натисни «Довіряти цьому комп'ютеру». Для оновлення й відновлення потрібен iTunes або «Пристрої Apple» з Microsoft Store.\n\n" +
                "Плати (ESP32, Arduino, Raspberry Pi Pico, STM32…):\n" +
                "Підключи кабелем з передачею даних (не лише зарядним). Якщо немає COM-порту — встанови драйвер мікросхеми (CP210x, CH340 або FTDI).\n" +
                "Pico в режимі прошивки: тримай BOOTSEL і підключи кабель.\n\n" +
                "Режими (Recovery, Fastboot, Download, DFU, BOOTSEL) Open Forge показує сам, щойно пристрій у них підключено.",
                "Як підключити пристрій", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        void InstallTools()
        {
            if (installingTools) return;
            if (MessageBox.Show(this, "Завантажити Android Platform Tools (adb і fastboot) з офіційного сайту Google?\n\n" +
                    "Це близько 7 МБ. Вони потрібні, щоб бачити подробиці Android-телефона (версія, завантажувач, режими) і перезавантажувати його в Recovery чи Fastboot.\n" +
                    "Інструменти ставляться в папку Open Forge і видаляються разом з програмою.",
                    "Інструменти Android", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            installingTools = true;
            sidebar.DeviceStatus = "Завантажую інструменти… 0 %";
            Devices.InstallTools(
                pct => { try { BeginInvoke((Action)(() => sidebar.DeviceStatus = "Завантажую інструменти… " + pct + " %")); } catch { } },
                err =>
                {
                    try
                    {
                        BeginInvoke((Action)(() =>
                        {
                            installingTools = false;
                            sidebar.DeviceStatus = null;
                            if (err != null) MessageBox.Show(this, "Не вдалося завантажити інструменти Android:\n" + err, "Open Forge");
                        }));
                    }
                    catch { }
                });
        }
    }
}
