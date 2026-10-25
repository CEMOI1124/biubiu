using System;
using System.Collections.Generic;
using System.Globalization;
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
        static readonly Dictionary<string, string> userSummaries = new Dictionary<string, string>(StringComparer.Ordinal);

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
            List<TaskRecord> read = ReadDatabase(path, now);
            if (read == null)
                return cached != null && string.Equals(path, cachedPath, StringComparison.OrdinalIgnoreCase) ? cached : new List<TaskRecord>();
            cached = read;
            cachedPath = path ?? "";
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
            record.StartedAt = ReadRunStart(header);
            string composerStatus = ReadString(header, "status");
            bool unfinished = HasUnfinished(header);
            bool needsConfirmation = NeedsConfirmation(header, composerStatus, unfinished);
            record.Status = ResolveStatus(composerStatus, unfinished, needsConfirmation);
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
            if (status == "generating" || status == "loading" || status == "in_progress")
                return "running";
            if (status == "error" || status == "failed")
                return "error";
            if (status == "aborted" || status == "cancelled" || status == "canceled")
                return "aborted";
            return "completed";
        }

        static bool NeedsConfirmation(Dictionary<string, object> item, string composerStatus, bool unfinished)
        {
            if (ReadBool(item, "hasBlockingPendingActions"))
                return true;
            if (!ReadBool(item, "hasPendingPlan"))
                return false;
            if (unfinished)
                return false;
            string status = composerStatus == null ? "" : composerStatus.Trim().ToLowerInvariant();
            return status != "generating" && status != "loading" && status != "in_progress";
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

        static DateTimeOffset ReadRunStart(Dictionary<string, object> header)
        {
            if (header == null || !header.ContainsKey("unfinishedRunAt") || header["unfinishedRunAt"] == null)
                return DateTimeOffset.MinValue;
            double number;
            if (!double.TryParse(header["unfinishedRunAt"].ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out number))
                return DateTimeOffset.MinValue;
            if (number >= 1000000000000d)
                return DateTimeOffset.FromUnixTimeMilliseconds((long)number);
            if (number >= 1000000000d)
                return DateTimeOffset.FromUnixTimeSeconds((long)number);
            return DateTimeOffset.MinValue;
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
            IntPtr history = IntPtr.Zero;
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
                if (sqlite3_prepare_v2(db, "SELECT value FROM cursorDiskKV WHERE key >= ? AND key < ? ORDER BY key DESC LIMIT 2", -1, out range, IntPtr.Zero) != 0)
                    return null;
                if (sqlite3_prepare_v2(db, "SELECT value FROM cursorDiskKV WHERE key >= ? AND key < ?", -1, out history, IntPtr.Zero) != 0)
                    return null;
                foreach (TaskRecord record in newest.Values)
                {
                    string body = QueryOne(lookup, "composerData:" + record.ConversationId);
                    ApplyBody(record, body);
                    string latestBubble = "";
                    string latestTool = "";
                    string user = LatestUserMessage(range, history, lookup, record.ConversationId, out latestBubble, out latestTool);
                    if (user.Length == 0)
                        continue;
                    record.Summary = user;
                    string activity = LiveTool(lookup, record.ConversationId, latestBubble, latestTool);
                    if (activity == "confirm")
                        record.Status = "confirm";
                    else if (activity == "running" && record.Status != "confirm")
                        record.Status = "running";
                    if (activity.Length > 0 && record.UpdatedAt < now)
                        record.UpdatedAt = now;
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
                if (history != IntPtr.Zero)
                    sqlite3_finalize(history);
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
            string composerStatus = ReadString(body, "status");
            bool unfinished = HasUnfinished(body) || ReadString(body, "chatGenerationUUID").Length > 0;
            bool needsConfirmation = NeedsConfirmation(body, composerStatus, unfinished);
            record.Status = ResolveStatus(composerStatus, unfinished, needsConfirmation);
            DateTimeOffset started = ReadRunStart(body);
            if (started != DateTimeOffset.MinValue)
                record.StartedAt = started;
        }

        public static string ToolActivity(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return "";
            object root;
            try
            {
                JavaScriptSerializer serializer = new JavaScriptSerializer();
                serializer.MaxJsonLength = 20000000;
                root = serializer.DeserializeObject(json);
            }
            catch (Exception)
            {
                return "";
            }
            Dictionary<string, object> bubble = root as Dictionary<string, object>;
            Dictionary<string, object> tool = ReadObject(bubble, "toolFormerData");
            Dictionary<string, object> extra = ReadObject(tool, "additionalData");
            string status = ReadString(extra, "status").Trim().ToLowerInvariant();
            if (status == "running")
                return "running";
            Dictionary<string, object> review = ReadObject(extra, "reviewData");
            string reviewStatus = ReadString(review, "status");
            if (string.Equals(reviewStatus, "REQUESTED", StringComparison.OrdinalIgnoreCase))
                return "confirm";
            if (status == "loading" || status == "pending")
                return "running";
            return "";
        }

        static string LiveTool(IntPtr lookup, string conversationId, string latestBubble, string latestTool)
        {
            string first = ToolActivity(LoadBubble(lookup, conversationId, latestBubble));
            if (first.Length > 0 || string.Equals(latestBubble, latestTool, StringComparison.Ordinal))
                return first;
            return ToolActivity(LoadBubble(lookup, conversationId, latestTool));
        }

        static string LoadBubble(IntPtr lookup, string conversationId, string bubbleId)
        {
            if (string.IsNullOrEmpty(conversationId) || string.IsNullOrEmpty(bubbleId))
                return "";
            return QueryOne(lookup, "bubbleId:" + conversationId + ":" + bubbleId);
        }

        static string LatestUserMessage(IntPtr range, IntPtr history, IntPtr lookup, string conversationId, out string latestBubble, out string latestTool)
        {
            latestBubble = "";
            latestTool = "";
            if (string.IsNullOrEmpty(conversationId))
                return "";
            string prefix = "composerConvHeaders:" + conversationId + ":";
            string userBubble = ReadChunks(range, prefix, true, out latestBubble, out latestTool);
            string summary = UserSummary(lookup, conversationId, userBubble);
            if (summary.Length == 0)
            {
                string remembered;
                if (!userSummaries.TryGetValue(conversationId, out remembered))
                {
                    string ignoredBubble;
                    string ignoredTool;
                    string older = ReadChunks(history, prefix, false, out ignoredBubble, out ignoredTool);
                    remembered = UserSummary(lookup, conversationId, older);
                }
                summary = remembered ?? "";
            }
            if (summary.Length > 0)
                userSummaries[conversationId] = summary;
            return summary;
        }

        static string ReadChunks(IntPtr statement, string prefix, bool newestFirst, out string latestBubble, out string latestTool)
        {
            latestBubble = "";
            latestTool = "";
            sqlite3_reset(statement);
            if (BindText(statement, 1, prefix) != 0 || BindText(statement, 2, prefix + "~") != 0)
                return "";
            List<string> chunks = new List<string>();
            while (sqlite3_step(statement) == SqliteRow)
                chunks.Add(ColumnText(statement, 0));
            string userBubble = "";
            long newestStart = long.MinValue;
            long newestToolStart = long.MinValue;
            JavaScriptSerializer serializer = new JavaScriptSerializer();
            serializer.MaxJsonLength = 20000000;
            if (newestFirst)
            {
                for (int i = chunks.Count - 1; i >= 0; i--)
                    WalkChunk(chunks[i], serializer, ref userBubble, ref latestBubble, ref newestStart, ref latestTool, ref newestToolStart);
            }
            else
            {
                for (int i = 0; i < chunks.Count; i++)
                    WalkChunk(chunks[i], serializer, ref userBubble, ref latestBubble, ref newestStart, ref latestTool, ref newestToolStart);
            }
            return userBubble;
        }

        static void WalkChunk(string json, JavaScriptSerializer serializer, ref string userBubble, ref string latestBubble, ref long newestStart, ref string latestTool, ref long newestToolStart)
        {
            object root;
            try
            {
                root = serializer.DeserializeObject(json);
            }
            catch (Exception)
            {
                return;
            }
            System.Collections.IList items = root as System.Collections.IList;
            if (items == null)
                return;
            for (int i = 0; i < items.Count; i++)
            {
                Dictionary<string, object> item = items[i] as Dictionary<string, object>;
                if (item == null)
                    continue;
                string id = ReadString(item, "bubbleId");
                if (ReadInt(item, "type") == 1 && id.Length > 0)
                    userBubble = id;
                long started = ReadLong(item, "startedAtMs");
                if (id.Length == 0 || started < newestStart)
                    continue;
                newestStart = started;
                latestBubble = id;
                if (id.IndexOf("/s:", StringComparison.Ordinal) < 0 && started >= newestToolStart)
                {
                    newestToolStart = started;
                    latestTool = id;
                }
            }
        }

        static string UserSummary(IntPtr lookup, string conversationId, string bubbleId)
        {
            if (bubbleId.Length == 0)
                return "";
            string bubble = QueryOne(lookup, "bubbleId:" + conversationId + ":" + bubbleId);
            if (bubble.Length == 0)
                return "";
            object parsed;
            try
            {
                JavaScriptSerializer serializer = new JavaScriptSerializer();
                serializer.MaxJsonLength = 20000000;
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

        static long ReadLong(Dictionary<string, object> item, string key)
        {
            if (item == null)
                return 0;
            object value;
            if (!item.TryGetValue(key, out value) || value == null)
                return 0;
            if (value is long)
                return (long)value;
            if (value is int)
                return (int)value;
            if (value is decimal)
                return (long)(decimal)value;
            long parsed;
            if (!long.TryParse(value.ToString(), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out parsed))
                return 0;
            return parsed;
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
            int length = sqlite3_column_bytes(statement, column);
            if (length <= 0)
                return "";
            IntPtr pointer = sqlite3_column_text(statement, column);
            if (pointer == IntPtr.Zero)
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
        static extern int sqlite3_column_bytes(IntPtr statement, int column);

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
