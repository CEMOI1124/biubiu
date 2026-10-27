using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace Biubiu
{
    public sealed class TaskRecord
    {
        public string ConversationId;
        public string WorkspacePath;
        public string Summary;
        public string Status;
        public DateTimeOffset StartedAt;
        public DateTimeOffset UpdatedAt;
    }

    public sealed class TaskLoadResult
    {
        public bool Ok;
        public string Error;
        public List<TaskRecord> Tasks;
    }

    public static class TaskBoard
    {
        public static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(10);
        static readonly Dictionary<string, List<TaskRecord>> remembered = new Dictionary<string, List<TaskRecord>>(StringComparer.OrdinalIgnoreCase);

        public static string DefaultPath()
        {
            string overridePath = Environment.GetEnvironmentVariable("BIUBIU_TASKS_PATH");
            if (!string.IsNullOrEmpty(overridePath))
                return overridePath;
            string root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(root, "Biubiu", "tasks");
        }

        public static string NormalizeWorkspacePath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return "";
            string text = path.Trim();
            if (text.Length >= 4 && text[0] == '/' && char.IsLetter(text[1]) && text[2] == ':' && (text[3] == '/' || text[3] == '\\'))
                text = text.Substring(1, 1) + ":\\" + text.Substring(4);
            text = text.Replace('/', '\\');
            if (text.Length > 3)
                text = text.TrimEnd('\\');
            return text;
        }

        public static TaskLoadResult Load(string path, DateTimeOffset now)
        {
            TaskLoadResult result = new TaskLoadResult();
            result.Ok = true;
            result.Tasks = new List<TaskRecord>();
            if (string.IsNullOrEmpty(path))
                return result;
            if (Directory.Exists(path))
                return LoadDirectory(path, now);
            if (!File.Exists(path))
                return result;
            string error = LoadDocument(path, now, result, null);
            if (error != null)
            {
                result.Ok = false;
                result.Error = error;
                result.Tasks.Clear();
            }
            return result;
        }

        static TaskLoadResult LoadDirectory(string path, DateTimeOffset now)
        {
            TaskLoadResult result = new TaskLoadResult();
            result.Ok = true;
            result.Tasks = new List<TaskRecord>();
            Dictionary<string, bool> seen = new Dictionary<string, bool>(StringComparer.Ordinal);
            string[] files;
            try
            {
                files = Directory.GetFiles(path, "*.json");
            }
            catch (IOException ex)
            {
                result.Ok = false;
                result.Error = ex.Message;
                return result;
            }
            catch (UnauthorizedAccessException ex)
            {
                result.Ok = false;
                result.Error = ex.Message;
                return result;
            }

            for (int i = 0; i < files.Length; i++)
            {
                if (files[i].EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
                    continue;
                string error = LoadDocument(files[i], now, result, seen);
                if (error == null)
                    continue;
                List<TaskRecord> previous;
                if (!remembered.TryGetValue(files[i], out previous))
                    continue;
                for (int n = 0; n < previous.Count; n++)
                {
                    result.Tasks.Add(previous[n]);
                    if (!string.IsNullOrEmpty(previous[n].ConversationId))
                        seen[previous[n].ConversationId] = true;
                }
            }

            DirectoryInfo parent = Directory.GetParent(path);
            if (parent != null)
                LoadDocument(Path.Combine(parent.FullName, "tasks.json"), now, result, seen);
            return result;
        }

        static string LoadDocument(string path, DateTimeOffset now, TaskLoadResult result, Dictionary<string, bool> seen)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return null;
            string text;
            try
            {
                text = ReadShared(path);
            }
            catch (IOException ex)
            {
                return ex.Message;
            }
            catch (UnauthorizedAccessException ex)
            {
                return ex.Message;
            }
            if (string.IsNullOrWhiteSpace(text))
                return null;

            object root;
            try
            {
                JavaScriptSerializer serializer = new JavaScriptSerializer();
                serializer.MaxJsonLength = 2000000;
                root = serializer.DeserializeObject(text);
            }
            catch (Exception)
            {
                return "tasks file is invalid";
            }

            Dictionary<string, object> document = root as Dictionary<string, object>;
            if (document == null)
                return "tasks file is invalid";

            object tasksValue;
            if (document.TryGetValue("tasks", out tasksValue) && tasksValue != null)
            {
                IList tasks = tasksValue as IList;
                if (tasks == null)
                    return "tasks file is invalid";
                List<TaskRecord> stored = new List<TaskRecord>();
                for (int i = 0; i < tasks.Count; i++)
                    AddRecord(tasks[i] as Dictionary<string, object>, now, result, seen, stored);
                remembered[path] = stored;
                return null;
            }

            if (document.ContainsKey("conversationId"))
            {
                List<TaskRecord> stored = new List<TaskRecord>();
                AddRecord(document, now, result, seen, stored);
                remembered[path] = stored;
                return null;
            }
            return null;
        }

        static void AddRecord(Dictionary<string, object> item, DateTimeOffset now, TaskLoadResult result, Dictionary<string, bool> seen, List<TaskRecord> stored)
        {
            if (item == null)
                return;
            TaskRecord record = new TaskRecord();
            record.ConversationId = ReadString(item, "conversationId");
            if (seen != null && !string.IsNullOrEmpty(record.ConversationId) && seen.ContainsKey(record.ConversationId))
                return;
            record.WorkspacePath = NormalizeWorkspacePath(ReadString(item, "workspacePath"));
            record.Summary = ReadString(item, "summary");
            record.Status = ReadString(item, "status");
            record.StartedAt = ReadTime(item, "startedAt");
            DateTimeOffset updated;
            if (!DateTimeOffset.TryParse(ReadString(item, "updatedAt"), out updated))
                updated = DateTimeOffset.MinValue;
            record.UpdatedAt = updated;
            if (record.Status == "running" && (updated == DateTimeOffset.MinValue || now - updated > StaleAfter))
                record.Status = "unknown";
            else if (record.Status == "running" && record.StartedAt == DateTimeOffset.MinValue)
                record.StartedAt = updated;
            result.Tasks.Add(record);
            stored.Add(record);
            if (seen != null && !string.IsNullOrEmpty(record.ConversationId))
                seen[record.ConversationId] = true;
        }

        static string ReadShared(string path)
        {
            using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
                return reader.ReadToEnd();
        }

        static DateTimeOffset ReadTime(Dictionary<string, object> item, string key)
        {
            DateTimeOffset value;
            if (!DateTimeOffset.TryParse(ReadString(item, key), out value))
                return DateTimeOffset.MinValue;
            return value;
        }

        static string ReadString(Dictionary<string, object> item, string key)
        {
            object value;
            if (!item.TryGetValue(key, out value) || value == null)
                return "";
            return value.ToString();
        }
    }
}
