// Open Forge — картинки в чаті: підготовка фото/скріншотів для моделі й мініатюри для показу.
// Картинка зменшується до 1600 px по довшій стороні й зберігається як JPEG у base64 — так її приймає Ollama
// (поле «images» повідомлення), і такою ж вона лягає в історію чату.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace OpenForge
{
    static class Pictures
    {
        public const int MaxPerMessage = 4;
        const int MaxSide = 1600;
        static readonly string[] Exts = { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp", ".tif", ".tiff" };

        public static bool IsImageFile(string path)
        {
            return Exts.Contains((Path.GetExtension(path) ?? "").ToLowerInvariant());
        }

        public const string DialogFilter = "Зображення|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff|Усі файли|*.*";

        // Зменшити й закодувати в JPEG base64.
        public static string Encode(Image src)
        {
            double k = Math.Min(1.0, (double)MaxSide / Math.Max(src.Width, src.Height));
            int w = Math.Max(1, (int)(src.Width * k)), h = Math.Max(1, (int)(src.Height * k));
            using (var bmp = new Bitmap(w, h, PixelFormat.Format24bppRgb))
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.White);   // прозорий PNG — на білому
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.DrawImage(src, 0, 0, w, h);
                }
                var codec = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);
                var ps = new EncoderParameters(1);
                ps.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 88L);
                using (var ms = new MemoryStream())
                {
                    bmp.Save(ms, codec, ps);
                    return Convert.ToBase64String(ms.ToArray());
                }
            }
        }

        // Файл → base64 або null (не картинка / не відкривається).
        public static string FromFile(string path)
        {
            try
            {
                if (new FileInfo(path).Length > 40L * 1024 * 1024) return null;
                using (var fs = File.OpenRead(path))
                using (var img = Image.FromStream(fs))
                    return Encode(img);
            }
            catch { return null; }
        }

        public static Bitmap Decode(string b64)
        {
            try
            {
                var ms = new MemoryStream(Convert.FromBase64String(b64));
                return new Bitmap(ms);
            }
            catch { return null; }
        }

        // Мініатюра заданої висоти (ширина пропорційна, але не більше maxW).
        public static Bitmap Thumb(Image src, int h, int maxW)
        {
            double k = (double)h / src.Height;
            int w = (int)Math.Min(maxW, src.Width * k);
            if (w < 8) w = 8;
            var bmp = new Bitmap(w, h);
            using (var g = Graphics.FromImage(bmp))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                // Обрізаємо по центру, якщо картинка ширша за maxW.
                double sw = w / k;
                g.DrawImage(src, new Rectangle(0, 0, w, h), new RectangleF((float)((src.Width - sw) / 2), 0, (float)sw, src.Height), GraphicsUnit.Pixel);
            }
            return bmp;
        }

        // Відкрити картинку в переглядачі Windows.
        public static void Open(string b64)
        {
            try
            {
                string path = Path.Combine(Path.GetTempPath(), "openforge_image_" + (b64.GetHashCode() & 0x7fffffff) + ".jpg");
                if (!File.Exists(path)) File.WriteAllBytes(path, Convert.FromBase64String(b64));
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
            }
            catch { }
        }
    }

    // Смужка доданих (ще не надісланих) картинок над полем вводу: мініатюри з хрестиком.
    class AttachStrip : UiControl
    {
        public readonly List<string> Images = new List<string>();
        readonly List<Bitmap> thumbs = new List<Bitmap>();
        int hover = -1;
        public event Action Changed;

        public AttachStrip() { OnBg = true; }

        int Size1 { get { return Ui.S(56); } }

        public bool Add(string b64)
        {
            if (b64 == null || Images.Count >= Pictures.MaxPerMessage) return false;
            var img = Pictures.Decode(b64);
            if (img == null) return false;
            Images.Add(b64);
            using (img) thumbs.Add(Pictures.Thumb(img, Size1, Size1));
            Invalidate();
            if (Changed != null) Changed();
            return true;
        }

        public void Clear()
        {
            Images.Clear();
            foreach (var t in thumbs) t.Dispose();
            thumbs.Clear();
            Invalidate();
            if (Changed != null) Changed();
        }

        Rectangle Item(int i) { return new Rectangle(i * (Size1 + Ui.S(8)), Ui.S(4), Size1, Size1); }
        Rectangle Close(int i) { var r = Item(i); int d = Ui.S(18); return new Rectangle(r.Right - d + Ui.S(4), r.Y - Ui.S(4), d, d); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Prep(e);
            for (int i = 0; i < thumbs.Count; i++)
            {
                var r = Item(i);
                using (var path = Ui.Round(r, Ui.S(10)))
                {
                    g.SetClip(path);
                    g.DrawImage(thumbs[i], r.X + (r.Width - thumbs[i].Width) / 2, r.Y);
                    g.ResetClip();
                    using (var pen = new Pen(Ui.CardLine, Math.Max(1f, Ui.Scale))) g.DrawPath(pen, path);
                }
                var c = Close(i);
                using (var b = new SolidBrush(i == hover ? Ui.Red : Color.FromArgb(200, 40, 40, 44))) g.FillEllipse(b, c);
                Icons.Draw(g, "x", RectangleF.Inflate(c, -Ui.S(5), -Ui.S(5)), Color.White);
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int h = -1;
            for (int i = 0; i < thumbs.Count; i++) if (Close(i).Contains(e.Location)) h = i;
            if (h != hover) { hover = h; Invalidate(); }
            Cursor = h >= 0 ? Cursors.Hand : Cursors.Default;
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            for (int i = 0; i < thumbs.Count; i++)
                if (Close(i).Contains(e.Location))
                {
                    Images.RemoveAt(i);
                    thumbs[i].Dispose();
                    thumbs.RemoveAt(i);
                    hover = -1;
                    Invalidate();
                    if (Changed != null) Changed();
                    return;
                }
        }
    }
}
