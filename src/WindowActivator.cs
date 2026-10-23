using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace Biubiu
{
    public sealed class ActivateResult
    {
        public long Hwnd;
        public long ForegroundHwnd;
        public bool IsForeground;
    }

    public static class WindowActivator
    {
        const int SwRestore = 9;
        const int SwShow = 5;
        const byte VkMenu = 0x12;
        const uint KeyeventfKeyup = 0x0002;

        public static ActivateResult Activate(long hwndValue)
        {
            ActivateResult result = new ActivateResult();
            result.Hwnd = hwndValue;
            IntPtr hwnd = new IntPtr(hwndValue);
            if (!IsWindow(hwnd))
            {
                result.ForegroundHwnd = GetForegroundWindow().ToInt64();
                return result;
            }

            Focus(hwnd, false);
            if (WaitUntilForeground(hwnd))
            {
                result.IsForeground = true;
                result.ForegroundHwnd = hwndValue;
                return result;
            }

            Focus(hwnd, true);
            result.IsForeground = WaitUntilForeground(hwnd);
            result.ForegroundHwnd = GetForegroundWindow().ToInt64();
            return result;
        }

        static void Focus(IntPtr hwnd, bool tapAlt)
        {
            uint currentThread = GetCurrentThreadId();
            IntPtr foreground = GetForegroundWindow();
            uint foregroundThread = GetWindowThreadProcessId(foreground, IntPtr.Zero);
            uint targetThread = GetWindowThreadProcessId(hwnd, IntPtr.Zero);
            bool attachedForeground = false;
            bool attachedTarget = false;

            if (foregroundThread != 0 && foregroundThread != currentThread)
                attachedForeground = AttachThreadInput(currentThread, foregroundThread, true);
            if (targetThread != 0 && targetThread != currentThread && targetThread != foregroundThread)
                attachedTarget = AttachThreadInput(currentThread, targetThread, true);

            try
            {
                if (IsIconic(hwnd))
                    ShowWindow(hwnd, SwRestore);
                else
                    ShowWindow(hwnd, SwShow);

                BringWindowToTop(hwnd);
                if (tapAlt)
                    keybd_event(VkMenu, 0, 0, UIntPtr.Zero);
                SetForegroundWindow(hwnd);
                SwitchToThisWindow(hwnd, true);
                if (tapAlt)
                    keybd_event(VkMenu, 0, KeyeventfKeyup, UIntPtr.Zero);
            }
            finally
            {
                if (attachedTarget)
                    AttachThreadInput(currentThread, targetThread, false);
                if (attachedForeground)
                    AttachThreadInput(currentThread, foregroundThread, false);
            }
        }

        static bool WaitUntilForeground(IntPtr hwnd)
        {
            for (int i = 0; i < 10; i++)
            {
                if (GetForegroundWindow() == hwnd)
                    return true;
                Thread.Sleep(50);
            }
            return GetForegroundWindow() == hwnd;
        }

        [DllImport("user32.dll")]
        static extern bool SetForegroundWindow(IntPtr hwnd);

        [DllImport("user32.dll")]
        static extern bool ShowWindow(IntPtr hwnd, int command);

        [DllImport("user32.dll")]
        static extern bool IsIconic(IntPtr hwnd);

        [DllImport("user32.dll")]
        static extern bool IsWindow(IntPtr hwnd);

        [DllImport("user32.dll")]
        static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        static extern uint GetWindowThreadProcessId(IntPtr hwnd, IntPtr processId);

        [DllImport("user32.dll")]
        static extern bool AttachThreadInput(uint sourceThread, uint targetThread, bool enabled);

        [DllImport("kernel32.dll")]
        static extern uint GetCurrentThreadId();

        [DllImport("user32.dll")]
        static extern bool BringWindowToTop(IntPtr hwnd);

        [DllImport("user32.dll")]
        static extern void SwitchToThisWindow(IntPtr hwnd, bool altTab);

        [DllImport("user32.dll")]
        static extern void keybd_event(byte virtualKey, byte scanCode, uint flags, UIntPtr extra);
    }
}
