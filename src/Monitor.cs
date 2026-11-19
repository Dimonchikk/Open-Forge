// Open Forge — навантаження ПК для бокового меню: процесор, оперативна пам'ять, відеокарта, відеопам'ять.
// Без сторонніх програм: процесор — GetSystemTimes, пам'ять — GlobalMemoryStatusEx,
// відеокарта NVIDIA — бібліотека драйвера nvml.dll; інші відеокарти — лічильники Windows «GPU Engine».
// Викликати Sample() з фонового потоку (раз на 1–2 секунди).
using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;

namespace OpenForge
{
    static class PcMonitor
    {
        // Значення у відсотках (-1 — невідомо) і в гігабайтах.
        public static volatile float Cpu = -1, Ram = -1, Gpu = -1, Vram = -1;
        public static double RamUsedGb, RamTotalGb, VramUsedGb, VramTotalGb;

        // ── процесор ─────────────────────────────────────────────────────────────

        [DllImport("kernel32.dll")]
        static extern bool GetSystemTimes(out long idle, out long kernel, out long user);
        static long lastIdle, lastTotal;

        static void SampleCpu()
        {
            long idle, kernel, user;
            if (!GetSystemTimes(out idle, out kernel, out user)) return;
            long total = kernel + user;   // kernel уже містить idle
            long dTotal = total - lastTotal, dIdle = idle - lastIdle;
            if (lastTotal != 0 && dTotal > 0) Cpu = (float)Math.Max(0, Math.Min(100, 100.0 * (dTotal - dIdle) / dTotal));
            lastIdle = idle;
            lastTotal = total;
        }

        // ── пам'ять ──────────────────────────────────────────────────────────────

        [StructLayout(LayoutKind.Sequential)]
        class MemoryStatusEx
        {
            public uint Length = (uint)Marshal.SizeOf(typeof(MemoryStatusEx));
            public uint MemoryLoad;
            public ulong TotalPhys, AvailPhys, TotalPageFile, AvailPageFile, TotalVirtual, AvailVirtual, AvailExtendedVirtual;
        }

        [DllImport("kernel32.dll")]
        static extern bool GlobalMemoryStatusEx([In, Out] MemoryStatusEx m);

        static void SampleRam()
        {
            var m = new MemoryStatusEx();
            if (!GlobalMemoryStatusEx(m)) return;
            RamTotalGb = m.TotalPhys / 1073741824.0;
            RamUsedGb = (m.TotalPhys - m.AvailPhys) / 1073741824.0;
            Ram = RamTotalGb > 0 ? (float)(100 * RamUsedGb / RamTotalGb) : -1;
        }

        // ── відеокарта NVIDIA (nvml.dll ставиться разом з драйвером) ──────────────

        [StructLayout(LayoutKind.Sequential)] struct NvUtil { public uint Gpu, Memory; }
        [StructLayout(LayoutKind.Sequential)] struct NvMem { public ulong Total, Free, Used; }
        [DllImport("nvml.dll")] static extern int nvmlInit_v2();
        [DllImport("nvml.dll")] static extern int nvmlDeviceGetHandleByIndex_v2(uint index, out IntPtr device);
        [DllImport("nvml.dll")] static extern int nvmlDeviceGetUtilizationRates(IntPtr device, out NvUtil util);
        [DllImport("nvml.dll")] static extern int nvmlDeviceGetMemoryInfo(IntPtr device, out NvMem mem);

        static int nvState;   // 0 — не пробували, 1 — працює, -1 — немає
        static IntPtr nvDev;

        static bool SampleNvidia()
        {
            if (nvState < 0) return false;
            try
            {
                if (nvState == 0)
                {
                    if (nvmlInit_v2() != 0 || nvmlDeviceGetHandleByIndex_v2(0, out nvDev) != 0) { nvState = -1; return false; }
                    nvState = 1;
                }
                NvUtil u;
                NvMem m;
                if (nvmlDeviceGetUtilizationRates(nvDev, out u) == 0) Gpu = Math.Min(100, u.Gpu);
                if (nvmlDeviceGetMemoryInfo(nvDev, out m) == 0 && m.Total > 0)
                {
                    VramTotalGb = m.Total / 1073741824.0;
                    VramUsedGb = m.Used / 1073741824.0;
                    Vram = (float)(100 * VramUsedGb / VramTotalGb);
                }
                return true;
            }
            catch { nvState = -1; return false; }   // DllNotFoundException — не NVIDIA
        }

        // ── інші відеокарти: лічильники Windows 10/11 ───────────────────────────

        static int pdhState;
        static DateTime nextScan;
        static PerformanceCounter[] engines = new PerformanceCounter[0], memory = new PerformanceCounter[0];

        static void SampleCounters()
        {
            if (pdhState < 0) return;
            try
            {
                if (DateTime.UtcNow >= nextScan)   // список процесів міняється — перечитуємо раз на 10 с
                {
                    nextScan = DateTime.UtcNow.AddSeconds(10);
                    var eng = new PerformanceCounterCategory("GPU Engine").GetInstanceNames().Where(n => n.EndsWith("engtype_3D")).ToArray();
                    var mem = new PerformanceCounterCategory("GPU Adapter Memory").GetInstanceNames();
                    foreach (var c in engines) c.Dispose();
                    foreach (var c in memory) c.Dispose();
                    engines = eng.Select(n => new PerformanceCounter("GPU Engine", "Utilization Percentage", n, true)).ToArray();
                    memory = mem.Select(n => new PerformanceCounter("GPU Adapter Memory", "Dedicated Usage", n, true)).ToArray();
                    foreach (var c in engines) c.NextValue();   // перше значення завжди 0
                    pdhState = 1;
                    return;
                }
                float g = 0;
                foreach (var c in engines) try { g += c.NextValue(); } catch { }
                Gpu = Math.Min(100, g);
                double used = 0;
                foreach (var c in memory) try { used = Math.Max(used, c.NextValue()); } catch { }
                VramTotalGb = Hardware.VramGb;
                VramUsedGb = used / 1073741824.0;
                Vram = VramTotalGb > 0 ? (float)Math.Min(100, 100 * VramUsedGb / VramTotalGb) : -1;
            }
            catch { pdhState = -1; }
        }

        public static bool Frozen;   // для знімків екрана: значення задаються ззовні

        public static void Sample()
        {
            if (Frozen) return;
            try { SampleCpu(); } catch { }
            try { SampleRam(); } catch { }
            if (!SampleNvidia()) SampleCounters();
        }
    }
}
