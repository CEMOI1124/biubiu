using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using Biubiu;

static class CardTests
{
    static int failures;

    public static int Run()
    {
        failures = 0;
        TestBranch();
        TestSettings();
        TestCards();
        TestLatestHistory();
        TestComposer();
        TestOrder();
        TestNotes();
        TestPainter();
        TestScale();
        return failures;
    }

    static void TestBranch()
    {
        if (GitBranch.NameFromHead("ref: refs/heads/feature/login\n") != "feature/login")
            Fail("branch name was not read from HEAD");
        if (GitBranch.NameFromHead("0123456789abcdef") != "0123456")
            Fail("detached HEAD was not shortened");

        string root = Path.Combine(Path.GetTempPath(), "biubiu-git-" + Guid.NewGuid().ToString("N"));
        string worktree = Path.Combine(Path.GetTempPath(), "biubiu-wt-" + Guid.NewGuid().ToString("N"));
        try
        {
            string gitDir = Path.Combine(root, ".git");
            Directory.CreateDirectory(gitDir);
            File.WriteAllText(Path.Combine(gitDir, "HEAD"), "ref: refs/heads/main\n");
            if (GitBranch.Read(root) != "main")
                Fail("branch was not read from .git directory");

            Directory.CreateDirectory(worktree);
            File.WriteAllText(Path.Combine(worktree, ".git"), "gitdir: " + gitDir + "\n");
            if (GitBranch.Read(worktree) != "main")
                Fail("branch was not read from a worktree gitdir");
            if (GitBranch.Read(Path.Combine(root, "missing")) != null)
                Fail("missing workspace returned a branch");
        }
        finally
        {
            Delete(root);
            Delete(worktree);
        }
    }

