using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Biubiu
{
    public sealed class CursorWindowInfo
    {
        public long Hwnd;
        public int ProcessId;
        public string Project;
        public string WorkspacePath;
        public string Title;
    }

    public static class CursorWindows
    {
        const int DwmwaCloaked = 14;

        public static List<CursorWindowInfo> List()
        {
            WorkspaceCatalog catalog = WorkspaceCatalog.Load();
            List<CursorWindowInfo> found = new List<CursorWindowInfo>();
            EnumWindows(delegate(IntPtr hwnd, IntPtr lParam)
            {
                CursorWindowInfo info = TryRead(hwnd, catalog);
                if (info != null)
                    found.Add(info);
                return true;
            }, IntPtr.Zero);
            return found;
        }

        static CursorWindowInfo TryRead(IntPtr hwnd, WorkspaceCatalog catalog)
        {
            if (!IsWindowVisible(hwnd))
                return null;
            if (IsCloaked(hwnd))
                return null;

            string className = ReadClassName(hwnd);
            if (!string.Equals(className, "Chrome_WidgetWin_1", StringComparison.Ordinal))
                return null;

            string title = ReadTitle(hwnd);
            string project = CursorTitle.ResolveProject(title, catalog.Names);
            if (project == null)
                return null;

            uint pid;
            GetWindowThreadProcessId(hwnd, out pid);
            if (pid == 0 || !IsCursorProcess(pid))
                return null;

            CursorWindowInfo info = new CursorWindowInfo();
            info.Hwnd = hwnd.ToInt64();
            info.ProcessId = (int)pid;
            info.Project = project;
            info.WorkspacePath = catalog.UniquePath(project);
            info.Title = title;
            return info;
        }

        static bool IsCursorProcess(uint pid)
        {
            try
            {
                Process process = Process.GetProcessById((int)pid);
                return string.Equals(process.ProcessName, "Cursor", StringComparison.OrdinalIgnoreCase);
            }
            catch (ArgumentException)
            {
                return false;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        static bool IsCloaked(IntPtr hwnd)
        {
            int cloaked;
            int hr = DwmGetWindowAttribute(hwnd, DwmwaCloaked, out cloaked, sizeof(int));
            return hr == 0 && cloaked != 0;
        }

        static string ReadTitle(IntPtr hwnd)
        {
            int length = GetWindowTextLength(hwnd);
            if (length <= 0)
                return null;
            StringBuilder buffer = new StringBuilder(length + 1);
            GetWindowText(hwnd, buffer, buffer.Capacity);
            return buffer.ToString();
        }

        static string ReadClassName(IntPtr hwnd)
        {
            StringBuilder buffer = new StringBuilder(256);
            int length = GetClassName(hwnd, buffer, buffer.Capacity);
            if (length <= 0)
                return null;
            return buffer.ToString();
        }

        delegate bool EnumProc(IntPtr hwnd, IntPtr lParam);

        [DllImport("user32.dll")]
        static extern bool EnumWindows(EnumProc callback, IntPtr lParam);

        [DllImport("user32.dll")]
        static extern bool IsWindowVisible(IntPtr hwnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int count);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern int GetWindowTextLength(IntPtr hwnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern int GetClassName(IntPtr hwnd, StringBuilder className, int count);

        [DllImport("user32.dll")]
        static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

        [DllImport("dwmapi.dll")]
        static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out int value, int size);
    }
}
