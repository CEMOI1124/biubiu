using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace Biubiu
{
    static class Program
    {
        public static string StartupWarning;

        [STAThread]
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            EnsureHook();
            if (args == null || args.Length == 0 || args[0] == "ui")
                return Show();
            if (args[0] == "list")
                return List();
            if (args[0] == "activate")
                return Activate(args);
            if (args[0] == "tasks")
                return Tasks();
            Usage();
            return 1;
        }

        static int Show()
        {
            HideOwnConsole();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
            return 0;
        }

        static void HideOwnConsole()
        {
            IntPtr hwnd = GetConsoleWindow();
            if (hwnd == IntPtr.Zero)
                return;
            uint owner;
            GetWindowThreadProcessId(hwnd, out owner);
            if (owner == GetCurrentProcessId())
                ShowWindow(hwnd, 0);
        }

        static void EnsureHook()
        {
            try
            {
                string script = HookInstaller.ReadEmbeddedScript();
                HookInstallResult result = HookInstaller.Install(HookInstaller.DefaultCursorDirectory(), script);
                if (!result.Ok)
                {
                    StartupWarning = result.Error;
                    Console.Error.WriteLine("hook: " + result.Error);
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("hook: " + ex.Message);
            }
        }

        static int Tasks()
        {
            TaskLoadResult loaded = TaskBoard.Load(TaskBoard.DefaultPath(), DateTimeOffset.Now);
            if (!loaded.Ok)
            {
                Console.Error.WriteLine(loaded.Error);
                return 1;
            }
            for (int i = 0; i < loaded.Tasks.Count; i++)
                WriteTask(loaded.Tasks[i]);
            return 0;
        }

        static int List()
        {
            List<CursorWindowInfo> windows = CursorWindows.List();
            for (int i = 0; i < windows.Count; i++)
                WriteWindow(windows[i]);
            return 0;
        }

        static int Activate(string[] args)
        {
            string project = null;
            long hwnd = 0;
            bool hasHwnd = false;

            for (int i = 1; i < args.Length; i++)
            {
                string arg = args[i];
                if (arg == "--project" && i + 1 < args.Length)
                {
                    project = args[++i];
                    continue;
                }
                if (arg == "--hwnd" && i + 1 < args.Length)
                {
                    if (!long.TryParse(args[++i], out hwnd))
                    {
                        Console.Error.WriteLine("invalid hwnd");
                        return 1;
                    }
                    hasHwnd = true;
                    continue;
                }
                Usage();
                return 1;
            }

            if (!hasHwnd && string.IsNullOrEmpty(project))
            {
                Usage();
                return 1;
            }

            List<CursorWindowInfo> windows = CursorWindows.List();
            CursorWindowInfo target = null;
            if (hasHwnd)
            {
                target = FindHwnd(windows, hwnd);
                if (target == null)
                {
                    Console.Error.WriteLine("not found");
                    return 3;
                }
            }
            else
            {
                List<CursorWindowInfo> matches = FindProject(windows, project);
                if (matches.Count == 0)
                {
                    Console.Error.WriteLine("not found");
                    return 3;
                }
                if (matches.Count > 1)
                {
                    Console.Error.WriteLine("ambiguous");
                    for (int i = 0; i < matches.Count; i++)
                        WriteWindow(matches[i]);
                    return 2;
                }
                target = matches[0];
            }

            ActivateResult result = WindowActivator.Activate(target.Hwnd);
            if (!result.IsForeground)
            {
                Console.Error.WriteLine("miss\t" + result.Hwnd + "\t" + result.ForegroundHwnd);
                return 4;
            }

            Console.WriteLine("ok\t" + target.Hwnd + "\t" + target.Project);
            return 0;
        }

        static CursorWindowInfo FindHwnd(List<CursorWindowInfo> windows, long hwnd)
        {
            for (int i = 0; i < windows.Count; i++)
            {
                if (windows[i].Hwnd == hwnd)
                    return windows[i];
            }
            return null;
        }

        static List<CursorWindowInfo> FindProject(List<CursorWindowInfo> windows, string project)
        {
            List<CursorWindowInfo> matches = new List<CursorWindowInfo>();
            for (int i = 0; i < windows.Count; i++)
            {
                if (string.Equals(windows[i].Project, project, StringComparison.OrdinalIgnoreCase))
                    matches.Add(windows[i]);
            }
            return matches;
        }

        static void WriteWindow(CursorWindowInfo info)
        {
            Console.WriteLine(info.Hwnd + "\t" + info.Project + "\t" + (info.WorkspacePath ?? "") + "\t" + info.Title);
        }

        static void WriteTask(TaskRecord task)
        {
            Console.WriteLine(task.Status
                + "\t" + WorkspaceCatalog.FolderName(task.WorkspacePath)
                + "\t" + task.Summary
                + "\t" + task.WorkspacePath
                + "\t" + task.ConversationId
                + "\t" + task.UpdatedAt.ToString("o"));
        }

        static void Usage()
        {
            Console.Error.WriteLine("usage: Biubiu.exe");
            Console.Error.WriteLine("       Biubiu.exe list");
            Console.Error.WriteLine("       Biubiu.exe activate --project <name>");
            Console.Error.WriteLine("       Biubiu.exe activate --hwnd <id>");
            Console.Error.WriteLine("       Biubiu.exe tasks");
        }

        [DllImport("kernel32.dll")]
        static extern IntPtr GetConsoleWindow();

        [DllImport("kernel32.dll")]
        static extern uint GetCurrentProcessId();

        [DllImport("user32.dll")]
        static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

        [DllImport("user32.dll")]
        static extern bool ShowWindow(IntPtr hwnd, int command);
    }
}
