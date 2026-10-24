using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Web.Script.Serialization;

namespace Biubiu
{
    public static class ComposerHistory
    {
        const int OpenReadonly = 1;
        const int SqliteRow = 100;

        static List<TaskRecord> cached = new List<TaskRecord>();
        static string cachedPath = "";
        static DateTimeOffset cachedUntil;

        public static string DefaultDatabase()
        {
            string root = Environment.GetEnvironmentVariable("BIUBIU_CURSOR_USER_DIR");
            if (string.IsNullOrEmpty(root))
            {
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                if (string.IsNullOrEmpty(appData))
                    return "";
                root = Path.Combine(appData, "Cursor", "User");
            }
            return Path.Combine(root, "globalStorage", "state.vscdb");
        }

        public static List<TaskRecord> Load(string path, DateTimeOffset now)
        {
            if (cached != null && string.Equals(path, cachedPath, StringComparison.OrdinalIgnoreCase) && now < cachedUntil)
                return cached;
            List<TaskRecord> read = ReadDatabase(path, now);
            if (read == null)
                return cached ?? new List<TaskRecord>();
            cached = read;
            cachedPath = path ?? "";
            cachedUntil = now.AddSeconds(2);
            return read;
        }

        public static TaskRecord FromHeader(string json, long updatedAtMs, DateTimeOffset now)
        {
            if (string.IsNullOrWhiteSpace(json))
                return null;
            object root;
            try
            {
                JavaScriptSerializer serializer = new JavaScriptSerializer();
                serializer.MaxJsonLength = 2000000;
                root = serializer.DeserializeObject(json);
            }
            catch (Exception)
            {
                return null;
            }
            Dictionary<string, object> header = root as Dictionary<string, object>;
            if (header == null || ReadBool(header, "isDraft"))
                return null;
            string mode = ReadString(header, "unifiedMode");
            if (mode == "chat")
                return null;

            string summary = ReadString(header, "name");
            if (summary.Length == 0)
                summary = ReadString(header, "subtitle");
            summary = summary.Trim();
            if (summary.Length > 80)
                summary = summary.Substring(0, 80);
            if (summary.Length == 0)
                return null;

            string workspace = WorkspacePath(header);
            if (workspace.Length == 0)
                return null;

            TaskRecord record = new TaskRecord();
            record.ConversationId = ReadString(header, "composerId");
            record.WorkspacePath = workspace;
            record.Summary = summary;
            record.UpdatedAt = updatedAtMs > 0
                ? DateTimeOffset.FromUnixTimeMilliseconds(updatedAtMs)
                : DateTimeOffset.MinValue;
            bool needsConfirmation = ReadBool(header, "hasBlockingPendingActions") || ReadBool(header, "hasPendingPlan");
            record.Status = ResolveStatus(ReadString(header, "status"), HasUnfinished(header), needsConfirmation);
            return record;
        }

        public static string ResolveStatus(string composerStatus, bool unfinished)
        {
            return ResolveStatus(composerStatus, unfinished, false);
        }

        public static string ResolveStatus(string composerStatus, bool unfinished, bool needsConfirmation)
        {
            if (needsConfirmation)
                return "confirm";
            if (unfinished)
                return "running";
            string status = composerStatus == null ? "" : composerStatus.Trim().ToLowerInvariant();
            if (status == "error" || status == "failed")
                return "error";
            if (status == "aborted" || status == "cancelled" || status == "canceled")
                return "aborted";
            return "completed";
        }

        public static string SummarizeUserMessage(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return "";
            int newline = text.IndexOf('\n');
            if (newline >= 0)
                text = text.Substring(0, newline);
            text = System.Text.RegularExpressions.Regex.Replace(text, "\\s+", " ").Trim();
            if (text.Length > 80)
                text = text.Substring(0, 80);
            return text;
        }

        static bool HasUnfinished(Dictionary<string, object> header)
        {
            if (header == null || !header.ContainsKey("unfinishedRunAt") || header["unfinishedRunAt"] == null)
                return false;
            string text = header["unfinishedRunAt"].ToString();
            return text.Length > 0 && text != "0" && text != "false";
        }

