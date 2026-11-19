// Open Forge — вікно «Агент»: вибір папки проєкту й рівня дозволів.
using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace OpenForge
{
    class AgentDialog : Form
    {
        static readonly string[] Perms = { "Питати завжди", "Авто", "Повний доступ" };
        static readonly string[] PermHints =
        {
            "Кожну дію агента (створення файлу, команду, видалення) ти підтверджуєш сам.",
            "Файли створює й змінює сам, а видалення й запуск команд — лише з твого дозволу.",
            "Агент діє без жодних підтверджень, включно з командами. Вмикай лише для папок, які не шкода.",
        };

        public string Folder;
        public AgentPermission Permission;
        public bool TurnOff;

        readonly InputBox path = new InputBox();
        readonly Segmented seg = new Segmented { Items = Perms, OnBg = true };
        readonly Label hint;

        public AgentDialog(string folder, AgentPermission perm, bool isOn)
        {
            Text = "Агент — робота з файлами";
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(Ui.S(560), Ui.S(330));
            BackColor = Ui.Bg;
            Font = Ui.Body;
            Permission = perm;

            int x = Ui.S(24), w = ClientSize.Width - 2 * x, y = Ui.S(20);
            Ui.MakeLabel(this, "Агент працює з файлами в одній папці", new Rectangle(x, y, w, Ui.S(28)), Ui.Section, Ui.CInk, Ui.CBg);
            y += Ui.S(30);
            Ui.MakeLabel(this, "Модель сама створює й змінює файли, а за дозволом — запускає команди. Вийти за межі цієї папки вона не може.",
                new Rectangle(x, y, w, Ui.S(40)), Ui.Body, Ui.CInk2, Ui.CBg);
            y += Ui.S(52);

            path.Around = () => Ui.Bg;
            path.Box.Hint = "Папка проєкту, наприклад D:\\Projects\\my-app";
            path.Box.Text = folder ?? "";
            var pick = new PillBtn { Icon = "folder", Text = "Обрати…", OnBg = true };
            int pw = pick.Natural();
            path.Bounds = new Rectangle(x, y, w - pw - Ui.S(10), Ui.S(38));
            pick.Bounds = new Rectangle(x + w - pw, y + Ui.S(3), pw, Ui.S(32));
            pick.Click += (s, e) => Browse();
            Controls.Add(path);
            Controls.Add(pick);
            y += Ui.S(54);

            Ui.MakeLabel(this, "Дозволи", new Rectangle(x, y, w, Ui.S(20)), Ui.BodySemi, Ui.CInk, Ui.CBg);
            y += Ui.S(26);
            seg.Bounds = new Rectangle(x, y, w, Ui.S(32));
            seg.Selected = (int)perm;
            seg.ItemClicked += i => { seg.Selected = i; Permission = (AgentPermission)i; UpdateHint(); };
            Controls.Add(seg);
            y += Ui.S(40);
            hint = Ui.MakeLabel(this, "", new Rectangle(x, y, w, Ui.S(38)), Ui.Small, Ui.CInk2, Ui.CBg);
            UpdateHint();

            var ok = new PillBtn { Primary = true, Text = isOn ? "Зберегти" : "Увімкнути агента", OnBg = true };
            var cancel = new PillBtn { Text = "Скасувати", OnBg = true };
            int by = ClientSize.Height - Ui.S(56), okW = ok.Natural(), cW = cancel.Natural();
            ok.Bounds = new Rectangle(x + w - okW, by, okW, Ui.S(36));
            cancel.Bounds = new Rectangle(ok.Left - Ui.S(10) - cW, by, cW, Ui.S(36));
            ok.Click += (s, e) => Accept();
            cancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
            Controls.Add(ok);
            Controls.Add(cancel);
            if (isOn)
            {
                var off = new PillBtn { Text = "Вимкнути агента", OnBg = true, Fore = () => Ui.Red };
                off.Bounds = new Rectangle(x, by, off.Natural(), Ui.S(36));
                off.Click += (s, e) => { TurnOff = true; DialogResult = DialogResult.OK; Close(); };
                Controls.Add(off);
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Ui.StyleCaption(this);
        }

        void UpdateHint()
        {
            int i = seg.Selected;
            hint.Text = PermHints[i];
            hint.ForeColor = i == 2 ? Ui.Orange : Ui.Ink2;
        }

        void Browse()
        {
            using (var d = new FolderBrowserDialog())
            {
                d.Description = "Папка, з якою працюватиме агент";
                d.ShowNewFolderButton = true;
                string cur = path.Box.Text.Trim();
                if (cur.Length > 0 && Directory.Exists(cur)) d.SelectedPath = cur;
                if (d.ShowDialog(this) == DialogResult.OK) path.Box.Text = d.SelectedPath;
            }
        }

        void Accept()
        {
            string p = path.Box.Text.Trim().Trim('"');
            if (p.Length == 0) { Browse(); return; }
            try
            {
                p = Path.GetFullPath(Environment.ExpandEnvironmentVariables(p));
                string rootOfDrive = Path.GetPathRoot(p);
                if (string.Equals(p.TrimEnd('\\'), rootOfDrive.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                {
                    MessageBox.Show(this, "Обери окрему папку проєкту, а не цілий диск.", Text);
                    return;
                }
                if (!Directory.Exists(p))
                {
                    if (MessageBox.Show(this, "Папки " + p + " ще немає. Створити?", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                    Directory.CreateDirectory(p);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Не вдалося використати цю папку:\n" + ex.Message, Text);
                return;
            }
            Folder = p;
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