    static void TestSettings()
    {
        string path = Path.Combine(Path.GetTempPath(), "biubiu-ui-" + Guid.NewGuid().ToString("N") + ".txt");
        try
        {
            if (UiSettings.LoadVertical(path))
                Fail("missing layout defaulted to vertical");
            if (Math.Abs(UiSettings.LoadScale(path) - 1f) > 0.001f)
                Fail("missing scale defaulted away from 1");
            UiSettings.SaveVertical(path, true);
            if (!UiSettings.LoadVertical(path))
                Fail("vertical layout was not saved");
            UiSettings.SaveVertical(path, false);
            if (UiSettings.LoadVertical(path))
                Fail("horizontal layout was saved as vertical");
            UiSettings.SavePinned(path, true);
            if (!UiSettings.LoadPinned(path) || UiSettings.LoadVertical(path))
                Fail("the pin was not saved on its own");
            UiSettings.SaveVertical(path, true);
            if (!UiSettings.LoadPinned(path) || !UiSettings.LoadVertical(path))
                Fail("saving the layout cleared the pin");
            Rectangle card = new Rectangle(0, 0, CardPainter.CardWidth, CardPainter.CardHeight);
            Rectangle title = CardPainter.TitleBounds(card, "Biubiu");
            Rectangle note = CardPainter.NoteBounds(card, "Biubiu", "", false);
            if (note.Left < title.Right || note.Left > title.Right + 10)
                Fail("the note is not next to the title");
            UiSettings.SaveScale(path, 1.25f);
            if (Math.Abs(UiSettings.LoadScale(path) - 1.25f) > 0.001f)
                Fail("scale was not saved");
            if (!UiSettings.LoadPinned(path) || !UiSettings.LoadVertical(path))
                Fail("saving the scale cleared the layout");
            UiSettings.SaveVertical(path, false);
            if (Math.Abs(UiSettings.LoadScale(path) - 1.25f) > 0.001f || !UiSettings.LoadPinned(path))
                Fail("saving the layout cleared the scale");
            UiSettings.SaveScale(path, 3f);
            if (UiSettings.LoadScale(path) > 1.6f || UiSettings.LoadScale(path) < 1.6f)
                Fail("scale was not limited");
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    static void TestCards()
    {
        CursorWindowInfo biubiu = Window(11, "Biubiu", "D:/work/Biubiu");
        CursorWindowInfo cemoi = Window(22, "cemoi", "");
        CursorWindowInfo cemoiAgain = Window(23, "cemoi", "");
        List<CursorWindowInfo> windows = new List<CursorWindowInfo>();
        windows.Add(biubiu);
        windows.Add(cemoi);

        TaskRecord running = Task("c1", "D:\\work\\Biubiu", "正在改登录", "running", "2026-10-23T13:00:00+08:00");
        TaskRecord done = Task("c2", "D:\\work\\Biubiu", "登录已完成", "completed", "2026-10-23T12:00:00+08:00");
        TaskRecord cemoiTask = Task("c3", "D:\\work\\cemoi", "生成表", "completed", "2026-10-23T12:30:00+08:00");
        List<TaskRecord> tasks = new List<TaskRecord>();
        tasks.Add(done);
        tasks.Add(running);
        tasks.Add(cemoiTask);

        List<ProjectCard> cards = ProjectCards.Build(windows, tasks, delegate(string path)
        {
            if (path.EndsWith("Biubiu", StringComparison.OrdinalIgnoreCase))
                return "feature/login";
            return null;
        });
        if (cards.Count != 2)
        {
            Fail("card count " + cards.Count);
            return;
        }
        if (cards[0].Alias != "feature/login" || cards[0].Status != "running" || cards[0].Summary != "正在改登录")
            Fail("running card did not prefer the active task");
        if (cards[1].Status != "completed" || cards[1].Summary != "生成表" || cards[1].Alias != "")
            Fail("unique folder name did not match the task");

        windows.Add(cemoiAgain);
        cards = ProjectCards.Build(windows, tasks, null);
        if (cards[1].Status != "idle" || cards[2].Status != "idle")
            Fail("duplicate project names were given a task");
    }

    static void TestLatestHistory()
    {
        CursorWindowInfo window = Window(1, "cemoi", "");
        List<CursorWindowInfo> windows = new List<CursorWindowInfo>();
        windows.Add(window);
        List<TaskRecord> tasks = new List<TaskRecord>();
        tasks.Add(Task("old", "D:\\work\\cemoi", "\u65e7\u4efb\u52a1", "completed", "2026-10-23T10:00:00+08:00"));
        tasks.Add(Task("new", "D:\\work\\cemoi", "\u65b0\u4efb\u52a1", "completed", "2026-10-23T11:00:00+08:00"));
        List<ProjectCard> cards = ProjectCards.Build(windows, tasks, null);
        if (cards.Count != 1 || cards[0].Status != "completed" || cards[0].Summary != "\u65b0\u4efb\u52a1")
            Fail("the latest finished task was not shown");
        tasks.Add(Task("other", "D:\\other\\cemoi", "\u53e6\u4e00\u5904", "completed", "2026-10-23T12:00:00+08:00"));
        cards = ProjectCards.Build(windows, tasks, null);
        if (cards[0].Status != "idle")
            Fail("two folders with the same name were merged");
    }

    static void TestComposer()
    {
        DateTimeOffset now = DateTimeOffset.Parse("2026-10-23T22:00:00+08:00");
        string doneJson = "{\"composerId\":\"c1\",\"name\":\"\\u767b\\u5f55\\u5df2\\u5b8c\\u6210\",\"unifiedMode\":\"agent\",\"isDraft\":false,\"workspaceIdentifier\":{\"uri\":{\"fsPath\":\"D:\\\\work\\\\Biubiu\"}}}";
        TaskRecord done = ComposerHistory.FromHeader(doneJson, now.ToUnixTimeMilliseconds(), now);
        if (done == null || done.Status != "completed" || done.Summary != "\u767b\u5f55\u5df2\u5b8c\u6210")
            Fail("a finished composer was not completed");
        if (done.WorkspacePath.IndexOf("Biubiu", StringComparison.OrdinalIgnoreCase) < 0)
            Fail("composer workspace path was lost");

        string runningJson = "{\"composerId\":\"c2\",\"name\":\"still going\",\"unifiedMode\":\"agent\",\"unfinishedRunAt\":1,\"workspaceIdentifier\":{\"uri\":{\"fsPath\":\"D:\\\\work\\\\Biubiu\"}}}";
        TaskRecord running = ComposerHistory.FromHeader(runningJson, now.ToUnixTimeMilliseconds(), now);
        if (running == null || running.Status != "running")
            Fail("an unfinished composer was not running");
        TaskRecord older = ComposerHistory.FromHeader(runningJson, now.AddMinutes(-11).ToUnixTimeMilliseconds(), now);
        if (older == null || older.Status != "running")
            Fail("a long unfinished composer was hidden");
        if (ComposerHistory.FromHeader("{\"name\":\"chat\",\"unifiedMode\":\"chat\",\"workspaceIdentifier\":{\"uri\":{\"fsPath\":\"D:\\\\work\\\\Biubiu\"}}}", 1, now) != null)
            Fail("a chat was treated as a task");
        if (ComposerHistory.ResolveStatus("aborted", true) != "running")
            Fail("an open conversation was marked interrupted");
        if (ComposerHistory.ResolveStatus("aborted", false) != "aborted")
            Fail("an aborted conversation stayed green");
        if (ComposerHistory.ResolveStatus("error", false) != "error")
            Fail("a failed conversation was not marked failed");
        if (ComposerHistory.ResolveStatus("generating", true, true) != "confirm")
            Fail("a confirmation request stayed on the progress bar");
        string waitingJson = "{\"composerId\":\"c3\",\"name\":\"wait\",\"unifiedMode\":\"agent\",\"unfinishedRunAt\":1,\"hasBlockingPendingActions\":true,\"workspaceIdentifier\":{\"uri\":{\"fsPath\":\"D:\\\\work\\\\Biubiu\"}}}";
        TaskRecord waiting = ComposerHistory.FromHeader(waitingJson, now.ToUnixTimeMilliseconds(), now);
        if (waiting == null || waiting.Status != "confirm")
            Fail("a blocking confirmation was not marked");
        if (ComposerHistory.SummarizeUserMessage("\u7b2c\u4e00\u884c\n\u7b2c\u4e8c\u884c") != "\u7b2c\u4e00\u884c")
            Fail("the user message kept later lines");

        string missing = Path.Combine(Path.GetTempPath(), "biubiu-missing-" + Guid.NewGuid().ToString("N") + ".vscdb");
        if (ComposerHistory.Load(missing, now).Count != 0)
            Fail("a missing composer database returned tasks");
    }

    static void TestOrder()
    {
        ProjectCard first = new ProjectCard();
        first.Hwnd = 1;
        first.Project = "a";
        ProjectCard second = new ProjectCard();
        second.Hwnd = 2;
        second.Project = "b";
        ProjectCard third = new ProjectCard();
        third.Hwnd = 3;
        third.Project = "c";
        List<ProjectCard> incoming = new List<ProjectCard>();
        incoming.Add(third);
        incoming.Add(first);
        incoming.Add(second);
        List<long> previous = new List<long>();
        previous.Add(1);
        previous.Add(2);
        previous.Add(3);
        List<ProjectCard> stable = CardOrder.Stabilize(previous, incoming);
        if (stable.Count != 3 || stable[0].Hwnd != 1 || stable[1].Hwnd != 2 || stable[2].Hwnd != 3)
            Fail("clicking a card changed the existing order");

        incoming.Clear();
        incoming.Add(second);
        ProjectCard added = new ProjectCard();
        added.Hwnd = 9;
        added.Project = "new";
        incoming.Add(added);
        incoming.Add(first);
        previous.Clear();
        previous.Add(1);
        previous.Add(2);
        stable = CardOrder.Stabilize(previous, incoming);
        if (stable.Count != 3 || stable[0].Hwnd != 1 || stable[1].Hwnd != 2 || stable[2].Hwnd != 9)
            Fail("a new card was not kept at the end");
    }

    static void TestNotes()
    {
        if (CardNotes.Key("D:/Work/Biubiu", "Biubiu") != CardNotes.Key("d:\\work\\biubiu", "other"))
            Fail("the same workspace did not share a note key");
        if (CardNotes.Key("", "Cemoi") != "name:cemoi")
            Fail("a window without a path did not use its project name");

        string path = Path.Combine(Path.GetTempPath(), "biubiu-notes-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            Dictionary<string, string> notes = new Dictionary<string, string>();
            notes["name:cemoi"] = "\u767b\u5f55\u5907\u6ce8";
            notes["path:d:\\work\\biubiu"] = "   ";
            CardNotes.Save(path, notes);
            Dictionary<string, string> loaded = CardNotes.Load(path);
            string note;
            if (!loaded.TryGetValue("name:cemoi", out note) || note != "\u767b\u5f55\u5907\u6ce8")
                Fail("the note was not saved");
            if (loaded.ContainsKey("path:d:\\work\\biubiu"))
                Fail("a blank note was saved");
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    static void TestPainter()
    {
        ProjectCard running = new ProjectCard();
        running.Project = "Biubiu";
        running.Alias = "feature/login";
        running.Summary = "正在改登录";
        running.Status = "running";
        ProjectCard completed = new ProjectCard();
        completed.Project = "cemoi";
        completed.Alias = "main";
        completed.Summary = "登录已完成";
        completed.Status = "completed";

        using (Bitmap start = Draw(running, 0.05f))
        using (Bitmap end = Draw(running, 0.95f))
        using (Bitmap done = Draw(completed, 0f))
        {
            Rectangle bar = CardPainter.ProgressBar(new Rectangle(0, 0, CardPainter.CardWidth, CardPainter.CardHeight));
            Color leftStart = start.GetPixel(bar.Left + 8, bar.Top + 2);
            Color rightStart = start.GetPixel(bar.Right - 8, bar.Top + 2);
            Color leftEnd = end.GetPixel(bar.Left + 8, bar.Top + 2);
            Color rightEnd = end.GetPixel(bar.Right - 8, bar.Top + 2);
            if (leftStart.ToArgb() != CardPainter.RunningHighlight.ToArgb() || rightStart.ToArgb() == CardPainter.RunningHighlight.ToArgb())
                Fail("sweep did not start on the left: " + leftStart + " / " + rightStart);
            if (rightEnd.ToArgb() != CardPainter.RunningHighlight.ToArgb() || leftEnd.ToArgb() == CardPainter.RunningHighlight.ToArgb())
                Fail("sweep did not reach the right: " + leftEnd + " / " + rightEnd);
            if (start.GetPixel(0, 0).ToArgb() != CardPainter.Canvas.ToArgb())
                Fail("card corners are still square");

            Rectangle mark = CardPainter.CheckBounds(new Rectangle(0, 0, CardPainter.CardWidth, CardPainter.CardHeight));
            Color markColor = done.GetPixel(mark.Left + mark.Width / 2, mark.Top + 2);
            if (markColor.G < markColor.R || markColor.G < 80)
                Fail("completed card has no check mark");
            if (done.GetPixel(0, 0).ToArgb() != CardPainter.Canvas.ToArgb())
                Fail("completed card corners are still square");
            if (done.GetPixel(bar.Left + 8, bar.Top + 2).ToArgb() == CardPainter.RunningHighlight.ToArgb())
                Fail("completed card kept the running sweep");

            ProjectCard waiting = new ProjectCard();
            waiting.Project = "Biubiu";
            waiting.Status = "confirm";
            using (Bitmap confirm = Draw(waiting, 0f))
            {
                if (confirm.GetPixel(CardPainter.CardWidth / 2, CardPainter.CardHeight / 2).ToArgb() != CardPainter.WarningFill.ToArgb())
                    Fail("a confirmation card was not yellow");
            }

            string dir = AppDomain.CurrentDomain.BaseDirectory;
            start.Save(Path.Combine(dir, "preview-running.png"), ImageFormat.Png);
            done.Save(Path.Combine(dir, "preview-completed.png"), ImageFormat.Png);
        }
    }

    static void TestScale()
    {
        CardPainter.SetScale(1f);
        Rectangle baseCard = new Rectangle(0, 0, CardPainter.CardWidth, CardPainter.CardHeight);
        Rectangle baseNote = CardPainter.NoteBounds(baseCard, "Biubiu", "备注", false);
        CardPainter.SetScale(1.5f);
        try
        {
            if (CardPainter.CardWidth < 470 || CardPainter.CardHeight < 220)
                Fail("the card did not grow with the text size");
            Rectangle big = new Rectangle(0, 0, CardPainter.CardWidth, CardPainter.CardHeight);
            Rectangle title = CardPainter.TitleBounds(big, "Biubiu");
            Rectangle note = CardPainter.NoteBounds(big, "Biubiu", "备注", false);
            if (note.Height < baseNote.Height + 8 || note.Width <= baseNote.Width)
                Fail("the note did not scale with the text size");
            if (title.Height != note.Height || note.Top != title.Top || note.Left < title.Right)
                Fail("the scaled note left the title");
            if (CardPainter.Px(56) <= note.Bottom)
                Fail("the content overlaps the title row");
            CardPainter.SetScale(3f);
            if (CardPainter.Scale > 1.6f)
                Fail("the text size was not limited");
        }
        finally
        {
            CardPainter.SetScale(1f);
        }
    }

    static Bitmap Draw(ProjectCard card, float phase)
    {
        Bitmap bitmap = new Bitmap(CardPainter.CardWidth, CardPainter.CardHeight);
        using (Graphics graphics = Graphics.FromImage(bitmap))
            CardPainter.Paint(graphics, new Rectangle(0, 0, bitmap.Width, bitmap.Height), card, phase);
        return bitmap;
    }

    static CursorWindowInfo Window(long hwnd, string project, string path)
    {
        CursorWindowInfo info = new CursorWindowInfo();
        info.Hwnd = hwnd;
        info.Project = project;
        info.WorkspacePath = path;
        return info;
    }

    static TaskRecord Task(string id, string path, string summary, string status, string updated)
    {
        TaskRecord task = new TaskRecord();
        task.ConversationId = id;
        task.WorkspacePath = path;
        task.Summary = summary;
        task.Status = status;
        task.UpdatedAt = DateTimeOffset.Parse(updated);
        return task;
    }

    static void Delete(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, true);
        }
        catch (IOException)
        {
        }
    }

    static void Fail(string message)
    {
        failures++;
        Console.Error.WriteLine(message);
    }
}
