using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace Biubiu
{
    public class MainForm : Form
    {
        readonly FlowLayoutPanel cards = new FlowLayoutPanel();
        readonly Button menuButton = new Button();
        readonly Button orientationButton = new Button();
        readonly Button pinButton = new Button();
        readonly Label emptyLabel = new Label();
        readonly Label statusLabel = new Label();
        readonly System.Windows.Forms.Timer refreshTimer = new System.Windows.Forms.Timer();
        readonly System.Windows.Forms.Timer shimmerTimer = new System.Windows.Forms.Timer();
        readonly Dictionary<long, ProjectCardControl> views = new Dictionary<long, ProjectCardControl>();
        readonly Dictionary<string, string> notes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, RunClocks.Clock> runClocks = RunClocks.Load(RunClocks.DefaultPath());
        readonly Dictionary<long, string> seenStatus = new Dictionary<long, string>();
        readonly string notesPath = CardNotes.DefaultPath();
        readonly string runsPath = RunClocks.DefaultPath();
        readonly string settingsPath = UiSettings.DefaultPath();
        bool vertical;
        bool pinned;
        bool alertsArmed;
        bool soundEnabled;
        int soundVolume;
        bool menuOpen;
        float phase;
        int reloading;
        AppMenuForm openMenu;
        DateTime menuClosedUtc;
        Font emptyFont;

        public MainForm()
        {
            Text = "Biubiu";
            Font = new Font("Segoe UI", 9f);
            BackColor = CardPainter.Canvas;
            StartPosition = FormStartPosition.CenterScreen;
            vertical = UiSettings.LoadVertical(settingsPath);
            pinned = UiSettings.LoadPinned(settingsPath);
            CardPainter.SetScale(UiSettings.LoadScale(settingsPath));
            soundEnabled = UiSettings.LoadSoundEnabled(settingsPath);
            soundVolume = UiSettings.LoadVolume(settingsPath);
            TopMost = pinned;
            try
            {
                notes = CardNotes.Load(notesPath);
            }
            catch (Exception)
            {
                notes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }

            Panel header = new Panel();
            header.Dock = DockStyle.Top;
            header.Height = 40;
            header.BackColor = CardPainter.Canvas;
            SetupPill(menuButton, new Size(30, 30));
            menuButton.AccessibleName = "菜单";
            menuButton.TabStop = false;
            menuButton.Click += delegate { ToggleMenu(); };
            menuButton.Paint += PaintMenu;
            menuButton.FlatAppearance.MouseOverBackColor = CardPainter.Canvas;
            menuButton.FlatAppearance.MouseDownBackColor = CardPainter.Canvas;
            SetupPill(orientationButton, new Size(64, 30));
            orientationButton.Click += delegate { ToggleOrientation(); };
            orientationButton.Paint += PaintPill;
            SetupPill(pinButton, new Size(30, 30));
            pinButton.Text = "\u7f6e\u9876";
            pinButton.Click += delegate { TogglePinned(); };
            pinButton.Paint += PaintPin;
            header.Controls.Add(menuButton);
            header.Controls.Add(orientationButton);
            header.Controls.Add(pinButton);
            header.Resize += delegate { PlaceHeaderButtons(header); };

            statusLabel.Dock = DockStyle.Bottom;
            statusLabel.Height = 0;
            statusLabel.TextAlign = ContentAlignment.MiddleLeft;
            statusLabel.Padding = new Padding(20, 0, 0, 0);
            statusLabel.ForeColor = Color.FromArgb(255, 153, 62, 62);
            statusLabel.BackColor = CardPainter.Canvas;
            SetStatus(Program.StartupWarning);

            cards.Dock = DockStyle.Fill;
            cards.AutoScroll = true;
            cards.BackColor = CardPainter.Canvas;
            cards.Padding = new Padding(8, 0, 8, 6);
            cards.WrapContents = false;
            emptyLabel.AutoSize = true;
            emptyLabel.ForeColor = CardPainter.MutedColor;
            emptyLabel.Text = "没有发现 Cursor 窗口";
            emptyLabel.Margin = new Padding(4, 12, 4, 4);
            ApplyEmptyFont();

            Controls.Add(cards);
            Controls.Add(statusLabel);
            Controls.Add(header);

            refreshTimer.Interval = 100;
            refreshTimer.Tick += delegate { Reload(); };
            shimmerTimer.Interval = 16;
            shimmerTimer.Tick += delegate { AdvanceShimmer(); };
            ApplyOrientation(false);
            Reload();
            AppIcon.Apply(this);
            refreshTimer.Start();
            shimmerTimer.Start();
        }

        static void SetupPill(Button button, Size size)
        {
            button.Size = size;
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 0;
            button.BackColor = Color.White;
            button.ForeColor = CardPainter.TitleColor;
            button.Font = new Font("Segoe UI", 9f);
            button.Cursor = Cursors.Hand;
        }

        void PlaceHeaderButtons(Panel header)
        {
            int y = (header.Height - orientationButton.Height) / 2;
            menuButton.Location = new Point(8, y);
            orientationButton.Location = new Point(header.Width - orientationButton.Width - 8, y);
            pinButton.Location = new Point(orientationButton.Left - pinButton.Width - 6, y);
            RoundButton(menuButton);
            RoundButton(orientationButton);
            RoundButton(pinButton);
        }

        static void RoundButton(Button button)
        {
            Region previous = button.Region;
            using (GraphicsPath path = CardPainter.RoundRect(new Rectangle(0, 0, button.Width - 1, button.Height - 1), 15))
                button.Region = new Region(path);
            if (previous != null)
                previous.Dispose();
        }

        void PaintPill(object sender, PaintEventArgs e)
        {
            Button button = (Button)sender;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.Clear(CardPainter.Canvas);
            Rectangle bounds = new Rectangle(0, 0, button.Width - 1, button.Height - 1);
            using (GraphicsPath path = CardPainter.RoundRect(bounds, 15))
            using (Brush fill = new SolidBrush(Color.White))
            using (Pen pen = new Pen(CardPainter.IdleBorder))
            {
                e.Graphics.FillPath(fill, path);
                e.Graphics.DrawPath(pen, path);
            }
            TextRenderer.DrawText(e.Graphics, button.Text, button.Font, bounds, CardPainter.TitleColor, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }

        void PaintPin(object sender, PaintEventArgs e)
        {
            Button button = (Button)sender;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.Clear(CardPainter.Canvas);
            Rectangle bounds = new Rectangle(0, 0, button.Width - 1, button.Height - 1);
            Color fillColor = pinned ? CardPainter.TitleColor : Color.White;
            Color line = pinned ? Color.White : CardPainter.MutedColor;
            using (GraphicsPath path = CardPainter.RoundRect(bounds, 15))
            using (Brush fill = new SolidBrush(fillColor))
            using (Brush glyph = new SolidBrush(line))
            using (Pen border = new Pen(pinned ? CardPainter.TitleColor : CardPainter.IdleBorder))
            {
                e.Graphics.FillPath(fill, path);
                e.Graphics.DrawPath(border, path);
                int cx = button.Width / 2;
                e.Graphics.FillEllipse(glyph, cx - 4, 5, 8, 8);
                e.Graphics.FillPolygon(glyph, new Point[] {
                    new Point(cx - 2, 12),
                    new Point(cx + 2, 12),
                    new Point(cx, 23)
                });
            }
        }

        void PaintMenu(object sender, PaintEventArgs e)
        {
            Button button = (Button)sender;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.Clear(CardPainter.Canvas);
            Rectangle bounds = new Rectangle(0, 0, button.Width - 1, button.Height - 1);
            Color fillColor = menuOpen ? CardPainter.TitleColor : Color.White;
            Color line = menuOpen ? Color.White : CardPainter.TitleColor;
            using (GraphicsPath path = CardPainter.RoundRect(bounds, 15))
            using (Brush fill = new SolidBrush(fillColor))
            using (Pen border = new Pen(menuOpen ? CardPainter.TitleColor : CardPainter.IdleBorder))
            using (Pen glyph = new Pen(line, 1.7f))
            {
                e.Graphics.FillPath(fill, path);
                e.Graphics.DrawPath(border, path);
                glyph.StartCap = LineCap.Round;
                glyph.EndCap = LineCap.Round;
                int left = 8;
                int right = button.Width - 9;
                int mid = button.Height / 2;
                e.Graphics.DrawLine(glyph, left, mid - 4, right, mid - 4);
                e.Graphics.DrawLine(glyph, left, mid, right, mid);
                e.Graphics.DrawLine(glyph, left, mid + 4, right, mid + 4);
            }
        }

        void ToggleMenu()
        {
            if (openMenu != null && !openMenu.IsDisposed)
            {
                openMenu.Close();
                return;
            }
            if ((DateTime.UtcNow - menuClosedUtc).TotalMilliseconds < 250)
                return;
            AppMenuForm menu = new AppMenuForm();
            openMenu = menu;
            menuOpen = true;
            menuButton.Invalidate();
            menu.FormClosed += delegate
            {
                menuClosedUtc = DateTime.UtcNow;
                menuOpen = false;
                if (openMenu == menu)
                    openMenu = null;
                menuButton.Invalidate();
                BeginInvoke(new Action(delegate
                {
                    if (!menu.IsDisposed)
                        menu.Dispose();
                }));
            };
            menu.SettingsChosen += OpenSettings;
            menu.AboutChosen += OpenAbout;
            Point screen = menuButton.PointToScreen(new Point(0, menuButton.Height + 6));
            Rectangle area = Screen.FromControl(this).WorkingArea;
            if (screen.X + menu.Width > area.Right - 8)
                screen.X = area.Right - menu.Width - 8;
            if (screen.Y + menu.Height > area.Bottom - 8)
                screen.Y = menuButton.PointToScreen(Point.Empty).Y - menu.Height - 6;
            menu.Location = screen;
            menu.TopMost = TopMost;
            menu.Show(this);
        }

        void OpenSettings()
        {
            using (SettingsForm dialog = new SettingsForm(CardPainter.Scale, soundEnabled, soundVolume))
            {
                dialog.ScaleChanged += ApplyScale;
                dialog.SoundChanged += ApplySound;
                dialog.SoundPreview += PreviewSound;
                dialog.ShowDialog(this);
            }
        }

        void OpenAbout()
        {
            using (AboutForm dialog = new AboutForm())
                dialog.ShowDialog(this);
        }

        void ApplyScale(float scale)
        {
            CardPainter.SetScale(scale);
            foreach (KeyValuePair<long, ProjectCardControl> pair in views)
                pair.Value.ApplyMetrics();
            ApplyEmptyFont();
            try
            {
                UiSettings.SaveScale(settingsPath, CardPainter.Scale);
            }
            catch (Exception ex)
            {
                SetStatus(ex.Message);
            }
            FitWindow();
            UpdateScrollExtent();
            cards.PerformLayout();
        }

        void ApplySound(bool enabled, int volume)
        {
            soundEnabled = enabled;
            soundVolume = UiSettings.ClampVolume(volume);
            try
            {
                UiSettings.SaveSound(settingsPath, soundEnabled, soundVolume);
            }
            catch (Exception ex)
            {
                SetStatus(ex.Message);
            }
        }

        void PreviewSound(int volume)
        {
            if (!soundEnabled)
                return;
            Reminder.Play(volume);
        }

        void ApplyEmptyFont()
        {
            Font next = CardPainter.CreateBodyFont();
            emptyLabel.Font = next;
            if (emptyFont != null)
                emptyFont.Dispose();
            emptyFont = next;
        }

        void TogglePinned()
        {
            pinned = !pinned;
            TopMost = pinned;
            try
            {
                UiSettings.SavePinned(settingsPath, pinned);
            }
            catch (Exception ex)
            {
                SetStatus(ex.Message);
            }
            pinButton.Invalidate();
        }

        static bool IsTaskFileLock(string text)
        {
            if (string.IsNullOrEmpty(text))
                return false;
            return text.IndexOf("tasks.json", StringComparison.OrdinalIgnoreCase) >= 0
                || text.IndexOf("正由另一进程使用", StringComparison.Ordinal) >= 0
                || text.IndexOf("being used by another process", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        void SetStatus(string text)
        {
            statusLabel.Text = text ?? "";
            bool show = statusLabel.Text.Length > 0;
            statusLabel.Height = show ? 28 : 0;
        }

        void ToggleOrientation()
        {
            vertical = !vertical;
            try
            {
                UiSettings.SaveVertical(settingsPath, vertical);
            }
            catch (Exception ex)
            {
                SetStatus(ex.Message);
            }
            ApplyOrientation(true);
        }

        void ApplyOrientation(bool resize)
        {
            if (vertical)
            {
                cards.FlowDirection = FlowDirection.TopDown;
                cards.WrapContents = false;
                orientationButton.Text = "横向";
            }
            else
            {
                cards.FlowDirection = FlowDirection.LeftToRight;
                cards.WrapContents = false;
                orientationButton.Text = "竖向";
            }
            if (resize)
                FitWindow();
            orientationButton.Invalidate();
            UpdateScrollExtent();
        }

        void FitWindow()
        {
            int width;
            int height;
            if (vertical)
            {
                width = CardPainter.CardWidth + CardPainter.Px(48);
                height = 680;
            }
            else
            {
                width = CardPainter.Px(980);
                height = CardPainter.CardHeight + CardPainter.Px(92);
            }
            if (IsHandleCreated)
            {
                Rectangle area = Screen.FromControl(this).WorkingArea;
                int limitW = Math.Max(360, area.Width - 32);
                int limitH = Math.Max(200, area.Height - 32);
                if (width > limitW)
                    width = limitW;
                if (height > limitH)
                    height = limitH;
            }
            ClientSize = new Size(width, height);
        }

        void UpdateScrollExtent()
        {
            int count = 0;
            foreach (Control control in cards.Controls)
            {
                if (control is ProjectCardControl)
                    count++;
            }
            int gap = CardPainter.Px(12);
            if (vertical)
                cards.AutoScrollMinSize = new Size(0, count * (CardPainter.CardHeight + gap) + 24);
            else
                cards.AutoScrollMinSize = new Size(count * (CardPainter.CardWidth + gap) + 24, 0);
        }

        void Reload()
        {
            if (!IsHandleCreated)
            {
                IntPtr created = Handle;
                if (created == IntPtr.Zero)
                    return;
            }
            if (Interlocked.Exchange(ref reloading, 1) == 1)
                return;
            ThreadPool.QueueUserWorkItem(delegate
            {
                List<CursorWindowInfo> windows = null;
                List<TaskRecord> tasks = null;
                string error = null;
                try
                {
                    windows = CursorWindows.List();
                    TaskLoadResult loaded = TaskBoard.Load(TaskBoard.DefaultPath(), DateTimeOffset.Now);
                    if (!loaded.Ok)
                        error = loaded.Error ?? "";
                    tasks = loaded.Tasks ?? new List<TaskRecord>();
                    List<TaskRecord> history = ComposerHistory.Load(ComposerHistory.DefaultDatabase(), DateTimeOffset.Now);
                    for (int i = 0; i < history.Count; i++)
                        tasks.Add(history[i]);
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                }
                try
                {
                    BeginInvoke((MethodInvoker)delegate
                    {
                        try
                        {
                            if (!string.IsNullOrEmpty(error))
                                SetStatus(error);
                            else if (IsTaskFileLock(statusLabel.Text))
                                SetStatus("");
                            if (windows != null && tasks != null)
                                ShowCards(windows, tasks);
                        }
                        finally
                        {
                            Interlocked.Exchange(ref reloading, 0);
                        }
                    });
                }
                catch (Exception)
                {
                    Interlocked.Exchange(ref reloading, 0);
                }
            });
        }

        void ShowCards(List<CursorWindowInfo> windows, List<TaskRecord> tasks)
        {
            List<ProjectCard> next = ProjectCards.Build(windows, tasks, null);
            for (int i = 0; i < next.Count; i++)
            {
                string note;
                if (notes.TryGetValue(next[i].NoteKey, out note))
                    next[i].Note = note;
            }
            Sync(next);
        }

        void Sync(List<ProjectCard> next)
        {
            List<long> previous = new List<long>();
            foreach (Control control in cards.Controls)
            {
                ProjectCardControl view = control as ProjectCardControl;
                if (view != null && view.Card != null)
                    previous.Add(view.Card.Hwnd);
            }
            List<ProjectCard> ordered = CardOrder.Stabilize(previous, next);

            Dictionary<long, bool> seen = new Dictionary<long, bool>();
            bool clocksChanged = false;
            DateTimeOffset now = DateTimeOffset.Now;
            for (int i = 0; i < ordered.Count; i++)
            {
                ProjectCard card = ordered[i];
                if (RunClocks.Apply(card, now, runClocks))
                    clocksChanged = true;
                seen[card.Hwnd] = true;
                ProjectCardControl view;
                if (!views.TryGetValue(card.Hwnd, out view))
                {
                    view = new ProjectCardControl();
                    view.ActivateRequested += ActivateCard;
                    view.NoteChanged += SaveNote;
                    views[card.Hwnd] = view;
                    cards.Controls.Add(view);
                }
                view.Card = card;
                view.Phase = phase;
                cards.Controls.SetChildIndex(view, i);
                view.Invalidate();
            }

            List<long> remove = new List<long>();
            foreach (KeyValuePair<long, ProjectCardControl> pair in views)
            {
                if (!seen.ContainsKey(pair.Key))
                    remove.Add(pair.Key);
            }
            for (int i = 0; i < remove.Count; i++)
            {
                ProjectCardControl view = views[remove[i]];
                views.Remove(remove[i]);
                cards.Controls.Remove(view);
                view.Dispose();
            }

            emptyLabel.Visible = ordered.Count == 0;
            if (ordered.Count == 0 && !cards.Controls.Contains(emptyLabel))
                cards.Controls.Add(emptyLabel);
            if (ordered.Count > 0 && cards.Controls.Contains(emptyLabel))
                cards.Controls.Remove(emptyLabel);
            if (clocksChanged)
            {
                try
                {
                    RunClocks.Save(runsPath, runClocks);
                }
                catch (Exception ex)
                {
                    SetStatus(ex.Message);
                }
            }
            NoticeChanges(ordered);
            UpdateScrollExtent();
        }

        void NoticeChanges(List<ProjectCard> ordered)
        {
            bool alert = false;
            for (int i = 0; i < ordered.Count; i++)
            {
                ProjectCard card = ordered[i];
                string status = card.Status ?? "";
                string previous;
                bool known = seenStatus.TryGetValue(card.Hwnd, out previous);
                seenStatus[card.Hwnd] = status;
                if (!alertsArmed || !known || previous == status)
                    continue;
                if (status == "completed" || status == "error" || status == "confirm")
                    alert = true;
            }
            alertsArmed = true;
            if (!alert)
                return;
            if (IsHandleCreated)
                Reminder.Flash(Handle);
            if (soundEnabled)
                Reminder.Play(soundVolume);
        }

        void SaveNote(string key, string text)
        {
            if (string.IsNullOrEmpty(key))
                return;
            if (string.IsNullOrEmpty(text))
                notes.Remove(key);
            else
                notes[key] = text;
            try
            {
                CardNotes.Save(notesPath, notes);
            }
            catch (Exception ex)
            {
                SetStatus(ex.Message);
            }
        }

        void AdvanceShimmer()
        {
            phase = (unchecked((uint)Environment.TickCount) % 1900u) / 1900f;
            foreach (KeyValuePair<long, ProjectCardControl> pair in views)
            {
                ProjectCardControl view = pair.Value;
                if (view.Card == null || view.Card.Status != "running" || view.Width < 2 || view.Height < 2)
                    continue;
                view.Phase = phase;
                Rectangle bar = CardPainter.ProgressBar(new Rectangle(0, 0, view.Width, view.Height));
                bar.Inflate(3, 4);
                view.Invalidate(bar);
            }
        }

        void ActivateCard(long hwnd)
        {
            ActivateResult result = WindowActivator.Activate(hwnd);
            SetStatus(result.IsForeground ? "" : "没有切到该窗口");
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            FitWindow();
            PlaceHeaderButtons((Panel)orientationButton.Parent);
        }
    }

    public class ProjectCardControl : Control
    {
        ProjectCard card;
        readonly TextBox noteBox = new TextBox();
        public float Phase;
        Point mouseDown;
        public event Action<long> ActivateRequested;
        public event Action<string, string> NoteChanged;
        Font noteFont;

        public ProjectCard Card
        {
            get { return card; }
            set
            {
                card = value;
                if (card != null && !noteBox.Focused && noteBox.Text != (card.Note ?? ""))
                    noteBox.Text = card.Note ?? "";
                PositionNote();
                Invalidate();
            }
        }

        public ProjectCardControl()
        {
            BackColor = CardPainter.Canvas;
            Cursor = Cursors.Hand;
            DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.StandardClick | ControlStyles.StandardDoubleClick, true);
            noteBox.BorderStyle = BorderStyle.None;
            noteBox.BackColor = CardPainter.ChipFill;
            noteBox.ForeColor = CardPainter.MutedColor;
            noteBox.MaxLength = 40;
            noteBox.Visible = false;
            noteBox.Cursor = Cursors.IBeam;
            noteBox.Leave += delegate { EndEdit(); };
            noteBox.KeyDown += NoteKeyDown;
            Controls.Add(noteBox);
            ApplyMetrics();
        }

        public void ApplyMetrics()
        {
            Width = CardPainter.CardWidth;
            Height = CardPainter.CardHeight;
            Margin = new Padding(CardPainter.Px(6));
            Font next = CardPainter.CreateBodyFont();
            noteBox.Font = next;
            if (noteFont != null)
                noteFont.Dispose();
            noteFont = next;
            PositionNote();
            Invalidate();
        }

        bool editing;

        void NoteKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode != Keys.Enter && e.KeyCode != Keys.Escape)
                return;
            e.SuppressKeyPress = true;
            if (Parent != null)
                Parent.Focus();
            else
                EndEdit();
        }

        void BeginEdit()
        {
            if (card == null)
                return;
            editing = true;
            noteBox.Text = card.Note ?? "";
            noteBox.Visible = true;
            PositionNote();
            noteBox.Focus();
            noteBox.SelectionStart = noteBox.Text.Length;
            Invalidate();
        }

        bool ending;

        void EndEdit()
        {
            if (ending)
                return;
            ending = true;
            editing = false;
            CommitNote();
            noteBox.Visible = false;
            Invalidate();
            ending = false;
        }

        void CommitNote()
        {
            if (card == null || NoteChanged == null)
                return;
            string text = noteBox.Text == null ? "" : noteBox.Text.Trim();
            if (text == (card.Note ?? ""))
                return;
            card.Note = text;
            NoteChanged(card.NoteKey, text);
        }

        void PositionNote()
        {
            string project = card == null ? "" : card.Project;
            string noteText = card == null ? "" : card.Note;
            bool reserveElapsed = card != null && card.StartedAt != DateTimeOffset.MinValue;
            Rectangle note = CardPainter.NoteBounds(new Rectangle(0, 0, Width, Height), project, noteText, true, reserveElapsed);
            int padX = CardPainter.Px(8);
            int padY = CardPainter.Px(3);
            noteBox.SetBounds(note.X + padX, note.Y + padY, Math.Max(CardPainter.Px(24), note.Width - padX * 2), Math.Max(CardPainter.Px(16), note.Height - CardPainter.Px(6)));
        }

        bool NoteHit(Point point)
        {
            if (card == null)
                return false;
            Rectangle note = CardPainter.NoteBounds(new Rectangle(0, 0, Width, Height), card.Project, card.Note, editing);
            return note.Contains(point);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            mouseDown = e.Location;
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left || editing || !NoteHit(e.Location))
                return;
            BeginEdit();
        }

        protected override void OnDoubleClick(EventArgs e)
        {
            base.OnDoubleClick(e);
            if (NoteHit(mouseDown))
                BeginEdit();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (card == null)
                return;
            CardPainter.Paint(e.Graphics, new Rectangle(0, 0, Width, Height), card, Phase, editing);
        }

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            if (NoteHit(mouseDown))
                return;
            if (card != null && ActivateRequested != null)
                ActivateRequested(card.Hwnd);
        }
    }
}
