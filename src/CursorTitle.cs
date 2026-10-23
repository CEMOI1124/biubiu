using System;
using System.Collections.Generic;

namespace Biubiu
{
    public static class CursorTitle
    {
        const string AppSuffix = " - Cursor";

        public static string ResolveProject(string title, IList<string> knownFolderNames)
        {
            string body = WorkspaceBody(title);
            if (body == null)
                return null;

            string known = MatchKnownFolder(body, knownFolderNames);
            if (known != null)
                return known;

            int split = body.IndexOf(" - ", StringComparison.Ordinal);
            if (split < 0)
                return body;
            return body.Substring(split + 3);
        }

        public static string WorkspaceBody(string title)
        {
            if (string.IsNullOrEmpty(title))
                return null;

            string trimmed = title.Trim();
            if (!trimmed.EndsWith(AppSuffix, StringComparison.Ordinal))
                return null;

            string body = trimmed.Substring(0, trimmed.Length - AppSuffix.Length).Trim();
            if (body.Length == 0)
                return null;

            if (body[0] == '\u25CF' || body[0] == '*')
                body = body.Substring(1).TrimStart();

            if (body.Length == 0)
                return null;
            return body;
        }

        static string MatchKnownFolder(string body, IList<string> knownFolderNames)
        {
            if (knownFolderNames == null)
                return null;

            string best = null;
            for (int i = 0; i < knownFolderNames.Count; i++)
            {
                string name = knownFolderNames[i];
                if (string.IsNullOrEmpty(name))
                    continue;
                if (body.Equals(name, StringComparison.OrdinalIgnoreCase)
                    || body.EndsWith(" - " + name, StringComparison.OrdinalIgnoreCase))
                {
                    if (best == null || name.Length > best.Length)
                        best = name;
                }
            }
            return best;
        }
    }
}
