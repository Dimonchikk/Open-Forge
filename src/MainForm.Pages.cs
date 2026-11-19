// Open Forge — розділ «Налаштування».
using System;
using System.Drawing;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace OpenForge
{
    partial class MainForm
    {
        InputBox sysInput;
        Label pcBody;
        bool hwStarted;

        Control BuildSettings()
        {
            var host = new ScrollHost();
            var page = host.Content;

            sysInput = new InputBox { Multiline = true, MinLines = 4, MaxLines = 12 };
            sysInput.Box.Hint = "Наприклад: «Відповідай українською, коротко й по суті»";
            sysInput.Box.Text = Settings.Get("SystemPrompt", "");
            sysInput.Box.Leave += (s, e) => SaveSystemPrompt();
            sysInput.WantHeight += host.Rearrange;
            page.Controls.Add(sysInput);
            var sysNote = Ui.MakeLabel(page, "Ця інструкція йде моделі першою в кожному чаті. Зберігається сама.", Rectangle.Empty, Ui.Small, Ui.CInk3, Ui.CBg);

            pcBody = Ui.MakeLabel(page, "Визначаю…", Rectangle.Empty, Ui.Body, Ui.CInk, Ui.CCard);

            var about = Ui.MakeLabel(page, "Open Forge " + AppVersion.Text + " · © 2026 Дмитро Бричнік\nЛокальні нейромережі через Ollama: усе працює на твоєму ПК, нічого не йде в інтернет.",
                Rectangle.Empty, Ui.Body, Ui.CInk2, Ui.CCard);
            var check = new PillBtn { Text = "Перевірити оновлення" };
            check.Click += (s, e) =>
            {
                check.Enabled = false;
                check.Text = "Перевіряю…";
                ThreadPool.QueueUserWorkItem(_ =>
                {
                    string v = Updates.CheckNow();
                    try
                    {
                        BeginInvoke((Action)(() =>
                        {
                            sidebar.NewVersion = Updates.Latest;
                            check.Enabled = true;
                            check.Text = v == null ? "Немає зв'язку з GitHub" : Updates.Latest != null ? "Є нова версія " + v : "У тебе остання версія";
                            host.Rearrange();
                        }));
                    }
                    catch { }
                });
            };
            page.Controls.Add(check);
            var site = new PillBtn { Text = "Сайт програми" };
            site.Click += (s, e) => Program.Open("https://dimonchikk.github.io/Open-Forge/");
            page.Controls.Add(site);
            // Видалення — та сама дія, що й у «Параметри → Програми»: окремий процес з ключем /uninstall
            // спитає підтвердження, закриє це вікно й прибере програму (Ollama й моделі залишаться).
            var remove = new PillBtn { Text = "Видалити Open Forge", Fore = () => Ui.Red };
            remove.Click += (s, e) =>
            {
                try { System.Diagnostics.Process.Start(Application.ExecutablePath, "/uninstall"); }
                catch (Exception ex) { MessageBox.Show(this, "Не вдалося почати видалення:\n" + ex.Message, "Open Forge"); }
            };
            page.Controls.Add(remove);

            host.Arrange += () =>
            {
                int x = Ui.S(32), w = Math.Min(host.Width - Ui.S(64), Ui.S(720)), y = Ui.S(8);
                page.Cards.Clear();

                page.SetCaption("sys", new Rectangle(x + Ui.S(4), y, w, Ui.S(24)), "Інструкція для моделі|системний промпт");
                y += Ui.S(32);
                sysInput.Bounds = new Rectangle(x, y, w, sysInput.Wanted);
                y = sysInput.Bottom + Ui.S(8);
                sysNote.Bounds = new Rectangle(x + Ui.S(6), y, w, Ui.S(18));
                y += Ui.S(42);

                page.SetCaption("pc", new Rectangle(x + Ui.S(4), y, w, Ui.S(24)), "Твій ПК|від цього залежать рекомендації моделей");
                y += Ui.S(32);
                int ph = TextRenderer.MeasureText(pcBody.Text, Ui.Body, new Size(w - Ui.S(40), int.MaxValue), TextFormatFlags.WordBreak).Height;
                var pc = new Rectangle(x, y, w, ph + Ui.S(36));
                page.Cards.Add(pc);
                pcBody.Bounds = new Rectangle(pc.X + Ui.S(20), pc.Y + Ui.S(18), pc.Width - Ui.S(40), ph + Ui.S(2));
                y = pc.Bottom + Ui.S(34);

                page.SetCaption("about", new Rectangle(x + Ui.S(4), y, w, Ui.S(24)), "Про програму");
                y += Ui.S(32);
                int ah = TextRenderer.MeasureText(about.Text, Ui.Body, new Size(w - Ui.S(40), int.MaxValue), TextFormatFlags.WordBreak).Height;
                var ab = new Rectangle(x, y, w, ah + Ui.S(86));
                page.Cards.Add(ab);
                about.Bounds = new Rectangle(ab.X + Ui.S(20), ab.Y + Ui.S(18), ab.Width - Ui.S(40), ah + Ui.S(2));
                check.Bounds = new Rectangle(ab.X + Ui.S(20), about.Bottom + Ui.S(14), check.Natural(), Ui.S(32));
                site.Bounds = new Rectangle(check.Right + Ui.S(10), check.Top, site.Natural(), Ui.S(32));
                int rw = remove.Natural();
                remove.Bounds = new Rectangle(ab.Right - Ui.S(20) - rw, check.Top, rw, Ui.S(32));
            };
            return host;
        }

        void SaveSystemPrompt()
        {
            if (sysInput != null) Settings.Set("SystemPrompt", sysInput.Box.Text.Trim());
        }

        void RefreshSettings()
        {
            if (hwStarted) return;
            hwStarted = true;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                Hardware.Detect();
                var sb = new StringBuilder();
                sb.Append("Відеокарта: ").Append(Hardware.GpuName.Length > 0 ? Hardware.GpuName : "не визначено");
                if (Hardware.VramGb > 0) sb.Append(" · ").Append(Hardware.VramGb.ToString("0")).Append(" ГБ відеопам'яті");
                sb.Append("\nОперативна пам'ять: ").Append(Hardware.RamGb > 0 ? Hardware.RamGb.ToString("0") + " ГБ" : "не визначено");
                sb.Append("\nПроцесор: ").Append(Hardware.Threads).Append(" потоків");
                string text = sb.ToString();
                try { BeginInvoke((Action)(() => { pcBody.Text = text; ((ScrollHost)pages[3]).Rearrange(); })); } catch { }
            });
        }
    }
}
