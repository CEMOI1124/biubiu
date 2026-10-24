using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace Biubiu
{
    public static class CardNotes
    {
        public static string DefaultPath()
        {
            string root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(root, "Biubiu", "notes.json");
        }

        public static string Key(string workspacePath, string project)
        {
            string path = TaskBoard.NormalizeWorkspacePath(workspacePath ?? "");
            if (path.Length > 0)
                return "path:" + path.ToLowerInvariant();
            return "name:" + (project ?? "").Trim().ToLowerInvariant();
        }

        public static Dictionary<string, string> Load(string path)
        {
            Dictionary<string, string> notes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return notes;
            string text = File.ReadAllText(path, Encoding.UTF8);
            if (string.IsNullOrWhiteSpace(text))
                return notes;
            JavaScriptSerializer serializer = new JavaScriptSerializer();
            object root = serializer.DeserializeObject(text);
            Dictionary<string, object> document = root as Dictionary<string, object>;
            if (document == null)
                return notes;
            object value;
            if (!document.TryGetValue("notes", out value) || !(value is IDictionary))
                return notes;
            IDictionary map = (IDictionary)value;
            foreach (object key in map.Keys)
            {
                if (key == null || map[key] == null)
                    continue;
                string note = map[key].ToString();
                if (note.Trim().Length == 0)
                    continue;
                notes[key.ToString()] = note;
            }
            return notes;
        }

        public static void Save(string path, IDictionary<string, string> notes)
        {
            if (string.IsNullOrEmpty(path))
                return;
            Dictionary<string, string> clean = new Dictionary<string, string>();
            if (notes != null)
            {
                foreach (KeyValuePair<string, string> pair in notes)
                {
                    if (string.IsNullOrEmpty(pair.Key) || string.IsNullOrEmpty(pair.Value))
                        continue;
                    string text = pair.Value.Trim();
                    if (text.Length == 0)
                        continue;
                    clean[pair.Key] = text;
                }
            }
            Dictionary<string, object> document = new Dictionary<string, object>();
            document["notes"] = clean;
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);
            JavaScriptSerializer serializer = new JavaScriptSerializer();
            File.WriteAllText(path, serializer.Serialize(document), new UTF8Encoding(false));
        }
    }
}
