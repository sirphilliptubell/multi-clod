using System.Runtime.InteropServices;

namespace MultiClod.App.Native;

/// <summary>
/// Fires the undo/redo callbacks for Ctrl+Z/Ctrl+Y anywhere on this thread's windows - including
/// inside the embedded terminal's native hwnd, which owns real Win32 keyboard focus once a session
/// pane is focused and therefore never lets a WPF KeyDown routed event reach anything (same
/// rationale as <see cref="ShiftDeleteHook"/>). A thread-scoped WH_KEYBOARD hook intercepts the
/// keystroke before GetMessage hands it to whichever window's WndProc would otherwise consume it.
/// Passes the keystroke through untouched (via CallNextHookEx) whenever <paramref name="isModalDialogOpen"/>
/// (checked live, not captured) reports true, so Ctrl+Z/Ctrl+Y still undo typed text in a focused
/// TextBox inside RenameDialog/AddSessionDialog/ImportSessionWindow instead of hitting the tree's
/// undo stack.
/// </summary>
internal sealed class UndoRedoShortcutHook : IDisposable
{
    private const int WH_KEYBOARD = 2;
    private const int VK_CONTROL = 0x11;
    private const int VK_Z = 0x5A;
    private const int VK_Y = 0x59;
    private const int TransitionStateBit = 1 << 31;

    private readonly NativeMethods.HookProc hookProc;
    private readonly IntPtr hookHandle;

    public UndoRedoShortcutHook(Func<bool> isModalDialogOpen, Action onUndo, Action onRedo)
    {
        // Stored as a field, not a lambda passed directly to SetWindowsHookEx - the delegate must
        // stay alive for the hook's entire lifetime, otherwise the GC can collect it out from under
        // the unmanaged callback the next time Ctrl+Z/Ctrl+Y is pressed.
        this.hookProc = (code, wParam, lParam) =>
        {
            var isKeyDown = (lParam.ToInt64() & TransitionStateBit) == 0;
            var isCtrlDown = (NativeMethods.GetKeyState(VK_CONTROL) & 0x8000) != 0;

            if (code >= 0 && isKeyDown && isCtrlDown && !isModalDialogOpen())
            {
                if ((int)wParam == VK_Z)
                {
                    onUndo();
                    return new IntPtr(1);
                }

                if ((int)wParam == VK_Y)
                {
                    onRedo();
                    return new IntPtr(1);
                }
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
