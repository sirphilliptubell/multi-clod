using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace MultiClod.App.Native;

/// <summary>
/// Forces a window to the foreground even when Windows would normally refuse a bare
/// SetForegroundWindow call from a thread that isn't already active - notably true for a freshly
/// Show()n WPF window under Remote Desktop, where the brief foreground-grant a process gets at
/// launch can expire before MainWindow actually appears (worse on an unoptimized Debug build,
/// which takes longer to JIT its way to the first paint). Only called once, from MainWindow's
/// Loaded handler.
/// </summary>
internal static class ForceForeground
{
    internal static void Apply(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        var foregroundHwnd = NativeMethods.GetForegroundWindow();
        if (foregroundHwnd == hwnd)
        {
            return;
        }

        var thisThreadId = NativeMethods.GetCurrentThreadId();
        var foregroundThreadId = NativeMethods.GetWindowThreadProcessId(foregroundHwnd, IntPtr.Zero);

        // Borrowing the foreground thread's input state is what actually grants
        // SetForegroundWindow permission here - without it, Windows silently ignores the call (or
        // just flashes the taskbar icon) for any thread that isn't already the active one.
        var attached = foregroundThreadId != thisThreadId
            && NativeMethods.AttachThreadInput(thisThreadId, foregroundThreadId, true);
        try
        {
            NativeMethods.SetForegroundWindow(hwnd);
            NativeMethods.BringWindowToTop(hwnd);
        }
        finally
        {
            if (attached)
            {
                NativeMethods.AttachThreadInput(thisThreadId, foregroundThreadId, false);
            }
        }
    }

    private static class NativeMethods
    {
        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool BringWindowToTop(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, IntPtr processId);

        [DllImport("kernel32.dll")]
        public static extern uint GetCurrentThreadId();

        [DllImport("user32.dll")]
        public static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);
    }
}
