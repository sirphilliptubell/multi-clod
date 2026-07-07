using System.Runtime.InteropServices;

namespace MultiClod.App.Native;

/// <summary>
/// Fires <see cref="Pressed"/> whenever Shift+Delete is keyed down anywhere on this thread's
/// windows - including inside the embedded terminal's native hwnd, which owns real Win32 keyboard
/// focus once a session pane is focused and therefore never lets a WPF KeyDown routed event reach
/// the Tree (see MainWindow.OnTreeKeyDown). A thread-scoped WH_KEYBOARD hook intercepts the
/// keystroke before GetMessage hands it to whichever window's WndProc would otherwise consume it,
/// so Shift+Delete reliably reaches the app regardless of which control currently has focus. Swallows
/// the keystroke (never forwarded to the focused window) so the terminal doesn't also act on it.
/// </summary>
internal sealed class ShiftDeleteHook : IDisposable
{
    private const int WH_KEYBOARD = 2;
    private const int VK_SHIFT = 0x10;
    private const int VK_DELETE = 0x2E;
    private const int TransitionStateBit = 1 << 31;

    private readonly NativeMethods.HookProc hookProc;
    private readonly IntPtr hookHandle;

    public ShiftDeleteHook(Action onPressed)
    {
        // Stored as a field, not a lambda passed directly to SetWindowsHookEx - the delegate must
        // stay alive for the hook's entire lifetime, otherwise the GC can collect it out from under
        // the unmanaged callback the next time Delete is pressed.
        this.hookProc = (code, wParam, lParam) =>
        {
            if (code >= 0 && (int)wParam == VK_DELETE && (lParam.ToInt64() & TransitionStateBit) == 0
                && (NativeMethods.GetKeyState(VK_SHIFT) & 0x8000) != 0)
            {
                onPressed();
                return new IntPtr(1);
            }

            return NativeMethods.CallNextHookEx(this.hookHandle, code, wParam, lParam);
        };

        this.hookHandle = NativeMethods.SetWindowsHookEx(WH_KEYBOARD, this.hookProc, IntPtr.Zero, NativeMethods.GetCurrentThreadId());
    }

    public void Dispose()
    {
        NativeMethods.UnhookWindowsHookEx(this.hookHandle);
    }

    private static class NativeMethods
    {
        public delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, int dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        public static extern IntPtr CallNextHookEx(IntPtr hhk, int code, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern short GetKeyState(int nVirtKey);

        [DllImport("kernel32.dll")]
        public static extern int GetCurrentThreadId();
    }
}
