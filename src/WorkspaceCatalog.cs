using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Biubiu
{
    public sealed class WorkspaceCatalog
    {
        readonly Dictionary<string, List<string>> pathsByName = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        readonly List<string> names = new List<string>();

        public IList<string> Names
        {
            get { return names; }
        }

        public static WorkspaceCatalog Load()
        {
            WorkspaceCatalog catalog = new WorkspaceCatalog();
            string root = UserDirectory();
            if (string.IsNullOrEmpty(root))
                return catalog;

            string storage = Path.Combine(root, "workspaceStorage");
            if (!Directory.Exists(storage))
                return catalog;

            string[] dirs;
            try
            {
                dirs = Directory.GetDirectories(storage);
            }
            catch (IOException)
            {
                return catalog;
            }
            catch (UnauthorizedAccessException)
            {
                return catalog;
            }

            for (int i = 0; i < dirs.Length; i++)
            {
                string jsonPath = Path.Combine(dirs[i], "workspace.json");
                if (!File.Exists(jsonPath))
                    continue;

                string json;
                try
                {
                    json = File.ReadAllText(jsonPath, Encoding.UTF8);
                }
                catch (IOException)
                {
                    continue;
                }
                catch (UnauthorizedAccessException)
                {
                    continue;
                }

                string folderUri = ReadJsonString(json, "folder");
                string path = FileUriToPath(folderUri);
                string name = FolderName(path);
                if (string.IsNullOrEmpty(name))
                {
                    string workspaceUri = ReadJsonString(json, "workspace");
                    path = FileUriToPath(workspaceUri);
                    name = WorkspaceFileName(path);
                }
                if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(path))
                    continue;

                catalog.Add(name, path);
            }

            return catalog;
        }

        public string UniquePath(string folderName)
        {
            if (string.IsNullOrEmpty(folderName))
                return null;

            List<string> paths;
            if (!pathsByName.TryGetValue(folderName, out paths))
                return null;
            if (paths.Count != 1)
                return null;
            return paths[0];
        }

        public static string FileUriToPath(string uri)
        {
            if (string.IsNullOrEmpty(uri))
                return null;
            if (!uri.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
                return null;

            string path = uri;
            if (path.StartsWith("file:///", StringComparison.OrdinalIgnoreCase))
                path = path.Substring("file:///".Length);
            else if (path.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
                path = path.Substring("file://".Length);

            path = Uri.UnescapeDataString(path);
            path = path.Replace('/', '\\');
            return path;
        }

        public static string FolderName(string path)
        {
            if (string.IsNullOrEmpty(path))
                return null;
            string trimmed = path.TrimEnd('\\');
            int slash = trimmed.LastIndexOf('\\');
            if (slash >= 0 && slash < trimmed.Length - 1)
                return trimmed.Substring(slash + 1);
            return trimmed;
        }

        static string UserDirectory()
        {
            string overrideDir = Environment.GetEnvironmentVariable("BIUBIU_CURSOR_USER_DIR");
            if (!string.IsNullOrEmpty(overrideDir))
                return overrideDir;

            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            if (string.IsNullOrEmpty(appData))
                return null;
            return Path.Combine(appData, "Cursor", "User");
        }

        static string WorkspaceFileName(string path)
        {
            string name = FolderName(path);
            if (string.IsNullOrEmpty(name))
                return null;
            if (name.EndsWith(".code-workspace", StringComparison.OrdinalIgnoreCase))
                return name.Substring(0, name.Length - ".code-workspace".Length);
            return name;
        }

        void Add(string name, string path)
        {
            List<string> paths;
            if (!pathsByName.TryGetValue(name, out paths))
            {
                paths = new List<string>();
                pathsByName.Add(name, paths);
                names.Add(name);
            }
            if (!ContainsPath(paths, path))
                paths.Add(path);
        }

        static bool ContainsPath(List<string> paths, string path)
        {
            for (int i = 0; i < paths.Count; i++)
            {
                if (string.Equals(paths[i], path, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        static string ReadJsonString(string json, string property)
        {
            if (string.IsNullOrEmpty(json) || string.IsNullOrEmpty(property))
                return null;

            string key = "\"" + property + "\"";
            int keyAt = json.IndexOf(key, StringComparison.Ordinal);
            if (keyAt < 0)
                return null;

            int colon = json.IndexOf(':', keyAt + key.Length);
            if (colon < 0)
                return null;

            int quote = json.IndexOf('"', colon + 1);
            if (quote < 0)
                return null;

            StringBuilder value = new StringBuilder();
            for (int i = quote + 1; i < json.Length; i++)
            {
                char c = json[i];
                if (c == '\\' && i + 1 < json.Length)
                {
                    char next = json[i + 1];
                    if (next == '"' || next == '\\' || next == '/')
                    {
                        value.Append(next);
                        i++;
                        continue;
                    }
                    if (next == 'u' && i + 5 < json.Length)
                    {
                        int code;
                        string hex = json.Substring(i + 2, 4);
                        if (int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out code))
                        {
                            value.Append((char)code);
                            i += 5;
                            continue;
                        }
                    }
                }
                if (c == '"')
                    return value.ToString();
                value.Append(c);
            }
            return null;
        }
    }
}