        static List<TaskRecord> ReadDatabase(string path, DateTimeOffset now)
        {
            List<TaskRecord> tasks = new List<TaskRecord>();
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return tasks;

            IntPtr db;
            byte[] name = Encoding.UTF8.GetBytes(path + "\0");
            if (sqlite3_open_v2(name, out db, OpenReadonly, IntPtr.Zero) != 0)
            {
                if (db != IntPtr.Zero)
                    sqlite3_close(db);
                return tasks;
            }

            IntPtr statement = IntPtr.Zero;
            IntPtr lookup = IntPtr.Zero;
            IntPtr range = IntPtr.Zero;
            try
            {
                sqlite3_busy_timeout(db, 300);
                string sql = "SELECT value, lastUpdatedAt FROM composerHeaders WHERE ifnull(isArchived, 0) = 0 AND ifnull(isSubagent, 0) = 0";
                if (sqlite3_prepare_v2(db, sql, -1, out statement, IntPtr.Zero) != 0)
                    return null;
                Dictionary<string, TaskRecord> newest = new Dictionary<string, TaskRecord>(StringComparer.OrdinalIgnoreCase);
                while (sqlite3_step(statement) == SqliteRow)
                {
                    string json = ColumnText(statement, 0);
                    long updated = sqlite3_column_int64(statement, 1);
                    TaskRecord record = FromHeader(json, updated, now);
                    if (record == null || string.IsNullOrEmpty(record.WorkspacePath))
                        continue;
                    TaskRecord previous;
                    if (!newest.TryGetValue(record.WorkspacePath, out previous) || record.UpdatedAt > previous.UpdatedAt)
                        newest[record.WorkspacePath] = record;
                }
                if (sqlite3_prepare_v2(db, "SELECT value FROM cursorDiskKV WHERE key = ?", -1, out lookup, IntPtr.Zero) != 0)
                    return null;
                if (sqlite3_prepare_v2(db, "SELECT value FROM cursorDiskKV WHERE key >= ? AND key < ?", -1, out range, IntPtr.Zero) != 0)
                    return null;
                foreach (TaskRecord record in newest.Values)
                {
                    string body = QueryOne(lookup, "composerData:" + record.ConversationId);
                    ApplyBody(record, body);
                    string user = LatestUserMessage(range, lookup, record.ConversationId);
                    if (user.Length == 0)
                        continue;
                    record.Summary = user;
                    tasks.Add(record);
                }
            }
            catch (Exception)
            {
                return null;
            }
            finally
            {
                if (statement != IntPtr.Zero)
                    sqlite3_finalize(statement);
                if (lookup != IntPtr.Zero)
                    sqlite3_finalize(lookup);
                if (range != IntPtr.Zero)
                    sqlite3_finalize(range);
                sqlite3_close(db);
            }
            return tasks;
        }

        static void ApplyBody(TaskRecord record, string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return;
            object root;
            try
            {
                JavaScriptSerializer serializer = new JavaScriptSerializer();
                serializer.MaxJsonLength = 20000000;
                root = serializer.DeserializeObject(json);
            }
            catch (Exception)
            {
                return;
            }
            Dictionary<string, object> body = root as Dictionary<string, object>;
            if (body == null)
                return;
            bool needsConfirmation = record.Status == "confirm" || ReadBool(body, "hasBlockingPendingActions") || ReadBool(body, "hasPendingPlan");
            record.Status = ResolveStatus(ReadString(body, "status"), HasUnfinished(body), needsConfirmation);
        }

        static string LatestUserMessage(IntPtr range, IntPtr lookup, string conversationId)
        {
            if (string.IsNullOrEmpty(conversationId))
                return "";
            string prefix = "composerConvHeaders:" + conversationId + ":";
            sqlite3_reset(range);
            if (BindText(range, 1, prefix) != 0 || BindText(range, 2, prefix + "~") != 0)
                return "";
            string bubbleId = "";
            JavaScriptSerializer serializer = new JavaScriptSerializer();
            serializer.MaxJsonLength = 20000000;
            while (sqlite3_step(range) == SqliteRow)
            {
                object root;
                try
                {
                    root = serializer.DeserializeObject(ColumnText(range, 0));
                }
                catch (Exception)
                {
                    continue;
                }
                System.Collections.IList items = root as System.Collections.IList;
                if (items == null)
                    continue;
                for (int i = 0; i < items.Count; i++)
                {
                    Dictionary<string, object> item = items[i] as Dictionary<string, object>;
                    if (item == null || ReadInt(item, "type") != 1)
                        continue;
                    string id = ReadString(item, "bubbleId");
                    if (id.Length > 0)
                        bubbleId = id;
                }
            }
            if (bubbleId.Length == 0)
                return "";
            string bubble = QueryOne(lookup, "bubbleId:" + conversationId + ":" + bubbleId);
            if (bubble.Length == 0)
                return "";
            object parsed;
            try
            {
                parsed = serializer.DeserializeObject(bubble);
            }
            catch (Exception)
            {
                return "";
            }
            return SummarizeUserMessage(ReadString(parsed as Dictionary<string, object>, "text"));
        }

