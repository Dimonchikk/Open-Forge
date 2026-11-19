// Open Forge — пристрої, підключені кабелем: Android (звичайний режим, Recovery, Sideload, Fastboot, Download Mode Samsung,
// EDL Qualcomm, MediaTek BROM) і Apple (iPhone/iPad, режим відновлення, DFU — і Mac з Apple Silicon у DFU).
// Розпізнаємо за стандартними USB-ідентифікаторами виробників (WMI), а подробиці Android — через adb/fastboot
// (Android Platform Tools від Google; програма пропонує їх завантажити). Модель отримує опис пристрою як файл у чаті.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Management;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace OpenForge
{
    enum DeviceState { Ok, Attention, Service, Danger }   // зелений / помаранчевий / фіолетовий / червоний

    class DeviceInfo
    {
        public string Kind = "android";   // android | apple | board (ESP32, Arduino, Pico, STM32…)
        public string Com = "";           // COM-порт плати
        public string Name = "", Mode = "", Serial = "", Vid = "", Pid = "";
        public DeviceState State;
        public string Transport = "";      // adb | fastboot | usb
        public string AdbState = "";       // device | recovery | sideload | unauthorized | …
        public readonly List<KeyValuePair<string, string>> Props = new List<KeyValuePair<string, string>>();

        public string Key { get { return Kind + "|" + (Serial.Length > 0 ? Serial : Vid + Pid) + "|" + Mode; } }

        // Опис для моделі: що за пристрій, у якому режимі і що з ним можна робити.
        public string Describe()
        {
            var sb = new StringBuilder();
            sb.AppendLine("Підключений кабелем пристрій (дані з Open Forge):");
            sb.AppendLine("Тип: " + (Kind == "apple" ? "Apple (iPhone / iPad / Mac)" : Kind == "board" ? "Плата / мікроконтролер" : "Android"));
            if (Com.Length > 0) sb.AppendLine("COM-порт: " + Com);
            sb.AppendLine("Назва: " + Name);
            sb.AppendLine("Режим: " + Mode);
            if (Serial.Length > 0) sb.AppendLine("Серійний номер / ID: " + Serial);
            if (Vid.Length > 0) sb.AppendLine("USB: VID " + Vid + ", PID " + Pid);
            foreach (var p in Props) sb.AppendLine(p.Key + ": " + p.Value);
            sb.AppendLine();
            if (Kind == "board")
            {
                sb.AppendLine("Прошивають зазвичай через Arduino IDE, PlatformIO або esptool (ESP32/ESP8266: esptool.py --port " + (Com.Length > 0 ? Com : "COMx") +
                              " ...). Raspberry Pi Pico у режимі BOOTSEL — скопіювати файл .uf2 на диск RPI-RP2 (або RP2350); STM32 у DFU — STM32CubeProgrammer або dfu-util.");
                sb.AppendLine("Агент Open Forge може запускати ці інструменти командами, якщо вони встановлені.");
            }
            else if (Kind == "android")
            {
                sb.AppendLine(Devices.ToolsDir != null
                    ? "На ПК встановлено Android Platform Tools (adb і fastboot); агент Open Forge може запускати їх командами adb / fastboot."
                    : "Android Platform Tools (adb, fastboot) на ПК не встановлені — їх можна поставити в Open Forge (клік по пристрою → «Встановити інструменти Android»).");
            }
            else sb.AppendLine("Для відновлення чи оновлення iPhone/iPad на Windows використовують iTunes або застосунок «Пристрої Apple»; Mac у DFU відновлюють через Apple Configurator на іншому Mac.");
            return sb.ToString();
        }
    }

    static class Devices
    {
        // ── Android Platform Tools ───────────────────────────────────────────────

        public static string OwnToolsDir { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenForge", "platform-tools"); } }
        public const string ToolsUrl = "https://dl.google.com/android/repository/platform-tools-latest-windows.zip";

        // Папка з adb.exe: наша, з Android SDK або з PATH. null — не знайдено.
        public static string ToolsDir
        {
            get
            {
                var dirs = new List<string> { OwnToolsDir, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Android", "Sdk", "platform-tools") };
                dirs.AddRange((Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator));
                foreach (string d in dirs)
                    try { if (d.Trim().Length > 0 && File.Exists(Path.Combine(d.Trim(), "adb.exe"))) return d.Trim(); } catch { }
                return null;
            }
        }

        public static string Adb { get { var d = ToolsDir; return d == null ? null : Path.Combine(d, "adb.exe"); } }
        public static string Fastboot { get { var d = ToolsDir; return d == null || !File.Exists(Path.Combine(d, "fastboot.exe")) ? null : Path.Combine(d, "fastboot.exe"); } }

        // Завантажити й розпакувати Platform Tools у %LOCALAPPDATA%\OpenForge\platform-tools. progress — 0..100.
        public static void InstallTools(Action<int> progress, Action<string> done)
        {
            ThreadPool.QueueUserWorkItem(_ =>
            {
                string zip = Path.Combine(Path.GetTempPath(), "openforge-platform-tools.zip");
                try
                {
                    ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;   // TLS 1.2
                    var req = (HttpWebRequest)WebRequest.Create(ToolsUrl);
                    req.UserAgent = "OpenForge/" + AppVersion.Text;
                    using (var resp = req.GetResponse())
                    using (var s = resp.GetResponseStream())
                    using (var f = File.Create(zip))
                    {
                        long total = resp.ContentLength, got = 0;
                        var buf = new byte[65536];
                        int n, last = -1;
                        while ((n = s.Read(buf, 0, buf.Length)) > 0)
                        {
                            f.Write(buf, 0, n);
                            got += n;
                            int pct = total > 0 ? (int)(got * 100 / total) : 0;
                            if (pct != last) { last = pct; progress(pct); }
                        }
                    }
                    string parent = Path.GetDirectoryName(OwnToolsDir);
                    Directory.CreateDirectory(parent);
                    if (Directory.Exists(OwnToolsDir)) Directory.Delete(OwnToolsDir, true);
                    ZipFile.ExtractToDirectory(zip, parent);   // в архіві — папка platform-tools
                    done(null);
                }
                catch (Exception ex) { done(ex.Message); }
                finally { try { File.Delete(zip); } catch { } }
            });
        }

        // Запустити adb/fastboot і повернути весь вивід (stdout + stderr). Не довше timeoutMs.
        public static string Run(string exe, string args, int timeoutMs = 5000)
        {
            if (exe == null) return "";
            try
            {
                var psi = new ProcessStartInfo(exe, args)
                {
                    UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
                };
                var sb = new StringBuilder();
                using (var p = new Process { StartInfo = psi })
                {
                    p.OutputDataReceived += (s, e) => { if (e.Data != null) lock (sb) sb.AppendLine(e.Data); };
                    p.ErrorDataReceived += (s, e) => { if (e.Data != null) lock (sb) sb.AppendLine(e.Data); };
                    p.Start();
                    p.BeginOutputReadLine();
                    p.BeginErrorReadLine();
                    if (p.WaitForExit(timeoutMs)) p.WaitForExit();   // без аргументу — ще й дочитати весь вивід
                    else { try { p.Kill(); } catch { } p.WaitForExit(1000); }
                }
                lock (sb) return sb.ToString();
            }
            catch { return ""; }
        }

        // ── розпізнавання ─────────────────────────────────────────────────────

        static readonly Dictionary<string, string> AndroidVendors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "18D1", "Google" }, { "04E8", "Samsung" }, { "2717", "Xiaomi" }, { "22B8", "Motorola" }, { "12D1", "Huawei" },
            { "2A70", "OnePlus" }, { "0BB4", "HTC" }, { "1004", "LG" }, { "0FCE", "Sony" }, { "22D9", "OPPO / realme" },
            { "2D95", "vivo" }, { "05C6", "Qualcomm" }, { "0E8D", "MediaTek" }, { "1949", "Amazon" }, { "19D2", "ZTE" },
            { "17EF", "Lenovo" }, { "0B05", "ASUS" }, { "2A45", "Meizu" }, { "1EBF", "Nothing" }, { "29A9", "Tecno / Infinix" },
        };

        internal class PnpGroup
        {
            public string Vid, Pid, Serial = "", Name = "", Names = "";
            public bool Adb, Boot, Mtp;
            public string Com { get { var m = Regex.Match(Names, @"\((COM\d+)\)"); return m.Success ? m.Groups[1].Value : ""; } }
        }

        // Плати й USB-UART мікросхеми: VID:PID (або лише VID) → назва, режим, стан.
        static readonly Dictionary<string, string[]> Boards = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            { "303A:1001", new[] { "ESP32-S3 / C3 / C6", "вбудований USB (USB-JTAG/Serial)", "ok" } },
            { "303A:0002", new[] { "ESP32-S2", "вбудований USB", "ok" } },
            { "303A", new[] { "Плата Espressif (ESP32)", "", "ok" } },
            { "10C4:EA60", new[] { "ESP32 / ESP8266 (CP210x)", "USB-UART CP210x", "ok" } },
            { "1A86:7523", new[] { "Плата з CH340", "USB-UART CH340", "ok" } },
            { "1A86:7522", new[] { "Плата з CH340K", "USB-UART CH340K", "ok" } },
            { "1A86:55D4", new[] { "ESP32 (CH9102)", "USB-UART CH9102", "ok" } },
            { "1A86:55D3", new[] { "ESP32 (CH343)", "USB-UART CH343", "ok" } },
            { "0403:6001", new[] { "Плата / адаптер FTDI", "USB-UART FT232", "ok" } },
            { "0403:6010", new[] { "Плата / адаптер FTDI", "USB-UART FT2232", "ok" } },
            { "0403:6014", new[] { "Плата / адаптер FTDI", "USB-UART FT232H", "ok" } },
            { "0403:6015", new[] { "Плата / адаптер FTDI", "USB-UART FT-X", "ok" } },
            { "2341:0043", new[] { "Arduino Uno", "", "ok" } }, { "2341:0001", new[] { "Arduino Uno", "", "ok" } },
            { "2341:0042", new[] { "Arduino Mega 2560", "", "ok" } }, { "2341:0010", new[] { "Arduino Mega 2560", "", "ok" } },
            { "2341:8036", new[] { "Arduino Leonardo", "", "ok" } }, { "2341:0036", new[] { "Arduino Leonardo", "завантажувач", "boot" } },
            { "2341:8037", new[] { "Arduino Micro", "", "ok" } }, { "2341:0058", new[] { "Arduino Nano Every", "", "ok" } },
            { "2341:805A", new[] { "Arduino Nano 33 BLE", "", "ok" } }, { "2341:1002", new[] { "Arduino Uno R4 WiFi", "", "ok" } },
            { "2341:0069", new[] { "Arduino Uno R4 Minima", "", "ok" } },
            { "2341", new[] { "Плата Arduino", "", "ok" } }, { "2A03", new[] { "Плата Arduino", "", "ok" } },
            { "2E8A:0003", new[] { "Raspberry Pi Pico (RP2040)", "BOOTSEL · прошивка .uf2", "boot" } },
            { "2E8A:000F", new[] { "Raspberry Pi Pico 2 (RP2350)", "BOOTSEL · прошивка .uf2", "boot" } },
            { "2E8A:0005", new[] { "Raspberry Pi Pico", "MicroPython", "ok" } },
            { "2E8A:000A", new[] { "Raspberry Pi Pico", "Pico SDK", "ok" } },
            { "2E8A", new[] { "Плата Raspberry Pi", "", "ok" } },
            { "0483:DF11", new[] { "STM32", "DFU · режим прошивки", "boot" } },
            { "0483:5740", new[] { "STM32", "віртуальний COM-порт", "ok" } },
            { "0483:374B", new[] { "ST-Link (програматор STM32)", "", "ok" } }, { "0483:3748", new[] { "ST-Link (програматор STM32)", "", "ok" } },
            { "0483:374E", new[] { "ST-Link (програматор STM32)", "", "ok" } }, { "0483:3752", new[] { "ST-Link (програматор STM32)", "", "ok" } },
            { "16C0:0483", new[] { "Teensy", "", "ok" } }, { "16C0:0478", new[] { "Teensy", "завантажувач", "boot" } },
            { "0D28:0204", new[] { "BBC micro:bit", "", "ok" } },
            { "239A", new[] { "Плата Adafruit", "", "ok" } }, { "2886", new[] { "Плата Seeed (XIAO тощо)", "", "ok" } },
            { "1B4F", new[] { "Плата SparkFun", "", "ok" } }, { "1366", new[] { "SEGGER J-Link (програматор)", "", "ok" } },
        };

        internal static DeviceInfo BoardFrom(PnpGroup g)
        {
            string[] b;
            if (!Boards.TryGetValue(g.Vid + ":" + g.Pid, out b) && !Boards.TryGetValue(g.Vid, out b)) return null;
            var d = new DeviceInfo { Kind = "board", Vid = g.Vid, Pid = g.Pid, Serial = g.Serial, Transport = "usb", Com = g.Com, Name = b[0] };
            bool boot = b[2] == "boot";
            d.State = boot ? DeviceState.Service : d.Com.Length > 0 ? DeviceState.Ok : DeviceState.Attention;
            string mode = b[1].Length > 0 && !b[1].StartsWith("USB-UART") ? b[1] : "";
            d.Mode = boot ? b[1] : (d.Com.Length > 0 ? d.Com : "немає COM — потрібен драйвер") + (mode.Length > 0 ? " · " + mode : "");
            if (b[1].StartsWith("USB-UART")) Add(d, "Мікросхема зв'язку", b[1].Substring(9));
            return d;
        }

        static List<PnpGroup> UsbDevices()
        {
            var groups = new Dictionary<string, PnpGroup>();
            // Для перевірки без заліза: OPENFORGE_FAKE_USB="10C4:EA60:CP210x (COM5);2E8A:0003:RP2 Boot"
            string fake = Environment.GetEnvironmentVariable("OPENFORGE_FAKE_USB");
            if (!string.IsNullOrEmpty(fake))
            {
                foreach (var e in fake.Split(';'))
                {
                    var f = e.Split(new[] { ':' }, 3);
                    if (f.Length == 3) groups[f[0] + f[1]] = new PnpGroup { Vid = f[0], Pid = f[1], Name = f[2], Names = f[2] };
                }
                return new List<PnpGroup>(groups.Values);
            }
            try
            {
                using (var s = new ManagementObjectSearcher("SELECT Name, PNPDeviceID FROM Win32_PnPEntity WHERE PNPDeviceID LIKE 'USB%'"))
                    foreach (ManagementObject o in s.Get())
                    {
                        string id = Convert.ToString(o["PNPDeviceID"]) ?? "", name = Convert.ToString(o["Name"]) ?? "";
                        var m = Regex.Match(id, @"VID_([0-9A-F]{4})&PID_([0-9A-F]{4})(&MI_\d+)?\\(.*)$", RegexOptions.IgnoreCase);
                        if (!m.Success) continue;
                        string vid = m.Groups[1].Value.ToUpperInvariant(), pid = m.Groups[2].Value.ToUpperInvariant();
                        PnpGroup g;
                        if (!groups.TryGetValue(vid + pid, out g)) groups[vid + pid] = g = new PnpGroup { Vid = vid, Pid = pid };
                        if (!m.Groups[3].Success) { g.Serial = m.Groups[4].Value; if (g.Name.Length == 0) g.Name = name; }
                        g.Names += name + "\n";
                        string low = name.ToLowerInvariant();
                        if (low.Contains("adb")) g.Adb = true;
                        if (low.Contains("bootloader") || low.Contains("fastboot")) g.Boot = true;
                        if (!m.Groups[3].Success || low.Contains("mtp")) { if (!low.Contains("composite") && !low.Contains("usb") && !low.Contains("adb")) { g.Mtp = true; g.Name = name; } }
                    }
            }
            catch { }
            return groups.Values.ToList();
        }

        internal static DeviceInfo FromPnp(PnpGroup g)
        {
            var board = BoardFrom(g);
            if (board != null) return board;
            int pid = int.Parse(g.Pid, NumberStyles.HexNumber);
            if (g.Vid == "05AC")
            {
                var d = new DeviceInfo { Kind = "apple", Vid = g.Vid, Pid = g.Pid, Serial = g.Serial, Transport = "usb" };
                if (pid == 0x1227 || pid == 0x1222) { d.Name = "Apple-пристрій"; d.Mode = "DFU (iPhone, iPad або Mac)"; d.State = DeviceState.Service; }
                else if (pid >= 0x1280 && pid <= 0x1283) { d.Name = "iPhone / iPad"; d.Mode = "Режим відновлення (Recovery)"; d.State = DeviceState.Attention; }
                else if (pid >= 0x1290 && pid <= 0x12AF)
                {
                    d.Name = g.Names.Contains("iPad") ? "iPad" : g.Names.Contains("iPhone") ? "iPhone" : "iPhone / iPad";
                    d.Mode = "Увімкнено"; d.State = DeviceState.Ok;
                }
                else return null;   // клавіатури, адаптери тощо
                return d;
            }
            string vendor;
            bool known = AndroidVendors.TryGetValue(g.Vid, out vendor);
            if (!known && !g.Adb && !g.Boot) return null;
            var a = new DeviceInfo { Vid = g.Vid, Pid = g.Pid, Serial = g.Serial, Transport = "usb" };
            a.Name = g.Mtp && g.Name.Length > 0 ? g.Name : (vendor ?? "Android-пристрій");
            if (g.Vid == "04E8" && pid == 0x685D) { a.Name = "Samsung"; a.Mode = "Download Mode (Odin)"; a.State = DeviceState.Service; }
            else if (g.Vid == "05C6" && pid == 0x9008) { a.Name = "Qualcomm-пристрій"; a.Mode = "EDL (Qualcomm 9008)"; a.State = DeviceState.Danger; }
            else if (g.Vid == "0E8D" && (pid == 0x0003 || pid == 0x2000)) { a.Name = "MediaTek-пристрій"; a.Mode = pid == 0x0003 ? "MediaTek BROM" : "MediaTek Preloader"; a.State = DeviceState.Danger; }
            else if (g.Boot) { a.Mode = "Fastboot / Bootloader"; a.State = DeviceState.Service; }
            else if (g.Adb) { a.Mode = "Увімкнено · налагодження USB"; a.State = DeviceState.Ok; }
            else if (known) { a.Mode = "Увімкнено · лише файли (увімкни налагодження USB)"; a.State = DeviceState.Attention; }
            else return null;
            return a;
        }

        static string Prop(string text, string key)
        {
            var m = Regex.Match(text, Regex.Escape(key) + @":(\S+)");
            return m.Success ? m.Groups[1].Value.Replace('_', ' ') : "";
        }

        // Пристрої, видимі через adb і fastboot (з подробицями).
        static List<DeviceInfo> AdbDevices()
        {
            var list = new List<DeviceInfo>();
            string adb = Adb, fb = Fastboot;
            if (adb != null)
            {
                foreach (string line in Run(adb, "devices -l", 6000).Split('\n'))
                {
                    var parts = line.Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length < 2 || line.StartsWith("List of") || line.StartsWith("*")) continue;
                    var d = new DeviceInfo { Serial = parts[0], Transport = "adb", AdbState = parts[1] };
                    string model = Prop(line, "model");
                    d.Name = model.Length > 0 ? model : "Android-пристрій";
                    switch (d.AdbState)
                    {
                        case "device": d.Mode = "Увімкнено · налагодження USB"; d.State = DeviceState.Ok; break;
                        case "recovery": d.Mode = "Recovery"; d.State = DeviceState.Attention; break;
                        case "sideload": d.Mode = "Sideload (встановлення оновлення)"; d.State = DeviceState.Attention; break;
                        case "rescue": d.Mode = "Rescue"; d.State = DeviceState.Attention; break;
                        case "unauthorized": d.Mode = "Підтверди налагодження на телефоні"; d.State = DeviceState.Attention; break;
                        case "offline": d.Mode = "Не відповідає (offline)"; d.State = DeviceState.Danger; break;
                        default: d.Mode = d.AdbState; d.State = DeviceState.Attention; break;
                    }
                    if (d.AdbState == "device" || d.AdbState == "recovery")
                    {
                        string o = Run(adb, "-s " + d.Serial + " shell \"getprop ro.product.manufacturer; getprop ro.product.model; getprop ro.build.version.release; " +
                                             "getprop ro.build.version.security_patch; getprop ro.build.display.id; getprop ro.boot.flash.locked; getprop ro.boot.verifiedbootstate; " +
                                             "getprop ro.product.cpu.abi; dumpsys battery | grep level\"", 6000);
                        var v = o.Replace("\r", "").Split('\n');
                        Func<int, string> at = i => i < v.Length ? v[i].Trim() : "";
                        if (at(1).Length > 0) d.Name = (at(0).Length > 0 ? Cap(at(0)) + " " : "") + at(1);
                        Add(d, "Android", at(2));
                        Add(d, "Безпековий патч", at(3));
                        Add(d, "Збірка", at(4));
                        Add(d, "Завантажувач", at(5) == "0" ? "розблоковано" : at(5) == "1" ? "заблоковано" : "");
                        Add(d, "Verified Boot", at(6));
                        Add(d, "Процесор (ABI)", at(7));
                        var lv = Regex.Match(o, @"level:\s*(\d+)");
                        if (lv.Success) Add(d, "Заряд", lv.Groups[1].Value + " %");
                    }
                    list.Add(d);
                }
            }
            if (fb != null)
            {
                foreach (string line in Run(fb, "devices", 5000).Split('\n'))
                {
                    var parts = line.Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length < 2) continue;
                    var d = new DeviceInfo { Serial = parts[0], Transport = "fastboot", AdbState = parts[1], Mode = parts[1] == "fastbootd" ? "Fastbootd (userspace)" : "Fastboot / Bootloader", State = DeviceState.Service };
                    string o = Run(fb, "-s " + d.Serial + " getvar all", 6000);
                    string product = Var(o, "product");
                    d.Name = product.Length > 0 ? product : "Android-пристрій";
                    Add(d, "Завантажувач", Var(o, "unlocked") == "yes" ? "розблоковано" : Var(o, "unlocked") == "no" ? "заблоковано" : "");
                    Add(d, "Активний слот", Var(o, "current-slot"));
                    Add(d, "Версія завантажувача", Var(o, "version-bootloader"));
                    Add(d, "Версія радіо", Var(o, "version-baseband"));
                    list.Add(d);
                }
            }
            return list;
        }

        static string Var(string o, string key)
        {
            var m = Regex.Match(o, @"\(bootloader\)\s*" + Regex.Escape(key) + @":\s*(\S+)");
            if (!m.Success) m = Regex.Match(o, "^" + Regex.Escape(key) + @":\s*(\S+)", RegexOptions.Multiline);
            return m.Success ? m.Groups[1].Value : "";
        }

        static string Cap(string s) { return s.Length == 0 ? s : char.ToUpper(s[0]) + s.Substring(1); }
        static void Add(DeviceInfo d, string k, string v) { if (!string.IsNullOrEmpty(v)) d.Props.Add(new KeyValuePair<string, string>(k, v)); }

        // Усі підключені пристрої. Android з adb/fastboot — з подробицями; решта — за USB-ідентифікаторами.
        public static List<DeviceInfo> Detect()
        {
            var pnp = UsbDevices();
            var rich = AdbDevices();
            var res = new List<DeviceInfo>(rich);
            foreach (var g in pnp)
            {
                var d = FromPnp(g);
                if (d == null) continue;
                // Пристрій, який уже бачить adb чи fastboot, не дублюємо.
                if (d.Kind == "android" && rich.Any(r => (g.Serial.Length > 0 && r.Serial.Equals(g.Serial, StringComparison.OrdinalIgnoreCase)) ||
                                                         ((g.Adb && r.Transport == "adb") || (g.Boot && r.Transport == "fastboot")))) continue;
                res.Add(d);
            }
            return res;
        }

        // ── фонове стеження ───────────────────────────────────────────────────

        static Thread watcher;
        public static volatile bool Paused;

        // Раз на кілька секунд перевіряти, що підключено; changed — коли список змінився.
        public static void Watch(Action<List<DeviceInfo>> changed)
        {
            if (watcher != null) return;
            watcher = new Thread(() =>
            {
                string last = null;
                while (true)
                {
                    try
                    {
                        if (!Paused)
                        {
                            var list = Detect();
                            string sig = string.Join(";", list.Select(d => d.Key + "|" + d.Name + "|" + d.Props.Count).ToArray());
                            if (sig != last) { last = sig; changed(list); }
                        }
                    }
                    catch (Exception ex) { if (Environment.GetEnvironmentVariable("OPENFORGE_FAKE_USB") != null) Console.Error.WriteLine("devices: " + ex); }
                    Thread.Sleep(4000);
                }
            }) { IsBackground = true, Priority = ThreadPriority.BelowNormal };
            watcher.Start();
        }
    }
}
