// Open Forge — розділ «Чат».
// Відповідь моделі приходить у фоновому потоці (Ollama.Chat), а вікно раз на 100 мс забирає накопичений текст
// і перемальовує лише змінене. Тож вікно не «зависає», хоч би якою довгою була відповідь чи вставлений текст.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace OpenForge
{
    partial class MainForm
    {
        UiControl chatPage;
        MessageList chatList;
        InputBox chatInput;
        PillBtn modelBtn, newChatBtn;
        RoundBtn sendBtn, folderBtn, agentOffBtn;
        PillBtn agentChip;
        string model;

        // Агент: якщо обрано папку, повідомлення йдуть агенту, який працює з файлами в ній.
        string agentFolder;
        AgentPermission agentPerm = AgentPermission.Auto;
        AgentRun agent;

        ChatStream stream;
        ChatMessage answer;
        int seen;
        readonly System.Windows.Forms.Timer pump = new System.Windows.Forms.Timer { Interval = 100 };

        Control BuildChat()
        {
            chatPage = new UiControl { OnBg = true };
            model = Settings.Get("Model", "");

            chatList = new MessageList();
            chatPage.Controls.Add(chatList);

            modelBtn = new PillBtn { Icon = "models", OnBg = true, Text = "Модель" };
            modelBtn.Click += (s, e) => ShowModelMenu();
            chatPage.Controls.Add(modelBtn);

            newChatBtn = new PillBtn { Icon = "new", OnBg = true, Text = "Новий чат" };
            newChatBtn.Click += (s, e) => NewChat();
            chatPage.Controls.Add(newChatBtn);

            chatInput = new InputBox { Multiline = true, MaxLines = 8 };
            chatInput.Box.Hint = "Напиши повідомлення…";
            chatInput.Box.EnterSubmits = true;
            chatInput.Box.Submit += Send;
            chatInput.WantHeight += LayoutChat;
            chatPage.Controls.Add(chatInput);

            sendBtn = new RoundBtn { Icon = "send", Primary = true, Bg = () => Ui.Card };
            sendBtn.Tip = "Надіслати (Enter)";
            sendBtn.Click += (s, e) => Send();
            chatInput.Controls.Add(sendBtn);

            folderBtn = new RoundBtn { Icon = "folder", Outline = true, Bg = () => Ui.Card };
            folderBtn.Tip = "Агент: модель працює з файлами в обраній папці";
            folderBtn.Click += (s, e) => AgentSetup();
            chatInput.Controls.Add(folderBtn);

            agentChip = new PillBtn { Icon = "folder", OnBg = true, FillColor = () => Ui.Mix(Ui.Bg, Ui.Blue, 0.13f), Fore = () => Ui.Blue };
            agentChip.Click += (s, e) => AgentSetup();
            chatPage.Controls.Add(agentChip);
            agentOffBtn = new RoundBtn { Icon = "x", Bg = () => Ui.Bg };
            agentOffBtn.Tip = "Вимкнути агента";
            agentOffBtn.Click += (s, e) => SetAgent(null, agentPerm);
            chatPage.Controls.Add(agentOffBtn);
            chatList.OpenFolder += OpenInExplorer;
            int perm = Settings.GetInt("AgentPerm", 1);
            SetAgent(Settings.GetBool("AgentOn", false) ? Settings.Get("AgentFolder", "") : null, (AgentPermission)Math.Max(0, Math.Min(2, perm)));

            pump.Tick += (s, e) => Pump();
            chatPage.Resize += (s, e) => LayoutChat();
            UpdateChatEmpty();
            return chatPage;
        }

        void LayoutChat()
        {
            if (chatPage == null) return;
            int w = chatPage.Width, h = chatPage.Height;
            int col = Math.Min(Ui.S(780), w - Ui.S(48));
            int x = (w - col) / 2;

            modelBtn.Bounds = new Rectangle(x, Ui.S(2), Math.Min(Ui.S(320), modelBtn.Natural()), Ui.S(32));
            newChatBtn.Bounds = new Rectangle(x + col - newChatBtn.Natural(), Ui.S(2), newChatBtn.Natural(), Ui.S(32));

            int ih = chatInput.Wanted;
            chatInput.Bounds = new Rectangle(x, h - ih - Ui.S(20), col, ih);
            int b = Ui.S(32), pad = Ui.S(5);
            sendBtn.Bounds = new Rectangle(chatInput.Width - b - pad - Ui.S(1), chatInput.Height - b - pad - Ui.S(1), b, b);
            folderBtn.Bounds = new Rectangle(pad + Ui.S(1), chatInput.Height - b - pad - Ui.S(1), b, b);
            chatInput.LeftInset = b - Ui.S(4);
            chatInput.RightInset = b;
            chatInput.Relayout();

            int listBottom = chatInput.Top - Ui.S(8);
            agentChip.Visible = agentOffBtn.Visible = agentFolder != null;
            if (agentFolder != null)
            {
                int cw = Math.Min(col - Ui.S(40), agentChip.Natural()), ch = Ui.S(30);
                agentChip.Bounds = new Rectangle(x, chatInput.Top - ch - Ui.S(8), cw, ch);
                agentOffBtn.Bounds = new Rectangle(agentChip.Right + Ui.S(6), agentChip.Top, ch, ch);
                listBottom = agentChip.Top - Ui.S(6);
            }
            chatList.Bounds = new Rectangle(0, Ui.S(42), w, Math.Max(0, listBottom - Ui.S(42)));
        }

        // ── агент ───────────────────────────────────────────────────────────────

        static readonly string[] PermNames = { "питати завжди", "авто", "повний доступ" };

        void AgentSetup()
        {
            if (Busy) return;
            string start = agentFolder ?? Settings.Get("AgentFolder", "");
            using (var d = new AgentDialog(start, agentPerm, agentFolder != null))
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                SetAgent(d.TurnOff ? null : d.Folder, d.Permission);
            }
            chatInput.Box.Focus();
        }

        void SetAgent(string folder, AgentPermission perm)
        {
            if (folder != null && folder.Length == 0) folder = null;
            agentFolder = folder;
            agentPerm = perm;
            if (folder != null)
            {
                Settings.Set("AgentFolder", folder);
                string name = System.IO.Path.GetFileName(folder.TrimEnd('\\'));
                agentChip.Text = "Агент у папці «" + (name.Length > 0 ? name : folder) + "» · " + PermNames[(int)perm];
            }
            Settings.SetBool("AgentOn", folder != null);
            Settings.SetInt("AgentPerm", (int)perm);
            folderBtn.Primary = folder != null;
            folderBtn.Outline = folder == null;
            folderBtn.Tip = folder != null ? "Агент увімкнений: " + folder : "Агент: модель працює з файлами в обраній папці";
            folderBtn.Invalidate();
            chatInput.Box.Hint = folder != null ? "Що зробити в папці? Наприклад: «створи сайт-візитку»…" : "Напиши повідомлення…";
            LayoutChat();
        }

        static void OpenInExplorer(string folder)
        {
            try { System.Diagnostics.Process.Start("explorer.exe", "\"" + folder + "\""); } catch { }
        }

        // Питання з фонового потоку агента: показуємо у вікні й чекаємо відповіді.
        bool AskAgentPermission(string tool, string title)
        {
            bool danger = tool == "run_command" || tool == "delete_file";
            try
            {
                return (bool)Invoke((Func<bool>)(() =>
                    MessageBox.Show(this, title + "\n\nДозволити цю дію?", "Дозволити дію агента?", MessageBoxButtons.YesNo,
                        danger ? MessageBoxIcon.Warning : MessageBoxIcon.Question) == DialogResult.Yes));
            }
            catch { return false; }
        }

        // ── модель ──────────────────────────────────────────────────────────────

        void OnModelsChanged()
        {
            if (models.Count > 0 && !models.Exists(m => m.Name == model))
            {
                model = models[0].Name;
                Settings.Set("Model", model);
            }
            modelBtn.Text = models.Count == 0 ? "Немає моделей" : model;
            modelBtn.Enabled = models.Count > 0;
            UpdateChatEmpty();
            LayoutChat();
            FillCompareModels();
        }

        void ShowModelMenu()
        {
            if (models.Count == 0) return;
            var menu = new ContextMenuStrip();
            foreach (var m in models)
            {
                string name = m.Name;
                var it = new ToolStripMenuItem(name + "   ·   " + m.SizeGb.ToString("0.0") + " ГБ");
                it.Checked = name == model;
                it.Click += (s, e) =>
                {
                    model = name;
                    Settings.Set("Model", name);
                    modelBtn.Text = name;
                    LayoutChat();
                };
                menu.Items.Add(it);
            }
            menu.Show(modelBtn, new Point(0, modelBtn.Height + Ui.S(4)));
        }

        void UpdateChatEmpty()
        {
            if (ollamaVersion == null && !firstPoll)
            {
                chatList.EmptyTitle = "Ollama не запущена";
                chatList.EmptyText = "Open Forge спілкується з моделями через Ollama. Запусти її (значок біля годинника) — і чат запрацює. Встановлення Ollama з програми з'явиться в «Налаштуваннях».";
            }
            else if (ollamaVersion != null && models.Count == 0)
            {
                chatList.EmptyTitle = "Ще немає жодної моделі";
                chatList.EmptyText = "Завантаж першу модель у розділі «Моделі» — там видно, які підходять саме твоєму ПК.";
            }
            else
            {
                chatList.EmptyTitle = "Про що поговоримо?";
                chatList.EmptyText = "Модель працює прямо на твоєму ПК — без інтернету й без підписок.\nEnter — надіслати, Shift+Enter — новий рядок.";
            }
            chatList.Invalidate();
        }

        // ── розмова ─────────────────────────────────────────────────────────────

        bool Busy { get { return stream != null || agent != null; } }

        void NewChat()
        {
            if (stream != null) stream.Cancel();
            if (agent != null) agent.Cancel();
            FinishIfDone(true);
            chatList.Clear();
            chatInput.Box.Focus();
        }

        void Send()
        {
            if (stream != null) { stream.Cancel(); return; }   // кнопка під час відповіді — «Зупинити»
            if (agent != null) { agent.Cancel(); return; }
            string text = chatInput.Box.Text.Trim();
            if (text.Length == 0) return;
            // Стан Ollama перевіряється раз на 3 с, тож одразу після запуску він ще може бути невідомим —
            // не блокуємо: якщо Ollama справді не працює, запит сам поверне зрозумілу помилку.
            if (string.IsNullOrEmpty(model))
            {
                chatList.Add(new ChatMessage("error", ollamaVersion == null && !firstPoll
                    ? "Ollama не запущена. Запусти її й надішли ще раз."
                    : "Спершу завантаж модель у розділі «Моделі»."));
                return;
            }

            var history = new List<Turn>();
            foreach (var m in chatList.Items)
            {
                if (m.Role != "user" && m.Role != "assistant") continue;
                string t = m.Role == "assistant" ? m.Display : m.Raw;
                if (t.Length > 0) history.Add(new Turn(m.Role, t));
            }
            history.Add(new Turn("user", text));

            chatList.Add(new ChatMessage("user", text));
            answer = new ChatMessage("assistant", "") { Streaming = true, Model = model };
            chatList.Add(answer);
            chatInput.Box.Clear();

            if (agentFolder != null)
            {
                history.RemoveAt(history.Count - 1);
                var run = new AgentRun(agentFolder, model, agentPerm, history, Settings.Get("SystemPrompt", ""));
                run.Confirm = AskAgentPermission;
                agent = run;
                answer.Folder = run.Folder;
                seen = 0;
                var at = new Thread(() => run.Run(text));
                at.IsBackground = true;
                at.Start();
                pump.Start();
                SetSending(true);
                return;
            }

            var s = new ChatStream();
            stream = s;
            seen = 0;
            string mdl = model, sys = Settings.Get("SystemPrompt", "");
            var th = new Thread(() => Ollama.Chat(mdl, sys, history, s));
            th.IsBackground = true;
            th.Start();
            pump.Start();
            SetSending(true);
        }

        void Pump()
        {
            if (agent != null) { PumpAgent(); return; }
            if (stream == null) { pump.Stop(); return; }
            string t = stream.TakeIfChanged(ref seen);
            if (t != null)
            {
                answer.Raw = t;
                chatList.Changed(answer);
            }
            FinishIfDone(false);
        }

        void PumpAgent()
        {
            var run = agent;
            string t = run.TakeIfChanged(ref seen);
            if (t != null) { answer.Raw = t; chatList.Changed(answer); }
            if (!run.Done) return;
            agent = null;
            pump.Stop();
            answer.Streaming = false;
            answer.Raw = run.Text;
            int changed = new HashSet<string>(run.Changed).Count;
            answer.Stats = (changed > 0 ? "змінено файлів: " + changed : "файли не змінювались") + (run.Cancelled ? " · зупинено" : "");
            if (answer.Display.Length == 0 && run.Error == null) answer.Raw = run.Cancelled ? "Зупинено." : "Готово.";
            if (run.Error != null)
            {
                if (answer.Display.Length == 0) chatList.Remove(answer);
                chatList.Add(new ChatMessage("error", run.ToolsUnsupported
                    ? "Модель «" + model + "» не вміє працювати з файлами (не підтримує інструменти). Для агента обери, наприклад, Qwen 3 8B або Qwen 2.5 Coder 7B."
                    : "Агент зупинився з помилкою: " + run.Error));
            }
            chatList.Changed(answer);
            SetSending(false);
        }

        void FinishIfDone(bool force)
        {
            if (agent != null && force)
            {
                agent = null;
                pump.Stop();
                answer.Streaming = false;
                SetSending(false);
                return;
            }
            var s = stream;
            if (s == null || !(s.Done || force)) return;
            stream = null;
            pump.Stop();
            answer.Streaming = false;
            answer.Raw = s.Text;
            if (answer.Display.Length == 0) chatList.Remove(answer);
            else
            {
                var parts = new List<string>();
                if (s.TokensPerSecond > 0) parts.Add(s.TokensPerSecond.ToString("0.0") + " ток/с");
                if (s.Done) parts.Add((s.Finished - s.Started).TotalSeconds.ToString("0.0") + " с");
                if (s.Cancelled) parts.Add("зупинено");
                answer.Stats = string.Join(" · ", parts.ToArray());
            }
            if (s.Error != null) chatList.Add(new ChatMessage("error", s.Error));
            chatList.Changed(answer);
            SetSending(false);
        }

        void SetSending(bool on)
        {
            sendBtn.Icon = on ? "stop" : "send";
            sendBtn.Tip = on ? "Зупинити" : "Надіслати (Enter)";
            sendBtn.Invalidate();
            modelBtn.Enabled = !on && models.Count > 0;
            folderBtn.Enabled = agentChip.Enabled = agentOffBtn.Enabled = !on;
        }
    }
}
