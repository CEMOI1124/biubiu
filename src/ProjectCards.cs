using System;
using System.Collections.Generic;

namespace Biubiu
{
    public sealed class ProjectCard
    {
        public long Hwnd;
        public string Project;
        public string WorkspacePath;
        public string Alias;
        public string Summary;
        public string Status;
        public string NoteKey;
        public string Note;
        public string ConversationId;
        public DateTimeOffset StartedAt;
        public DateTimeOffset EndedAt;
    }

    public static class ProjectCards
    {
        public static List<ProjectCard> Build(IList<CursorWindowInfo> windows, IList<TaskRecord> tasks, Func<string, string> readBranch)
        {
            List<ProjectCard> cards = new List<ProjectCard>();
            if (windows == null)
                return cards;

            for (int i = 0; i < windows.Count; i++)
            {
                CursorWindowInfo window = windows[i];
                if (window == null)
                    continue;
                ProjectCard card = new ProjectCard();
                card.Hwnd = window.Hwnd;
                card.Project = window.Project ?? "";
                card.WorkspacePath = TaskBoard.NormalizeWorkspacePath(window.WorkspacePath ?? "");
                card.Status = "idle";
                card.Summary = "";
                card.Alias = "";
                card.Note = "";
                card.NoteKey = CardNotes.Key(card.WorkspacePath, card.Project);
                if (card.WorkspacePath.Length > 0 && readBranch != null)
                {
                    string branch = readBranch(card.WorkspacePath);
                    if (!string.IsNullOrEmpty(branch))
                        card.Alias = branch;
                }

                TaskRecord task = MatchTask(window, windows, tasks);
                if (task != null)
                {
                    card.Summary = task.Summary ?? "";
                    card.Status = string.IsNullOrEmpty(task.Status) ? "idle" : task.Status;
                    card.ConversationId = task.ConversationId ?? "";
                    card.StartedAt = FindStart(task, tasks);
                    if (!IsActive(card.Status) && card.StartedAt != DateTimeOffset.MinValue)
                        card.EndedAt = FindEnd(task, tasks, card.StartedAt);
                }
                cards.Add(card);
            }
            return cards;
        }

        static TaskRecord MatchTask(CursorWindowInfo window, IList<CursorWindowInfo> windows, IList<TaskRecord> tasks)
        {
            if (tasks == null || window == null)
                return null;

            string path = TaskBoard.NormalizeWorkspacePath(window.WorkspacePath ?? "");
            if (path.Length > 0)
                return Choose(TasksForPath(tasks, path));

            if (CountProject(windows, window.Project) != 1)
                return null;
            return Choose(TasksForFolder(tasks, window.Project));
        }

        static List<TaskRecord> TasksForPath(IList<TaskRecord> tasks, string path)
        {
            List<TaskRecord> matches = new List<TaskRecord>();
            for (int i = 0; i < tasks.Count; i++)
            {
                TaskRecord task = tasks[i];
                if (task == null)
                    continue;
                if (string.Equals(TaskBoard.NormalizeWorkspacePath(task.WorkspacePath), path, StringComparison.OrdinalIgnoreCase))
                    matches.Add(task);
            }
            return matches;
        }

        static List<TaskRecord> TasksForFolder(IList<TaskRecord> tasks, string project)
        {
            List<TaskRecord> matches = new List<TaskRecord>();
            if (string.IsNullOrEmpty(project))
                return matches;
            for (int i = 0; i < tasks.Count; i++)
            {
                TaskRecord task = tasks[i];
                if (task == null)
                    continue;
                string folder = WorkspaceCatalog.FolderName(TaskBoard.NormalizeWorkspacePath(task.WorkspacePath));
                if (string.Equals(folder, project, StringComparison.OrdinalIgnoreCase))
                    matches.Add(task);
            }
            string onlyPath = null;
            for (int i = 0; i < matches.Count; i++)
            {
                string path = TaskBoard.NormalizeWorkspacePath(matches[i].WorkspacePath);
                if (onlyPath == null)
                    onlyPath = path;
                else if (!string.Equals(onlyPath, path, StringComparison.OrdinalIgnoreCase))
                    return new List<TaskRecord>();
            }
            return matches;
        }

