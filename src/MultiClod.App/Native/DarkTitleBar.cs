using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace MultiClod.App.Native;

/// <summary>
/// Switches a window's native title bar to Windows' own dark chrome (DWMWA_USE_IMMERSIVE_DARK_MODE)
/// - WPF has no managed API for this, since the title bar is drawn by DWM in the non-client area,
/// outside anything a Window's own XAML controls. Every Window in the app (MainWindow,
/// RenameDialog, ImportSessionWindow, AddSessionDialog, SplashWindow) calls Apply from its own
/// constructor so none of them show the stock white title bar against the app's otherwise all-dark
/// UI.
/// </summary>
internal static class DarkTitleBar
{
    // 20 (DWMWA_USE_IMMERSIVE_DARK_MODE) is the attribute id on Windows 10 20H1+ and Windows 11;
    // 19 is what the same feature used on Windows 10 builds before 20H1. Trying 20 first and
    // falling back to 19 on failure covers both without needing an OS version check.
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaUseImmersiveDarkModeBefore20H1 = 19;

    internal static void Apply(Window window)
    {
        // WindowInteropHelper.Handle is IntPtr.Zero until the window has a real hwnd, which for a
        // freshly-constructed Window (the only place every call site here calls this from) isn't
        // until SourceInitialized fires.
        window.SourceInitialized += (sender, _) => ApplyToHandle(new WindowInteropHelper((Window)sender!).Handle);
    }

    private static void ApplyToHandle(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        var useDarkMode = 1;
        if (NativeMethods.DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref useDarkMode, sizeof(int)) != 0)
        {
            NativeMethods.DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkModeBefore20H1, ref useDarkMode, sizeof(int));
        }
    }

    private static class NativeMethods
    {
        [DllImport("dwmapi.dll")]
        public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int valueSize);
    }
}
