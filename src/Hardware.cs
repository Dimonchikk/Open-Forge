// Open Forge — що за ПК: оперативна пам'ять, потоки процесора, відеокарта та її відеопам'ять.
// Від цього залежать рекомендації моделей: що влазить у відеопам'ять — швидко, частково в RAM — повільніше.
using System;
using System.Management;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace OpenForge
{
    static class Hardware
    {
        [StructLayout(LayoutKind.Sequential)]
        class MemoryStatusEx
        {
            public uint Length = (uint)Marshal.SizeOf(typeof(MemoryStatusEx));
            public uint MemoryLoad;
            public ulong TotalPhys, AvailPhys, TotalPageFile, AvailPageFile, TotalVirtual, AvailVirtual, AvailExtendedVirtual;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool GlobalMemoryStatusEx([In, Out] MemoryStatusEx buffer);

        public static double RamGb, VramGb;
        public static string GpuName = "";
        public static int Threads = Environment.ProcessorCount;
        static bool detected;

        // Визначити один раз (кілька сотень мілісекунд через WMI) — з фонового потоку.
        public static void Detect()
        {
            if (detected) return;
            detected = true;
            try
            {
                var m = new MemoryStatusEx();
                if (GlobalMemoryStatusEx(m)) RamGb = m.TotalPhys / 1073741824.0;
            }
            catch { }
            DetectGpu();
        }

        // Обсяг відеопам'яті: WMI (AdapterRAM) обрізаний до 4 ГБ, тому беремо 64-бітне значення з реєстру драйвера.
        static void DetectGpu()
        {
            try
            {
                using (var cls = Registry.LocalMachine.OpenSubKey("SYSTEM\\CurrentControlSet\\Control\\Class\\{4d36e968-e325-11ce-bfc1-08002be10318}"))
                {
                    if (cls == null) return;
                    foreach (string sub in cls.GetSubKeyNames())
                    {
                        if (sub.Length != 4) continue;
                        using (var k = cls.OpenSubKey(sub))
                        {
                            if (k == null) continue;
                            object q = k.GetValue("HardwareInformation.qwMemorySize");
                            double gb = 0;
                            if (q is long) gb = (long)q / 1073741824.0;
                            else if (q is byte[] && ((byte[])q).Length >= 8) gb = BitConverter.ToInt64((byte[])q, 0) / 1073741824.0;
                            if (gb > VramGb)
                            {
                                VramGb = gb;
                                GpuName = Convert.ToString(k.GetValue("DriverDesc") ?? "");
                            }
                        }
                    }
                }
            }
            catch { }
            if (VramGb > 0) return;
            try
            {
                using (var s = new ManagementObjectSearcher("SELECT Name, AdapterRAM FROM Win32_VideoController"))
                    foreach (ManagementObject o in s.Get())
                    {
                        double gb = Convert.ToUInt64(o["AdapterRAM"] ?? 0UL) / 1073741824.0;
                        if (gb > VramGb) { VramGb = gb; GpuName = Convert.ToString(o["Name"] ?? ""); }
                    }
            }
            catch { }
        }
    }
}