        static string QueryOne(IntPtr statement, string key)
        {
            sqlite3_reset(statement);
            if (BindText(statement, 1, key) != 0)
                return "";
            if (sqlite3_step(statement) != SqliteRow)
                return "";
            return ColumnText(statement, 0);
        }

        static int BindText(IntPtr statement, int index, string text)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(text ?? "");
            return sqlite3_bind_text(statement, index, bytes, bytes.Length, new IntPtr(-1));
        }

        static int ReadInt(Dictionary<string, object> item, string key)
        {
            if (item == null)
                return 0;
            object value;
            if (!item.TryGetValue(key, out value) || value == null)
                return 0;
            if (value is int)
                return (int)value;
            if (value is long)
                return (int)(long)value;
            if (value is decimal)
                return (int)(decimal)value;
            int parsed;
            if (!int.TryParse(value.ToString(), out parsed))
                return 0;
            return parsed;
        }

        static string WorkspacePath(Dictionary<string, object> header)
        {
            Dictionary<string, object> identifier = ReadObject(header, "workspaceIdentifier");
            Dictionary<string, object> uri = ReadObject(identifier, "uri");
            string path = ReadString(uri, "fsPath");
            if (path.Length == 0)
                path = ReadString(uri, "path");
            return TaskBoard.NormalizeWorkspacePath(path);
        }

        static Dictionary<string, object> ReadObject(Dictionary<string, object> item, string key)
        {
            if (item == null)
                return null;
            object value;
            if (!item.TryGetValue(key, out value))
                return null;
            return value as Dictionary<string, object>;
        }

        static string ReadString(Dictionary<string, object> item, string key)
        {
            if (item == null)
                return "";
            object value;
            if (!item.TryGetValue(key, out value) || value == null)
                return "";
            return value.ToString();
        }

        static bool ReadBool(Dictionary<string, object> item, string key)
        {
            if (item == null)
                return false;
            object value;
            if (!item.TryGetValue(key, out value) || value == null)
                return false;
            if (value is bool)
                return (bool)value;
            return value.ToString() == "true";
        }

        static string ColumnText(IntPtr statement, int column)
        {
            IntPtr pointer = sqlite3_column_text(statement, column);
            if (pointer == IntPtr.Zero)
                return "";
            int length = 0;
            while (Marshal.ReadByte(pointer, length) != 0)
                length++;
            if (length == 0)
                return "";
            byte[] bytes = new byte[length];
            Marshal.Copy(pointer, bytes, 0, length);
            return Encoding.UTF8.GetString(bytes);
        }

        [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)]
        static extern int sqlite3_open_v2(byte[] filename, out IntPtr db, int flags, IntPtr vfs);

        [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)]
        static extern int sqlite3_busy_timeout(IntPtr db, int milliseconds);

        [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)]
        static extern int sqlite3_prepare_v2(IntPtr db, string sql, int length, out IntPtr statement, IntPtr tail);

        [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)]
        static extern int sqlite3_bind_text(IntPtr statement, int index, byte[] value, int length, IntPtr destructor);

        [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)]
        static extern int sqlite3_reset(IntPtr statement);

        [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)]
        static extern int sqlite3_step(IntPtr statement);

        [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)]
        static extern IntPtr sqlite3_column_text(IntPtr statement, int column);

        [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)]
        static extern long sqlite3_column_int64(IntPtr statement, int column);

        [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)]
        static extern int sqlite3_finalize(IntPtr statement);

        [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)]
        static extern int sqlite3_close(IntPtr db);
    }
}
