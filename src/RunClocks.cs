using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Biubiu
{
    public static class RunClocks
    {
        public sealed class Clock
        {
            public DateTimeOffset StartedAt;
            public DateTimeOffset EndedAt;
        }

        public static string DefaultPath()
        {
            string root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(root, "Biubiu", "runs.txt");
        }

        public static string Format(TimeSpan span)
        {
            if (span < TimeSpan.Zero)
                span = TimeSpan.Zero;
            int hours = (int)span.TotalHours;
            return hours.ToString("00", CultureInfo.InvariantCulture)
                + ":" + span.Minutes.ToString("00", CultureInfo.InvariantCulture)
                + ":" + span.Seconds.ToString("00", CultureInfo.InvariantCulture);
        }

        public static string Text(DateTimeOffset started, DateTimeOffset ended, DateTimeOffset now)
        {
            if (started == DateTimeOffset.MinValue)
                return "";
            DateTimeOffset end = ended == DateTimeOffset.MinValue ? now : ended;
            if (end < started)
                end = started;
            return Format(end - started);
        }

        public static Dictionary<string, Clock> Load(string path)
        {
            Dictionary<string, Clock> clocks = new Dictionary<string, Clock>(StringComparer.Ordinal);
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return clocks;
            string[] lines;
            try
            {
                lines = File.ReadAllLines(path, Encoding.UTF8);
            }
            catch (IOException)
            {
                return clocks;
            }
            catch (UnauthorizedAccessException)
            {
                return clocks;
            }
            for (int i = 0; i < lines.Length; i++)
            {
                string[] parts = lines[i].Split('\t');
                if (parts.Length < 2 || string.IsNullOrEmpty(parts[0]))
                    continue;
                DateTimeOffset started;
                if (!DateTimeOffset.TryParse(parts[1], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out started))
                    continue;
                DateTimeOffset ended = DateTimeOffset.MinValue;
                if (parts.Length > 2 && parts[2].Length > 0)
                    DateTimeOffset.TryParse(parts[2], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out ended);
                Clock clock = new Clock();
                clock.StartedAt = started;
                clock.EndedAt = ended;
                clocks[parts[0]] = clock;
            }
            return clocks;
        }

        public static void Save(string path, Dictionary<string, Clock> clocks)
        {
            if (string.IsNullOrEmpty(path) || clocks == null)
                return;
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);
            StringBuilder text = new StringBuilder();
            foreach (KeyValuePair<string, Clock> pair in clocks)
            {
                if (string.IsNullOrEmpty(pair.Key) || pair.Value == null || pair.Key.IndexOf('\t') >= 0 || pair.Key.IndexOf('\n') >= 0)
                    continue;
                text.Append(pair.Key);
                text.Append('\t');
                text.Append(pair.Value.StartedAt.ToString("o", CultureInfo.InvariantCulture));
                text.Append('\t');
                if (pair.Value.EndedAt != DateTimeOffset.MinValue)
                    text.Append(pair.Value.EndedAt.ToString("o", CultureInfo.InvariantCulture));
                text.Append('\n');
            }
            File.WriteAllText(path, text.ToString(), new UTF8Encoding(false));
        }

        public static bool Apply(ProjectCard card, DateTimeOffset now, Dictionary<string, Clock> clocks)
        {
            if (card == null || clocks == null)
                return false;
            string status = card.Status ?? "";
            bool active = status == "running" || status == "confirm";
            bool finished = status == "completed" || status == "aborted" || status == "error" || status == "unknown";
            if (!active && !finished)
                return false;
            string key = Key(card);
            if (key.Length == 0)
                return false;

            Clock clock;
            clocks.TryGetValue(key, out clock);
            if (active)
            {
                DateTimeOffset start = card.StartedAt;
                if (start == DateTimeOffset.MinValue)
                    start = clock != null && clock.EndedAt == DateTimeOffset.MinValue ? clock.StartedAt : now;
                bool sameOpen = clock != null
                    && clock.EndedAt == DateTimeOffset.MinValue
                    && start >= clock.StartedAt
                    && start - clock.StartedAt < TimeSpan.FromSeconds(3);
                if (sameOpen)
                    start = clock.StartedAt;
                bool changed = clock == null || clock.StartedAt != start || clock.EndedAt != DateTimeOffset.MinValue;
                if (changed)
                {
                    clock = new Clock();
                    clock.StartedAt = start;
                    clock.EndedAt = DateTimeOffset.MinValue;
                    clocks[key] = clock;
                }
                card.StartedAt = start;
                card.EndedAt = DateTimeOffset.MinValue;
                return changed;
            }

            DateTimeOffset started = card.StartedAt != DateTimeOffset.MinValue
                ? card.StartedAt
                : (clock == null ? DateTimeOffset.MinValue : clock.StartedAt);
            if (started == DateTimeOffset.MinValue)
                return false;
            DateTimeOffset ended;
            if (clock != null && clock.StartedAt == started && clock.EndedAt != DateTimeOffset.MinValue)
                ended = clock.EndedAt;
            else if (card.EndedAt != DateTimeOffset.MinValue && card.EndedAt >= started)
                ended = card.EndedAt;
            else
                ended = now;
            bool write = clock == null || clock.StartedAt != started || clock.EndedAt != ended;
            if (write)
            {
                clock = new Clock();
                clock.StartedAt = started;
                clock.EndedAt = ended;
                clocks[key] = clock;
            }
            card.StartedAt = started;
            card.EndedAt = ended;
            return write;
        }

        static string Key(ProjectCard card)
        {
            if (!string.IsNullOrEmpty(card.ConversationId))
                return "c:" + card.ConversationId;
            if (!string.IsNullOrEmpty(card.NoteKey))
                return "n:" + card.NoteKey;
            return "";
        }
    }
}
