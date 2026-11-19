// Open Forge — озвучка відповідей («Слухати»).
// • Текст з кирилицею читає український голос Piper (офлайн, безкоштовний): його разово завантажуємо
//   за згодою користувача (~100 МБ) у %LOCALAPPDATA%\OpenForge\voice. Якщо у Windows є свій український
//   голос (SAPI), беремо його без завантажень.
// • Інший текст читають голоси Windows (SAPI) — вони вже є в системі.
// Довгий текст ділиться на шматки: поки звучить один, наступний уже готується.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Media;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace OpenForge
{
    static class Speaker
    {
        public static readonly string VoiceDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenForge\\voice");
        const string PiperZip = "https://github.com/rhasspy/piper/releases/download/2023.11.14-2/piper_windows_amd64.zip";
        const string VoiceUrl = "https://huggingface.co/rhasspy/piper-voices/resolve/main/uk/uk_UA/ukrainian_tts/medium/uk_UA-ukrainian_tts-medium.onnx";
        const string VoiceName = "uk_UA-ukrainian_tts-medium.onnx";

        static string PiperExe { get { return Path.Combine(VoiceDir, "piper\\piper.exe"); } }
        static string VoiceFile { get { return Path.Combine(VoiceDir, VoiceName); } }
        public static bool PiperReady { get { return File.Exists(PiperExe) && File.Exists(VoiceFile) && File.Exists(VoiceFile + ".json"); } }

        // Що зараз відбувається — для підпису під відповіддю.
        public static volatile object Current;        // повідомлення, яке звучить або готується
        public static volatile string Status;         // «Готую…», «Завантажую голос… 43 %» або null
        static volatile int generation;
        [ThreadStatic] static object sapiVoice;   // у кожного потоку читання свій голос (COM)
        static SoundPlayer player;
        static Process piper;

        public static bool IsUkrainian(string text)
        {
            int cyr = 0, lat = 0;
            foreach (char c in text)
            {
                if (c >= 'Ѐ' && c <= 'ӿ') cyr++;
                else if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z')) lat++;
            }
            return cyr > lat;
        }

        // Текст для читання: без блоків коду, розмітки й посилань.
        public static string Clean(string text)
        {
            string t = Regex.Replace(text, "```[\\s\\S]*?(```|$)", " (фрагмент коду) ");
            t = Regex.Replace(t, "`([^`]*)`", "$1");
            t = Regex.Replace(t, "https?://\\S+", " ");
            t = Regex.Replace(t, "^\\s*#{1,6}\\s*", "", RegexOptions.Multiline);
            t = Regex.Replace(t, "^\\s*[-*•]\\s+", "", RegexOptions.Multiline);
            t = t.Replace("**", "").Replace("__", "").Replace("*", "").Replace("|", " ");
            return t.Trim();
        }

        // Шматки по ~400 символів на межі речень.
        static List<string> Chunks(string text)
        {
            var res = new List<string>();
            foreach (string para in text.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string p = para.Trim();
                while (p.Length > 450)
                {
                    int cut = -1;
                    foreach (char c in new[] { '.', '!', '?', ';', ',' })
                    {
                        int i = p.LastIndexOf(c, 420);
                        if (i > 120) { cut = i + 1; break; }
                    }
                    if (cut < 0) { cut = p.LastIndexOf(' ', 420); if (cut < 120) cut = 420; }
                    res.Add(p.Substring(0, cut).Trim());
                    p = p.Substring(cut).Trim();
                }
                if (p.Length > 0) res.Add(p);
            }
            return res;
        }

        public static void Stop()
        {
            generation++;
            Current = null;
            Status = null;
            try { var p = player; if (p != null) p.Stop(); } catch { }
            try { var pr = piper; if (pr != null && !pr.HasExited) pr.Kill(); } catch { }
        }

        // ── голоси Windows (SAPI) ────────────────────────────────────────────────

        static object Sapi()
        {
            if (sapiVoice != null) return sapiVoice;
            var t = Type.GetTypeFromProgID("SAPI.SpVoice");
            if (t == null) return null;
            sapiVoice = Activator.CreateInstance(t);
            return sapiVoice;
        }

        // Чи є у Windows голос для мови (LCID у шістнадцятковому вигляді, наприклад 422 — українська).
        static bool SapiPick(string lcidHex)
        {
            try
            {
                var v = Sapi();
                if (v == null) return false;
                var tp = v.GetType();
                object voices = tp.InvokeMember("GetVoices", BindingFlags.InvokeMethod, null, v, new object[] { "Language=" + lcidHex, "" });
                int n = (int)voices.GetType().InvokeMember("Count", BindingFlags.GetProperty, null, voices, null);
                if (n == 0) return false;
                object first = voices.GetType().InvokeMember("Item", BindingFlags.InvokeMethod, null, voices, new object[] { 0 });
                tp.InvokeMember("Voice", BindingFlags.SetProperty, null, v, new object[] { first });
                return true;
            }
            catch { return false; }
        }

        static void SapiDefault()
        {
            try
            {
                var v = Sapi();
                var tp = v.GetType();
                object voices = tp.InvokeMember("GetVoices", BindingFlags.InvokeMethod, null, v, new object[] { "", "" });
                object first = voices.GetType().InvokeMember("Item", BindingFlags.InvokeMethod, null, voices, new object[] { 0 });
                tp.InvokeMember("Voice", BindingFlags.SetProperty, null, v, new object[] { first });
            }
            catch { }
        }

        static void SapiSpeak(string text, int gen)
        {
            var v = Sapi();
            var tp = v.GetType();
            tp.InvokeMember("Speak", BindingFlags.InvokeMethod, null, v, new object[] { text, 1 });   // 1 — асинхронно
            Status = null;
            // Чекаємо кінця (або «Зупинити»), щоб кнопка повернулась у «Слухати».
            while (gen == generation)
            {
                bool done;
                try { done = (bool)tp.InvokeMember("WaitUntilDone", BindingFlags.InvokeMethod, null, v, new object[] { 200 }); }
                catch { break; }
                if (done) break;
            }
            if (gen != generation)   // натиснули «Зупинити» — обірвати мовлення
                try { tp.InvokeMember("Speak", BindingFlags.InvokeMethod, null, v, new object[] { "", 2 }); } catch { }
        }

        // ── Piper ────────────────────────────────────────────────────────────────

        static void Download(string url, string path, string what, long approx, int gen)
        {
            try { ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072; } catch { }
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.UserAgent = "OpenForge/" + AppVersion.Text;
            req.Timeout = 30000;
            req.ReadWriteTimeout = 60000;
            string tmp = path + ".part";
            using (var resp = req.GetResponse())
            using (var rs = resp.GetResponseStream())
            using (var fs = File.Create(tmp))
            {
                long total = resp.ContentLength > 0 ? resp.ContentLength : approx, got = 0;
                var buf = new byte[81920];
                int n;
                while ((n = rs.Read(buf, 0, buf.Length)) > 0)
                {
                    if (gen != generation) throw new OperationCanceledException();
                    fs.Write(buf, 0, n);
                    got += n;
                    Status = "Завантажую " + what + "… " + Math.Min(99, got * 100 / Math.Max(1, total)) + " %";
                }
            }
            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);
        }

        static void EnsurePiper(int gen)
        {
            Directory.CreateDirectory(VoiceDir);
            if (!File.Exists(PiperExe))
            {
                string zip = Path.Combine(VoiceDir, "piper.zip");
                Download(PiperZip, zip, "програму голосу", 22000000, gen);
                Status = "Розпаковую…";
                string target = Path.Combine(VoiceDir, "piper");
                if (Directory.Exists(target)) Directory.Delete(target, true);
                ZipFile.ExtractToDirectory(zip, VoiceDir);   // в архіві вже є папка «piper»
                File.Delete(zip);
            }
            if (!File.Exists(VoiceFile + ".json")) Download(VoiceUrl + ".json", VoiceFile + ".json", "голос", 2000, gen);
            if (!File.Exists(VoiceFile)) Download(VoiceUrl, VoiceFile, "голос", 77000000, gen);
        }

        // Синтез одного шматка у wav-файл.
        static string PiperSynth(string text, int index)
        {
            string wav = Path.Combine(Path.GetTempPath(), "openforge_say_" + index + ".wav");
            var psi = new ProcessStartInfo(PiperExe, "--model \"" + VoiceFile + "\" --speaker 1 --output_file \"" + wav + "\"");
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardInput = true;
            psi.RedirectStandardError = true;
            psi.RedirectStandardOutput = true;
            psi.WorkingDirectory = Path.GetDirectoryName(PiperExe);
            using (var p = Process.Start(psi))
            {
                piper = p;
                p.ErrorDataReceived += (s, e) => { };
                p.OutputDataReceived += (s, e) => { };
                p.BeginErrorReadLine();
                p.BeginOutputReadLine();
                byte[] data = new UTF8Encoding(false).GetBytes(text.Replace('\n', ' ') + "\n");
                p.StandardInput.BaseStream.Write(data, 0, data.Length);
                p.StandardInput.Close();
                p.WaitForExit(120000);
                piper = null;
            }
            return File.Exists(wav) ? wav : null;
        }

        static void PiperSpeak(string text, int gen)
        {
            var chunks = Chunks(text);
            if (chunks.Count == 0) return;
            Status = "Готую…";
            string next = PiperSynth(chunks[0], 0);
            for (int i = 0; i < chunks.Count && gen == generation; i++)
            {
                string wav = next;
                if (wav == null) throw new Exception("голос не зміг прочитати текст");
                // Наступний шматок готуємо, поки звучить цей.
                string upcoming = null;
                Thread prep = null;
                if (i + 1 < chunks.Count)
                {
                    int k = i + 1;
                    prep = new Thread(() => { try { upcoming = PiperSynth(chunks[k], k % 2); } catch { } });
                    prep.IsBackground = true;
                    prep.Start();
                }
                Status = null;
                using (var sp = new SoundPlayer(wav))
                {
                    player = sp;
                    sp.PlaySync();
                    player = null;
                }
                if (prep != null) prep.Join();
                next = upcoming;
            }
        }

        // ── вхід ─────────────────────────────────────────────────────────────────

        // Прочитати текст. needPiper(true) спитає дозвіл на завантаження голосу (виклик з фонового потоку).
        // done викликається з фонового потоку після кінця або помилки (текст помилки або null).
        public static void Speak(object owner, string text, Func<bool> askDownload, Action<string> done)
        {
            Stop();
            int gen = ++generation;
            Current = owner;
            Status = "Готую…";
            var t = new Thread(() =>
            {
                string error = null;
                try
                {
                    string clean = Clean(text);
                    if (IsUkrainian(clean))
                    {
                        if (SapiPick("422")) SapiSpeak(clean, gen);
                        else
                        {
                            if (!PiperReady)
                            {
                                if (!askDownload()) { error = ""; return; }
                                EnsurePiper(gen);
                            }
                            if (gen == generation) PiperSpeak(clean, gen);
                        }
                    }
                    else
                    {
                        if (Sapi() == null) throw new Exception("у Windows немає голосів для читання");
                        if (!SapiPick("409")) SapiDefault();   // 409 — англійська
                        SapiSpeak(clean, gen);
                    }
                }
                catch (OperationCanceledException) { }
                catch (Exception ex) { if (gen == generation) error = ex.Message; }
                finally
                {
                    if (gen == generation) { Current = null; Status = null; }
                    if (done != null) done(error);
                }
            });
            t.IsBackground = true;
            t.SetApartmentState(ApartmentState.STA);   // SAPI — COM
            t.Start();
        }
    }
}
