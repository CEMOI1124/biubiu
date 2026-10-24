using System;
using System.IO;

namespace Biubiu
{
    public static class GitBranch
    {
        public static string Read(string workspacePath)
        {
            try
            {
                if (string.IsNullOrEmpty(workspacePath))
                    return null;
                string root = TaskBoard.NormalizeWorkspacePath(workspacePath);
                if (!Directory.Exists(root))
                    return null;

                string gitPath = Path.Combine(root, ".git");
                string gitDir = gitPath;
                if (File.Exists(gitPath))
                {
                    gitDir = ResolveGitDir(root, File.ReadAllText(gitPath));
                    if (string.IsNullOrEmpty(gitDir))
                        return null;
                }
                else if (!Directory.Exists(gitPath))
                {
                    return null;
                }

                string headPath = Path.Combine(gitDir, "HEAD");
                if (!File.Exists(headPath))
                    return null;
                return NameFromHead(File.ReadAllText(headPath));
            }
            catch (IOException)
            {
                return null;
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }
        }

        public static string NameFromHead(string head)
        {
            if (string.IsNullOrEmpty(head))
                return null;
            string text = head.Trim();
            if (text.StartsWith("ref:", StringComparison.Ordinal))
            {
                string refName = text.Substring(4).Trim();
                if (refName.StartsWith("refs/heads/", StringComparison.Ordinal))
                    return refName.Substring("refs/heads/".Length);
                return refName;
            }
            if (text.Length >= 7)
                return text.Substring(0, 7);
            return text;
        }

        static string ResolveGitDir(string workspace, string gitFile)
        {
            if (string.IsNullOrEmpty(gitFile))
                return null;
            string[] lines = gitFile.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                const string marker = "gitdir:";
                if (!line.StartsWith(marker, StringComparison.OrdinalIgnoreCase))
                    continue;
                string target = line.Substring(marker.Length).Trim();
                if (target.Length == 0)
                    return null;
                if (!Path.IsPathRooted(target))
                    target = Path.GetFullPath(Path.Combine(workspace, target));
                return target;
            }
            return null;
        }
    }
}