        static int CountProject(IList<CursorWindowInfo> windows, string project)
        {
            int count = 0;
            for (int i = 0; i < windows.Count; i++)
            {
                if (windows[i] != null && string.Equals(windows[i].Project, project, StringComparison.OrdinalIgnoreCase))
                    count++;
            }
            return count;
        }

        static bool IsActive(string status)
        {
            return status == "running" || status == "confirm";
        }

        static DateTimeOffset FindStart(TaskRecord chosen, IList<TaskRecord> tasks)
        {
            if (chosen != null && chosen.StartedAt != DateTimeOffset.MinValue)
                return chosen.StartedAt;
            DateTimeOffset found = DateTimeOffset.MinValue;
            if (chosen == null || tasks == null || string.IsNullOrEmpty(chosen.ConversationId))
                return found;
            for (int i = 0; i < tasks.Count; i++)
            {
                TaskRecord task = tasks[i];
                if (task == null || task.StartedAt == DateTimeOffset.MinValue)
                    continue;
                if (!string.Equals(task.ConversationId, chosen.ConversationId, StringComparison.Ordinal))
                    continue;
                if (found == DateTimeOffset.MinValue || task.StartedAt > found)
                    found = task.StartedAt;
            }
            return found;
        }

        static DateTimeOffset FindEnd(TaskRecord chosen, IList<TaskRecord> tasks, DateTimeOffset start)
        {
            DateTimeOffset end = DateTimeOffset.MinValue;
            if (tasks != null && chosen != null && !string.IsNullOrEmpty(chosen.ConversationId))
            {
                for (int i = 0; i < tasks.Count; i++)
                {
                    TaskRecord task = tasks[i];
                    if (task == null || IsActive(task.Status) || task.StartedAt != start)
                        continue;
                    if (!string.Equals(task.ConversationId, chosen.ConversationId, StringComparison.Ordinal))
                        continue;
                    if (task.UpdatedAt >= start && task.UpdatedAt > end)
                        end = task.UpdatedAt;
                }
            }
            if (end == DateTimeOffset.MinValue && chosen != null && chosen.UpdatedAt >= start)
                end = chosen.UpdatedAt;
            return end;
        }

        static TaskRecord Choose(List<TaskRecord> matches)
        {
            TaskRecord newest = null;
            TaskRecord newestRunning = null;
            TaskRecord newestConfirm = null;
            TaskRecord newestFinished = null;
            for (int i = 0; i < matches.Count; i++)
            {
                TaskRecord task = matches[i];
                if (newest == null || task.UpdatedAt > newest.UpdatedAt)
                    newest = task;
                if (task.Status == "running" && (newestRunning == null || task.UpdatedAt > newestRunning.UpdatedAt))
                    newestRunning = task;
                if (task.Status == "confirm" && (newestConfirm == null || task.UpdatedAt > newestConfirm.UpdatedAt))
                    newestConfirm = task;
                if (IsFinished(task.Status) && (newestFinished == null || task.UpdatedAt > newestFinished.UpdatedAt))
                    newestFinished = task;
            }
            bool confirmCurrent = newestConfirm != null
                && (newestFinished == null || !SameConversation(newestConfirm, newestFinished) || newestConfirm.UpdatedAt >= newestFinished.UpdatedAt);
            if (confirmCurrent)
            {
                if (newestRunning == null || SameConversation(newestConfirm, newestRunning) || newestConfirm.UpdatedAt >= newestRunning.UpdatedAt)
                    return newestConfirm;
            }
            if (newestRunning != null)
            {
                if (newestFinished != null && SameConversation(newestFinished, newestRunning) && newestFinished.UpdatedAt >= newestRunning.UpdatedAt)
                    return newestFinished;
                return newestRunning;
            }
            return newest;
        }

        static bool IsFinished(string status)
        {
            return status == "completed" || status == "aborted" || status == "error";
        }

        static bool SameConversation(TaskRecord left, TaskRecord right)
        {
            return left != null
                && right != null
                && !string.IsNullOrEmpty(left.ConversationId)
                && string.Equals(left.ConversationId, right.ConversationId, StringComparison.Ordinal);
        }
    }
}
