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

        public static string DefaultPath()
        {
            string overridePath = Environment.GetEnvironmentVariable("BIUBIU_TASKS_PATH");
            if (!string.IsNullOrEmpty(overridePath))
                return overridePath;
            string root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(root, "Biubiu", "tasks.json");
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
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return result;

            string text;
            try
            {
                text = File.ReadAllText(path, Encoding.UTF8);
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

            if (string.IsNullOrWhiteSpace(text))
                return result;

            object root;
            try
            {
                JavaScriptSerializer serializer = new JavaScriptSerializer();
                serializer.MaxJsonLength = 2000000;
                root = serializer.DeserializeObject(text);
            }
            catch (Exception)
            {
                result.Ok = false;
                result.Error = "tasks file is invalid";
                return result;
            }

            Dictionary<string, object> document = root as Dictionary<string, object>;
            if (document == null)
            {
                result.Ok = false;
                result.Error = "tasks file is invalid";
                return result;
            }

            object tasksValue;
            if (!document.TryGetValue("tasks", out tasksValue) || tasksValue == null)
                return result;
            IList tasks = tasksValue as IList;
            if (tasks == null)
            {
                result.Ok = false;
                result.Error = "tasks file is invalid";
                return result;
            }

            for (int i = 0; i < tasks.Count; i++)
            {
                Dictionary<string, object> item = tasks[i] as Dictionary<string, object>;
                if (item == null)
                    continue;
                TaskRecord record = new TaskRecord();
                record.ConversationId = ReadString(item, "conversationId");
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
            }
            return result;
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
