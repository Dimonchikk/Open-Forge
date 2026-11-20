// Open Forge — моделі з Hugging Face, які Ollama не може завантажити сама (наприклад, через задовгу назву репозиторію).
// Open Forge завантажує файл .gguf сам (з продовженням після обриву), рахує його контрольну суму,
// передає файл в Ollama (/api/blobs) і створює модель з короткою назвою (/api/create). Потім тимчасовий файл видаляється.
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace OpenForge
{
    static class HfImport
    {
        static string Dir { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenForge", "gguf"); } }

        public static void Run(PullJob job, string url, long bytes)
        {
            try
            {
                try { ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072; } catch { }   // TLS 1.2
                Directory.CreateDirectory(Dir);
                string file = Path.Combine(Dir, Path.GetFileName(new Uri(url).AbsolutePath));

                // Місце на диску: сам файл + така сама копія в Ollama.
                long have = File.Exists(file) ? new FileInfo(file).Length : 0;
                long need = (bytes - have) + bytes + (512L << 20);
                long free = new DriveInfo(Path.GetPathRoot(Dir)).AvailableFreeSpace;
                if (free < need)
                {
                    job.Error = "замало місця на диску: потрібно близько " + (need / 1e9).ToString("0") + " ГБ, вільно " + (free / 1e9).ToString("0") + " ГБ " +
                                "(файл моделі тимчасово займає місце двічі — поки Ollama його не скопіює)";
                    return;
                }

                if (!Download(job, url, file, bytes)) return;

                // Контрольна сума (Ollama зберігає файли за нею).
                job.Status = "Перевірка файлу";
                string digest = Sha256(job, file);
                if (job.Cancelled || digest == null) return;

                // Передати файл в Ollama.
                job.Status = "Додаю в Ollama";
                if (!Upload(job, file, digest)) return;

                // Створити модель з короткою назвою.
                job.Status = "Створюю модель";
                var body = new Dictionary<string, object>
                {
                    { "model", job.Model },
                    { "files", new Dictionary<string, object> { { Path.GetFileName(file), digest } } },
                    { "stream", false },
                };
                var req = Ollama.PostJsonPublic("/api/create", body, 30 * 60 * 1000);
                job.Attach(req);
                using (var resp = req.GetResponse())
                using (var sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                {
                    string text = sr.ReadToEnd();
                    if (text.Contains("\"error\"")) { job.Error = text; return; }
                }
                try { File.Delete(file); } catch { }   // Ollama має свою копію — тимчасовий файл більше не потрібен
                job.Status = "Готово";
            }
            catch (WebException ex)
            {
                if (job.Cancelled) return;
                string detail = "";
                try { if (ex.Response != null) using (var sr = new StreamReader(ex.Response.GetResponseStream())) detail = sr.ReadToEnd(); } catch { }
                job.Error = ex.Message + (detail.Length > 0 ? " — " + detail : "");
            }
            catch (Exception ex) { if (!job.Cancelled) job.Error = ex.Message; }
            finally { job.Done = true; }
        }

        // Завантаження з продовженням: якщо частина файлу вже є, докачуємо решту.
        static bool Download(PullJob job, string url, string file, long bytes)
        {
            long have = File.Exists(file) ? new FileInfo(file).Length : 0;
            job.Total = bytes;
            job.Completed = have;
            if (have >= bytes) return true;
            job.Status = "Завантаження з Hugging Face";
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.UserAgent = "OpenForge";
            req.Timeout = 60000;
            req.ReadWriteTimeout = 120000;
            if (have > 0) req.AddRange(have);
            job.Attach(req);
            using (var resp = (HttpWebResponse)req.GetResponse())
            {
                bool partial = resp.StatusCode == HttpStatusCode.PartialContent;
                using (var rs = resp.GetResponseStream())
                using (var fs = new FileStream(file, partial ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.Read, 1 << 20))
                {
                    if (!partial) job.Completed = 0;
                    var buf = new byte[1 << 20];
                    int n;
                    while (!job.Cancelled && (n = rs.Read(buf, 0, buf.Length)) > 0)
                    {
                        fs.Write(buf, 0, n);
                        job.Completed += n;
                    }
                }
            }
            if (job.Cancelled) return false;
            long got = new FileInfo(file).Length;
            if (got != bytes) { job.Error = "файл завантажився не повністю (" + (got / 1e9).ToString("0.0") + " з " + (bytes / 1e9).ToString("0.0") + " ГБ). Натисни «Завантажити» ще раз — докачається решта."; return false; }
            return true;
        }

        static string Sha256(PullJob job, string file)
        {
            using (var sha = SHA256.Create())
            using (var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20))
            {
                job.Total = fs.Length;
                job.Completed = 0;
                var buf = new byte[1 << 20];
                int n;
                while ((n = fs.Read(buf, 0, buf.Length)) > 0)
                {
                    if (job.Cancelled) return null;
                    sha.TransformBlock(buf, 0, n, null, 0);
                    job.Completed += n;
                }
                sha.TransformFinalBlock(buf, 0, 0);
                var sb = new StringBuilder("sha256:");
                foreach (byte b in sha.Hash) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }

        static bool Upload(PullJob job, string file, string digest)
        {
            // Може вже бути в Ollama (наприклад, після попередньої спроби).
            try
            {
                var head = Ollama.RequestPublic("/api/blobs/" + digest, "HEAD", 30000);
                using (var r = (HttpWebResponse)head.GetResponse()) if (r.StatusCode == HttpStatusCode.OK) return true;
            }
            catch (WebException) { }

            var req = Ollama.RequestPublic("/api/blobs/" + digest, "POST", 60 * 60 * 1000);
            req.AllowWriteStreamBuffering = false;
            req.SendChunked = false;
            req.ContentType = "application/octet-stream";
            using (var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20))
            {
                req.ContentLength = fs.Length;
                job.Total = fs.Length;
                job.Completed = 0;
                job.Attach(req);
                using (var rs = req.GetRequestStream())
                {
                    var buf = new byte[1 << 20];
                    int n;
                    while ((n = fs.Read(buf, 0, buf.Length)) > 0)
                    {
                        if (job.Cancelled) return false;
                        rs.Write(buf, 0, n);
                        job.Completed += n;
                    }
                }
            }
            using (req.GetResponse()) { }
            return true;
        }
    }
}
