using System.Runtime.InteropServices;

namespace MultiClod.App.Native;

/// <summary>
/// Redelivers Left/Right/Up/Down and Ctrl+C/Ctrl+Z/Ctrl+Y key messages straight to whichever
/// embedded terminal hwnd currently owns real Win32 keyboard focus, bypassing WPF's normal
/// dispatch for those keys only - see remarks.
/// </summary>
/// <remarks>
/// KeyboardNavigation.DirectionalNavigation="None" (set on both the Window and
/// WpfTerminalPane's own container) fixes the *severe* form of this bug - arrow keys stealing
/// Win32 focus outright and leaving every subsequent keystroke going nowhere - but WPF's
/// TranslateAccelerator machinery still gets first look at arrow-key WM_KEYDOWN messages pulled
/// off this thread's queue even with that set, and Left/Right can still resolve to the Tree (the
/// terminal's nearest navigable neighbor) and drive its expand/collapse behavior directly,
/// without actually moving focus away - so typing and Up/Down keep working normally while
/// Left/Right silently double as tree commands. The same machinery resolves Ctrl+C/Ctrl+Z/Ctrl+Y
/// against whichever element WPF's FocusManager still considers logically focused (also the
/// Tree, for the same reason) even though real Win32 focus is on the terminal - so those three
/// silently double as tree commands too, instead of reaching the shell (Ctrl+C) or simply typing
/// nothing useful (Ctrl+Z/Ctrl+Y). A thread-scoped WH_KEYBOARD hook (same technique as
/// ShiftDeleteHook) intercepts these keys before GetMessage hands them to anything and, when real
/// Win32 focus is on an embedded terminal, re-sends the exact same message straight to that
/// hwnd's own WndProc (see TerminalContainer's message hook) and swallows the original - the
/// terminal still receives the keystroke, but WPF's translation layer never gets a chance to
/// reinterpret it.
/// </remarks>
internal sealed class TerminalKeyRoutingHook : IDisposable
{
    private const int WH_KEYBOARD = 2;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;
    private const int TransitionStateBit = 1 << 31;
    private const uint GA_ROOT = 2;

    private const int VK_LEFT = 0x25;
    private const int VK_UP = 0x26;
    private const int VK_RIGHT = 0x27;
    private const int VK_DOWN = 0x28;

    private const int VK_CONTROL = 0x11;
    private const int VK_C = 0x43;
    private const int VK_Y = 0x59;
    private const int VK_Z = 0x5A;

    private readonly Func<IntPtr> getMainWindowHwnd;
    private readonly NativeMethods.HookProc hookProc;
    private readonly IntPtr hookHandle;

    public TerminalKeyRoutingHook(Func<IntPtr> getMainWindowHwnd)
    {
        this.getMainWindowHwnd = getMainWindowHwnd;

        // Stored as a field, not a lambda passed directly to SetWindowsHookEx - the delegate must
        // stay alive for the hook's entire lifetime, otherwise the GC can collect it out from
        // under the unmanaged callback the next time a routed key is pressed.
        this.hookProc = (code, wParam, lParam) =>
        {
            if (code >= 0 && IsRoutedKey((int)wParam) && this.TryGetEmbeddedTerminalFocus(out var focused))
            {
                var message = (lParam.ToInt64() & TransitionStateBit) == 0 ? WM_KEYDOWN : WM_KEYUP;
                NativeMethods.SendMessage(focused, message, wParam, lParam);
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

    private bool TryGetEmbeddedTerminalFocus(out IntPtr focused)
    {
        focused = NativeMethods.GetFocus();
        var mainWindow = this.getMainWindowHwnd();

        if (focused == IntPtr.Zero || mainWindow == IntPtr.Zero || focused == mainWindow)
        {
            return false;
        }

        // A terminal's HwndHost is a true WS_CHILD of the main window (see
        // TerminalContainer.BuildWindowCore), so its root ancestor resolves back to the main
        // window's own hwnd. A separate top-level window (RenameDialog, ImportSessionWindow) is
        // its own root, so this never misfires against one of those.
        return NativeMethods.GetAncestor(focused, GA_ROOT) == mainWindow;
    }

    private static bool IsRoutedKey(int vkey)
    {
        if (IsArrowKey(vkey))
        {
            return true;
        }

        // Ctrl+C/Ctrl+Z/Ctrl+Y only count as routed while Ctrl is actually held - unlike the
        // arrow keys, C/Y/Z are also perfectly ordinary characters the terminal needs to receive
        // unmolested (e.g. typing "cyz").
        return vkey is VK_C or VK_Y or VK_Z && (NativeMethods.GetKeyState(VK_CONTROL) & 0x8000) != 0;
    }

    private static bool IsArrowKey(int vkey)
    {
        return vkey is VK_LEFT or VK_UP or VK_RIGHT or VK_DOWN;
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
        public static extern IntPtr GetFocus();

        [DllImport("user32.dll")]
        public static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);

        [DllImport("user32.dll")]
        public static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern short GetKeyState(int nVirtKey);

        [DllImport("kernel32.dll")]
        public static extern int GetCurrentThreadId();
    }
}
