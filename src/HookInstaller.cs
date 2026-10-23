using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Web.Script.Serialization;

namespace Biubiu
{
    public sealed class HookInstallResult
    {
        public bool Ok;
        public string Error;
        public bool WroteScript;
        public bool WroteHooks;
    }

    public static class HookInstaller
    {
        public const string ScriptFileName = "biubiu-status.ps1";

        public static string DefaultCursorDirectory()
        {
            string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(profile, ".cursor");
        }

        public static string HookCommand(string eventName)
        {
            return "powershell -NoProfile -ExecutionPolicy Bypass -File ./hooks/" + ScriptFileName + " " + eventName;
        }

        public static string ReadEmbeddedScript()
        {
            Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Biubiu.status.ps1");
            if (stream == null)
                return null;
            using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
                return reader.ReadToEnd();
        }

        public static HookInstallResult Install(string cursorDirectory, string scriptText)
        {
            HookInstallResult result = new HookInstallResult();
            result.Ok = true;
            if (string.IsNullOrEmpty(cursorDirectory) || string.IsNullOrEmpty(scriptText))
            {
                result.Ok = false;
                result.Error = "hook script is missing";
                return result;
            }

            try
            {
                string hooksDirectory = Path.Combine(cursorDirectory, "hooks");
                Directory.CreateDirectory(hooksDirectory);
                result.WroteScript = WriteIfChanged(Path.Combine(hooksDirectory, ScriptFileName), NormalizeNewlines(scriptText));
                result.WroteHooks = MergeHooks(Path.Combine(cursorDirectory, "hooks.json"), result);
            }
            catch (IOException ex)
            {
                result.Ok = false;
                result.Error = ex.Message;
            }
            catch (UnauthorizedAccessException ex)
            {
                result.Ok = false;
                result.Error = ex.Message;
            }
            return result;
        }

        static bool WriteIfChanged(string path, string text)
        {
            if (File.Exists(path))
            {
                string current = NormalizeNewlines(File.ReadAllText(path, Encoding.UTF8));
                if (current == text)
                    return false;
            }
            File.WriteAllText(path, text, new UTF8Encoding(false));
            return true;
        }

        static bool MergeHooks(string path, HookInstallResult result)
        {
            JavaScriptSerializer serializer = new JavaScriptSerializer();
            serializer.MaxJsonLength = 2000000;
            Dictionary<string, object> document;
            string original = null;
            if (!File.Exists(path))
            {
                document = new Dictionary<string, object>();
                document["version"] = 1;
            }
            else
            {
                original = File.ReadAllText(path, Encoding.UTF8);
                if (string.IsNullOrWhiteSpace(original))
                {
                    document = new Dictionary<string, object>();
                    document["version"] = 1;
                }
                else
                {
                    object parsed;
                    try
                    {
                        parsed = serializer.DeserializeObject(original);
                    }
                    catch (Exception)
                    {
                        result.Ok = false;
                        result.Error = "hooks.json could not be parsed, so it was left unchanged";
                        return false;
                    }
                    document = parsed as Dictionary<string, object>;
                    if (document == null)
                    {
                        result.Ok = false;
                        result.Error = "hooks.json could not be parsed, so it was left unchanged";
                        return false;
                    }
                }
            }

            if (!document.ContainsKey("version"))
                document["version"] = 1;

            Dictionary<string, object> hooks = GetHooks(document, result);
            if (!result.Ok)
                return false;

            bool changed = false;
            changed |= EnsureCommand(hooks, "beforeSubmitPrompt", HookCommand("beforeSubmitPrompt"));
            changed |= EnsureCommand(hooks, "stop", HookCommand("stop"));
            if (!changed)
                return false;

            if (original != null)
            {
                string backup = path + ".biubiu-backup";
                File.WriteAllText(backup, original, new UTF8Encoding(false));
            }
            File.WriteAllText(path, serializer.Serialize(document), new UTF8Encoding(false));
            return true;
        }

        static Dictionary<string, object> GetHooks(Dictionary<string, object> document, HookInstallResult result)
        {
            object hooksValue;
            if (!document.TryGetValue("hooks", out hooksValue) || hooksValue == null)
            {
                Dictionary<string, object> created = new Dictionary<string, object>();
                document["hooks"] = created;
                return created;
            }

            Dictionary<string, object> hooks = hooksValue as Dictionary<string, object>;
            if (hooks == null)
            {
                result.Ok = false;
                result.Error = "hooks.json could not be parsed, so it was left unchanged";
            }
            return hooks;
        }

        static bool EnsureCommand(Dictionary<string, object> hooks, string eventName, string command)
        {
            ArrayList list = EnsureList(hooks, eventName);
            for (int i = 0; i < list.Count; i++)
            {
                Dictionary<string, object> item = list[i] as Dictionary<string, object>;
                if (item == null)
                    continue;
                object existing;
                if (item.TryGetValue("command", out existing) && existing != null
                    && existing.ToString().IndexOf(ScriptFileName, StringComparison.OrdinalIgnoreCase) >= 0)
                    return false;
            }

            Dictionary<string, object> entry = new Dictionary<string, object>();
            entry["command"] = command;
            entry["failClosed"] = false;
            list.Add(entry);
            return true;
        }

        static ArrayList EnsureList(Dictionary<string, object> hooks, string eventName)
        {
            object existing;
            ArrayList list = new ArrayList();
            if (hooks.TryGetValue(eventName, out existing) && existing is IList)
            {
                foreach (object item in (IList)existing)
                    list.Add(item);
            }
            hooks[eventName] = list;
            return list;
        }

        static string NormalizeNewlines(string text)
        {
            if (text == null)
                return "";
            return text.Replace("\r\n", "\n").Replace("\r", "\n");
        }
    }
}
