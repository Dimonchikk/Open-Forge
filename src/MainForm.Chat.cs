// Open Forge — розділ «Чат».
// Відповідь моделі приходить у фоновому потоці (Ollama.Chat), а вікно раз на 100 мс забирає накопичений текст
// і перемальовує лише змінене. Тож вікно не «зависає», хоч би якою довгою була відповідь чи вставлений текст.
using System;
using System.Collections.Generic;
using System.Linq;
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
        PillBtn modelBtn;
        RoundBtn sendBtn, folderBtn, agentOffBtn, imageBtn;
        AttachStrip attach;
        HistoryList historyList;
        string chatId, chatTitle;   // поточний чат в історії (null — ще не збережений)
        readonly System.Windows.Forms.Timer speakTimer = new System.Windows.Forms.Timer { Interval = 250 };
        PillBtn agentChip, editChip;
        RoundBtn editOffBtn;
        ChatMessage editing;          // повідомлення, яке зараз редагуєш (null — пишеш нове)
        InputBox historySearch;
        string model;

        // Агент: якщо обрано папку, повідомлення йдуть агенту, який працює з файлами в ній.
        string agentFolder;
        AgentPermission agentPerm = AgentPermission.Auto;
        AgentRun agent;
        int shotsSeen;

        ChatStream stream;
        ChatMessage answer;
        int seen;
        readonly System.Windows.Forms.Timer pump = new System.Windows.Forms.Timer { Interval = 100 };

        Control BuildChat()
        {
            chatPage = new UiControl { OnBg = true };
            model = Settings.Get("Model", "");

            chatList = new MessageList { Editable = true };
            chatPage.Controls.Add(chatList);
            chatList.Speak += SpeakMessage;
            chatList.EditMessage += StartEdit;
            chatList.DeleteMessage += DeleteMessage;
            chatList.Regenerate += Regenerate;
            chatList.PreviewHtml += code => Preview.OpenHtml(this, code);
            chatList.PreviewFile += path => Preview.OpenFile(this, path);

            historyList = new HistoryList();
            historyList.Open += id => { ShowPage(0); OpenChat(id); };
            historyList.Rename += RenameChat;
            historyList.Delete += DeleteChat;
            historyList.SetItems(ChatStore.All());
            sidebar.List = historyList;   // список чатів — у боковому меню під «Новим чатом»
            historySearch = new InputBox { Around = () => Ui.Side };
            historySearch.Box.Hint = "Пошук у чатах";
            historySearch.Box.TextChanged += (s, e) => RefreshHistory();
            sidebar.Search = historySearch;
            speakTimer.Tick += (s, e) =>
            {
                chatList.Invalidate();
                foreach (var c in cards) c.List.Invalidate();
                if (Speaker.Current == null) speakTimer.Stop();
            };

            modelBtn = new PillBtn { Icon = "models", OnBg = true, Text = "Модель" };
            modelBtn.Click += (s, e) => ShowModelMenu();
            chatPage.Controls.Add(modelBtn);


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

            // Фото й файли: кнопка «+», Ctrl+V зі скріншотом чи файлами, перетягування.
            imageBtn = new RoundBtn { Icon = "plus", Outline = true, Bg = () => Ui.Card };
            imageBtn.Tip = "Додати фото, скріншот або файл — PDF, Word, текст, код (або Ctrl+V, або перетягни)";
            imageBtn.Click += (s, e) => PickFiles();
            chatInput.Controls.Add(imageBtn);
            attach = new AttachStrip();
            attach.Changed += LayoutChat;
            chatPage.Controls.Add(attach);
            chatInput.Box.ImagePasted += img => AddImage(Pictures.Encode(img));
            chatInput.Box.FilesPasted += AddFiles;
            foreach (Control c in new Control[] { chatList, chatInput, chatInput.Box, chatPage })
            {
                c.AllowDrop = true;
                c.DragEnter += (s, e) => e.Effect = DroppedFiles(e.Data).Length > 0 ? DragDropEffects.Copy : DragDropEffects.None;
                c.DragDrop += (s, e) => AddFiles(DroppedFiles(e.Data));
            }

            editChip = new PillBtn { Icon = "new", OnBg = true, Text = "Редагуєш повідомлення — Enter надішле його заново", FillColor = () => Ui.Mix(Ui.Bg, Ui.Orange, 0.14f), Fore = () => Ui.Ink };
            chatPage.Controls.Add(editChip);
            editOffBtn = new RoundBtn { Icon = "x", Bg = () => Ui.Bg };
            editOffBtn.Tip = "Скасувати редагування";
            editOffBtn.Click += (s, e) => { SetEditing(null); chatInput.Box.Clear(); attach.Clear(); };
            chatPage.Controls.Add(editOffBtn);
            editChip.Visible = editOffBtn.Visible = false;

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

            int ih = chatInput.Wanted;
            chatInput.Bounds = new Rectangle(x, h - ih - Ui.S(20), col, ih);
            int b = Ui.S(32), pad = Ui.S(5);
            sendBtn.Bounds = new Rectangle(chatInput.Width - b - pad - Ui.S(1), chatInput.Height - b - pad - Ui.S(1), b, b);
            folderBtn.Bounds = new Rectangle(pad + Ui.S(1), chatInput.Height - b - pad - Ui.S(1), b, b);
            imageBtn.Bounds = new Rectangle(folderBtn.Right + Ui.S(4), folderBtn.Top, b, b);
            chatInput.LeftInset = 2 * b;
            chatInput.RightInset = b;
            chatInput.Relayout();

            int listBottom = chatInput.Top - Ui.S(8);
            attach.Visible = attach.Count > 0;
            if (attach.Visible)
            {
                attach.Bounds = new Rectangle(x, listBottom - Ui.S(66), col, Ui.S(66));
                listBottom = attach.Top - Ui.S(4);
            }
            agentChip.Visible = agentOffBtn.Visible = agentFolder != null;
            if (agentFolder != null)
            {
                int cw = Math.Min(col - Ui.S(40), agentChip.Natural()), ch = Ui.S(30);
                agentChip.Bounds = new Rectangle(x, listBottom - ch, cw, ch);
                agentOffBtn.Bounds = new Rectangle(agentChip.Right + Ui.S(6), agentChip.Top, ch, ch);
                listBottom = agentChip.Top - Ui.S(6);
            }
            editChip.Visible = editOffBtn.Visible = editing != null;
            if (editing != null)
            {
                int cw = Math.Min(col - Ui.S(40), editChip.Natural()), ch = Ui.S(30);
                editChip.Bounds = new Rectangle(x, listBottom - ch, cw, ch);
                editOffBtn.Bounds = new Rectangle(editChip.Right + Ui.S(6), editChip.Top, ch, ch);
                listBottom = editChip.Top - Ui.S(6);
            }
            chatList.Bounds = new Rectangle(0, Ui.S(42), w, Math.Max(0, listBottom - Ui.S(42)));
        }

        // ── картинки ────────────────────────────────────────────────────────────

        static string[] DroppedFiles(IDataObject data)
        {
            try
            {
                if (!data.GetDataPresent(DataFormats.FileDrop)) return new string[0];
                return ((string[])data.GetData(DataFormats.FileDrop)).Where(f => Pictures.IsImageFile(f) || FileText.CanRead(f)).ToArray();
            }
            catch { return new string[0]; }
        }

        void PickFiles()
        {
            string filter = "Фото й документи|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp;*.pdf;*.docx;*.txt;*.md;*.csv;*.json;*.html;*.css;*.js;*.py;*.cs|" +
                            "Зображення|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp|" + FileText.DialogFilter;
            using (var d = new OpenFileDialog { Filter = filter, Multiselect = true, Title = "Фото, скріншот або файл для моделі" })
                if (d.ShowDialog(this) == DialogResult.OK) AddFiles(d.FileNames);
            chatInput.Box.Focus();
        }

        // Картинки — одразу; документи читаються у фоні (великий PDF може читатися кілька секунд).
        void AddFiles(string[] files)
        {
            var docs = new List<string>();
            foreach (string f in files)
            {
                if (Pictures.IsImageFile(f)) { if (!AddImage(Pictures.FromFile(f))) break; }
                else docs.Add(f);
            }
            if (docs.Count == 0) return;
            if (attach.Files.Count + docs.Count > FileText.MaxPerMessage)
            {
                MessageBox.Show(this, "До одного повідомлення можна додати до " + FileText.MaxPerMessage + " файлів.", "Open Forge");
                docs = docs.Take(Math.Max(0, FileText.MaxPerMessage - attach.Files.Count)).ToList();
            }
            ThreadPool.QueueUserWorkItem(_ =>
            {
                var read = new List<FileAttachment>();
                var errors = new List<string>();
                foreach (string f in docs)
                {
                    string err;
                    var fa = FileText.Read(f, out err);
                    if (fa != null) read.Add(fa); else errors.Add(err);
                }
                try
                {
                    BeginInvoke((Action)(() =>
                    {
                        foreach (var fa in read) attach.AddFile(fa);
                        if (errors.Count > 0) MessageBox.Show(this, string.Join("\n", errors.ToArray()), "Open Forge");
                        chatInput.Box.Focus();
                    }));
                }
                catch { }
            });
        }

        bool AddImage(string b64)
        {
            if (b64 == null) return true;
            if (attach.Images.Count >= Pictures.MaxPerMessage)
            {
                MessageBox.Show(this, "До одного повідомлення можна додати до " + Pictures.MaxPerMessage + " картинок.", "Open Forge");
                return false;
            }
            attach.Add(b64);
            if (ModelSeesImages(model) == false)
                chatList.Add(new ChatMessage("error", "Модель «" + model + "» не бачить картинок. Обери модель з позначкою «Розуміє картинки» — наприклад Qwen 3 VL 8B, Qwen 2.5 VL 7B чи Gemma 3 (фільтр «Зображення» в «Налаштування → Моделі»)."));
            chatInput.Box.Focus();
            return true;
        }

        // true/false — точно знаємо з каталогу; null — модель не з каталогу, хай вирішить Ollama.
        static bool? ModelSeesImages(string name)
        {
            var c = Catalog.Find(name ?? "");
            if (c == null) return null;
            return c.Vision;
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
            var items = new List<PopupItem>();
            // Найсильніші зверху.
            foreach (var m in models.OrderByDescending(x => { var c = Catalog.Find(x.Name); return c != null ? c.Power : 0; }).ThenBy(x => x.Name))
            {
                string name = m.Name;
                var it = new PopupItem(name, name == model, () =>
                {
                    model = name;
                    Settings.Set("Model", name);
                    modelBtn.Text = name;
                    LayoutChat();
                });
                var c = Catalog.Find(name);
                string gb = m.SizeGb.ToString("0.0") + " ГБ";
                if (c != null)
                {
                    // Як у каталозі: позначки, сила 1–10, чи потягне ПК.
                    it.Text = c.Name;
                    it.Badges = ModelList.BadgesOf(c);
                    it.Power = c.Power;
                    var fit = c.FitFor(Hardware.VramGb, Hardware.RamGb);
                    if (Hardware.Done) it.Dot = fit == Fit.Fast ? Ui.Green : fit == Fit.Slow ? Ui.Orange : Ui.Red;
                    it.Detail = "· " + gb + " · " + (!Hardware.Done ? name : fit == Fit.Fast ? "швидко" : fit == Fit.Slow ? "повільніше — частина в RAM" : "не потягне ПК");
                }
                else it.Detail = gb + " · " + (string.IsNullOrEmpty(m.Params) ? "" : m.Params + " параметрів · ") + "немає в каталозі";
                items.Add(it);
            }
            PopupMenu.Show(modelBtn, items);
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
            Speaker.Stop();
            chatList.Clear();
            SetEditing(null);
            chatId = chatTitle = null;
            historyList.Selected = null;
            historyList.Invalidate();
            if (current == 0) sidebar.Selected = 0;
            chatInput.Box.Focus();
        }

        // ── історія ─────────────────────────────────────────────────────────────

        void SaveChat()
        {
            if (chatId == null) return;
            ChatStore.Save(chatId, chatTitle, chatList.Items);
            historyList.Selected = chatId;
            RefreshHistory();
            if (current == 0) sidebar.Selected = -1;
        }

        void RefreshHistory()
        {
            string q = historySearch != null ? historySearch.Box.Text.Trim() : "";
            historyList.EmptyText = q.Length > 0 ? "Нічого не знайдено за «" + q + "»." : null;
            historyList.SetItems(ChatStore.Search(q));
        }

        void OpenChat(string id)
        {
            if (Busy || id == chatId) return;
            Speaker.Stop();
            string title;
            var msgs = ChatStore.Load(id, out title);
            chatList.Clear();
            SetEditing(null);
            foreach (var m in msgs) chatList.Items.Add(m);
            chatList.Changed(null);
            chatList.ScrollToEnd();
            chatId = id;
            chatTitle = title;
            historyList.Selected = id;
            historyList.Invalidate();
            sidebar.Selected = -1;
            UpdateChatEmpty();
            chatInput.Box.Focus();
        }

        void RenameChat(string id)
        {
            var e = ChatStore.All().Find(c => c.Id == id);
            if (e == null) return;
            using (var d = new TextPrompt("Перейменувати чат", "Нова назва чату", e.Title))
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                ChatStore.Rename(id, d.Value);
                if (id == chatId) chatTitle = d.Value;
            }
            RefreshHistory();
        }

        void DeleteChat(string id)
        {
            var e = ChatStore.All().Find(c => c.Id == id);
            if (e == null) return;
            if (MessageBox.Show(this, "Видалити чат «" + e.Title + "»? Його не можна буде відновити.", "Open Forge",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            if (id == chatId) { if (Busy) return; NewChat(); }
            ChatStore.Delete(id);
            RefreshHistory();
        }

        // ── озвучка ─────────────────────────────────────────────────────────────

        void SpeakMessage(ChatMessage m)
        {
            if (Speaker.Current == m) { Speaker.Stop(); chatList.Invalidate(); foreach (var c in cards) c.List.Invalidate(); return; }
            Speaker.Speak(m, m.Display, AskVoiceDownload, err =>
            {
                if (string.IsNullOrEmpty(err)) return;
                try { BeginInvoke((Action)(() => MessageBox.Show(this, "Не вдалося прочитати текст:\n" + err, "Open Forge"))); } catch { }
            });
            speakTimer.Start();
        }

        // З фонового потоку: спитати, чи завантажити український голос.
        bool AskVoiceDownload()
        {
            try
            {
                return (bool)Invoke((Func<bool>)(() => MessageBox.Show(this,
                    "Щоб читати українською, потрібен голос Piper — безкоштовний, працює без інтернету.\n\n" +
                    "Завантажити його зараз? Це один раз, близько 100 МБ.", "Український голос",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes));
            }
            catch { return false; }
        }

        void Send()
        {
            if (stream != null) { stream.Cancel(); return; }   // кнопка під час відповіді — «Зупинити»
            if (agent != null) { agent.Cancel(); return; }
            string text = chatInput.Box.Text.Trim();
            var images = attach.Images.Count > 0 ? new List<string>(attach.Images) : null;
            var files = attach.Files.Count > 0 ? new List<FileAttachment>(attach.Files) : null;
            if (text.Length == 0 && images == null && files == null) return;
            if (text.Length == 0) text = images != null ? "Що на цьому зображенні?" : "Що в цьому файлі? Коротко перекажи головне.";
            if (!ModelReady()) return;
            if (editing != null)
            {
                // Редагування: прибираємо старе повідомлення й усе після нього, далі — як нове.
                int i = chatList.Items.IndexOf(editing);
                bool first = chatList.Items.FindIndex(x => x.IsUser) == i;
                if (first && chatTitle == ChatStore.TitleFrom(editing.Raw)) chatTitle = ChatStore.TitleFrom(text);   // назва — з першого питання
                if (i >= 0) RemoveFrom(i);
                SetEditing(null);
            }
            var um = new ChatMessage("user", text) { Images = images, Files = files };
            chatList.Add(um);
            attach.Clear();
            chatInput.Box.Clear();
            if (chatId == null) { chatId = ChatStore.NewId(); chatTitle = ChatStore.TitleFrom(text); }
            Ask(um);
        }

        // Стан Ollama перевіряється раз на 3 с, тож одразу після запуску він ще може бути невідомим —
        // не блокуємо: якщо Ollama справді не працює, запит сам поверне зрозумілу помилку.
        bool ModelReady()
        {
            if (!string.IsNullOrEmpty(model)) return true;
            chatList.Add(new ChatMessage("error", ollamaVersion == null && !firstPoll
                ? "Ollama не запущена. Запусти її й надішли ще раз."
                : "Спершу завантаж модель у розділі «Моделі»."));
            return false;
        }

        void RemoveFrom(int index)
        {
            while (chatList.Items.Count > index) chatList.Items.RemoveAt(chatList.Items.Count - 1);
            chatList.ClearSelection();
            chatList.Changed(null);
        }

        // Розмова для моделі — усе до повідомлення um включно. Файли й картинки «важкі», тож повний текст файлів
        // іде лише з останнього повідомлення з файлами, а картинок — лише 4 найсвіжіші.
        List<Turn> History(ChatMessage um)
        {
            var history = new List<Turn>();
            bool filesSent = false;
            int end = chatList.Items.IndexOf(um);
            for (int i = end; i >= 0; i--)
            {
                var m = chatList.Items[i];
                if (m.Role != "user" && m.Role != "assistant") continue;
                string t = m.Role == "assistant" ? m.Display : m.Raw;
                if (m.Role == "user" && m.Files != null && m.Files.Count > 0)
                {
                    if (!filesSent) { t = FileText.ForModel(m.Files, t); filesSent = true; }
                    else t = "(Раніше прикріплені файли: " + string.Join(", ", m.Files.Select(f => f.Name).ToArray()) + ")\n\n" + t;
                }
                if (t.Length > 0) history.Insert(0, new Turn(m.Role, t) { Images = m.Role == "user" ? m.Images : null });
            }
            // Модель точно не бачить картинок (з каталогу) — не шлемо їх зовсім, навіть зі старих повідомлень,
            // інакше Ollama відмовляє на кожне наступне повідомлення в цьому чаті.
            if (ModelSeesImages(model) == false)
                foreach (var t in history)
                    if (t.Images != null && t.Images.Count > 0)
                    {
                        t.Content = "(До цього повідомлення була прикріплена картинка, але поточна модель картинок не бачить.)\n" + t.Content;
                        t.Images = null;
                    }
            int left = 4;
            for (int i = history.Count - 1; i >= 0; i--)
            {
                var im = history[i].Images;
                if (im == null) continue;
                if (left <= 0) history[i].Images = null;
                else if (im.Count > left) history[i].Images = im.GetRange(im.Count - left, left);
                left -= im.Count;
            }
            return history;
        }

        // Надіслати моделі розмову, що закінчується твоїм повідомленням um (воно вже в стрічці).
        void Ask(ChatMessage um)
        {
            var history = History(um);
            SaveChat();
            answer = new ChatMessage("assistant", "") { Streaming = true, Model = model };
            chatList.Add(answer);

            if (agentFolder != null)
            {
                string task = history.Count > 0 ? history[history.Count - 1].Content : um.Raw;
                history.RemoveAt(history.Count - 1);
                var run = new AgentRun(agentFolder, model, agentPerm, history, Settings.Get("SystemPrompt", ""));
                run.TaskImages = ModelSeesImages(model) == false ? null : um.Images;
                run.ModelSeesImages = ModelSeesImages(model) != false;
                shotsSeen = 0;
                run.Confirm = AskAgentPermission;
                agent = run;
                answer.Folder = run.Folder;
                seen = 0;
                var at = new Thread(() => run.Run(task));
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

        // ── редагування, видалення, «Ще раз» ───────────────────────────────────

        void SetEditing(ChatMessage m)
        {
            editing = m;
            if (editChip != null) LayoutChat();
        }

        void StartEdit(ChatMessage m)
        {
            if (Busy || !m.IsUser) return;
            attach.Clear();
            if (m.Images != null) foreach (string b in m.Images) attach.Add(b);
            if (m.Files != null) foreach (var f in m.Files) attach.AddFile(f);
            chatInput.Box.Text = m.Raw;
            chatInput.Box.SelectionStart = chatInput.Box.TextLength;
            SetEditing(m);
            chatInput.Box.Focus();
        }

        void DeleteMessage(ChatMessage m)
        {
            if (Busy) return;
            int i = chatList.Items.IndexOf(m);
            if (i < 0) return;
            int j = i + 1;
            if (m.IsUser) while (j < chatList.Items.Count && !chatList.Items[j].IsUser) j++;   // разом із відповіддю на нього
            if (editing != null && chatList.Items.IndexOf(editing) >= i && chatList.Items.IndexOf(editing) < j) { SetEditing(null); chatInput.Box.Clear(); attach.Clear(); }
            Speaker.Stop();
            chatList.Items.RemoveRange(i, j - i);
            chatList.ClearSelection();
            chatList.Changed(null);
            if (chatId != null)
            {
                if (chatList.Items.Any(x => x.Role == "user" || x.Role == "assistant")) SaveChat();
                else { ChatStore.Delete(chatId); chatId = chatTitle = null; historyList.Selected = null; RefreshHistory(); }
            }
        }

        // Перегенерувати останню відповідь: прибрати її й спитати модель ще раз.
        void Regenerate(ChatMessage m)
        {
            if (Busy || !ModelReady()) return;
            int i = chatList.Items.IndexOf(m);
            int u = i - 1;
            while (u >= 0 && !chatList.Items[u].IsUser) u--;
            if (u < 0) return;
            Speaker.Stop();
            RemoveFrom(u + 1);
            Ask(chatList.Items[u]);
        }

        void Pump()
        {
            if (agent != null) { PumpAgent(); return; }
            if (stream == null) { pump.Stop(); return; }
            string t = stream.TakeIfChanged(ref seen);
            if (t != null)
            {
                answer.Raw = t;
                if (answer.ThinkSec == 0 && t.IndexOf("</think>", StringComparison.Ordinal) >= 0)
                    answer.ThinkSec = Math.Max(1, (DateTime.UtcNow - (stream.FirstToken == default(DateTime) ? answer.StartedAt : stream.FirstToken)).TotalSeconds);
                chatList.Changed(answer);
            }
            else if (answer.Display.Length == 0) chatList.Invalidate();   // крапки й таймер «Думає над відповіддю»
            FinishIfDone(false);
        }

        void PumpAgent()
        {
            var run = agent;
            string t = run.TakeIfChanged(ref seen);
            var shots = run.Shots(ref shotsSeen);
            if (shots != null) answer.Images = shots;
            if (t != null || shots != null) { if (t != null) answer.Raw = t; chatList.Changed(answer); }
            else chatList.Invalidate();   // крапки й таймер у рядку кроків
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
                    : run.Error));
            }
            chatList.Changed(answer);
            SaveChat();
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
            if (answer.ThinkSec == 0 && answer.Think.Length > 0)
                answer.ThinkSec = Math.Max(1, (s.Finished - (s.FirstToken == default(DateTime) ? s.Started : s.FirstToken)).TotalSeconds);
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
            SaveChat();
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
