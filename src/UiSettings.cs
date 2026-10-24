using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace Biubiu
{
    public static class UiSettings
    {
        public const float MinScale = 0.8f;
        public const float MaxScale = 1.6f;

        public static string DefaultPath()
        {
            string root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(root, "Biubiu", "ui.txt");
        }

        public static bool LoadVertical(string path)
        {
            string[] lines = ReadLines(path);
            return lines.Length > 0 && lines[0].Trim() == "vertical";
        }

        public static bool LoadPinned(string path)
        {
            string[] lines = ReadLines(path);
            return lines.Length > 1 && lines[1].Trim() == "pinned";
        }

        public static float LoadScale(string path)
        {
            string[] lines = ReadLines(path);
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (!line.StartsWith("scale ", StringComparison.Ordinal))
                    continue;
                float value;
                if (float.TryParse(line.Substring(6), NumberStyles.Float, CultureInfo.InvariantCulture, out value))
                    return ClampScale(value);
            }
            return 1f;
        }

        public static void SaveVertical(string path, bool vertical)
        {
            Save(path, vertical, LoadPinned(path), LoadScale(path));
        }

        public static void SavePinned(string path, bool pinned)
        {
            Save(path, LoadVertical(path), pinned, LoadScale(path));
        }

        public static void SaveScale(string path, float scale)
        {
            Save(path, LoadVertical(path), LoadPinned(path), scale);
        }

        public static float ClampScale(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
                return 1f;
            if (value < MinScale)
                return MinScale;
            if (value > MaxScale)
                return MaxScale;
            return value;
        }

        static void Save(string path, bool vertical, bool pinned, float scale)
        {
            if (string.IsNullOrEmpty(path))
                return;
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);
            scale = ClampScale(scale);
            StringBuilder text = new StringBuilder();
            text.Append(vertical ? "vertical" : "horizontal");
            text.Append('\n');
            if (pinned)
            {
                text.Append("pinned");
                text.Append('\n');
            }
            text.Append("scale ");
            text.Append(scale.ToString("0.00", CultureInfo.InvariantCulture));
            text.Append('\n');
            File.WriteAllText(path, text.ToString(), new UTF8Encoding(false));
        }

        static string[] ReadLines(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                    return new string[0];
                return File.ReadAllLines(path, Encoding.UTF8);
            }
            catch (IOException)
            {
                return new string[0];
            }
            catch (UnauthorizedAccessException)
            {
                return new string[0];
            }
        }
    }
}
