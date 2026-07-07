using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace MultiClod.Terminal.ConPty.Native;

/// <summary>
/// P/Invoke signatures for the Win32 pseudoconsole API. Ported from Microsoft's own
/// samples/ConPTY/MiniTerm (microsoft/terminal repo) - see
/// https://learn.microsoft.com/en-us/windows/console/creating-a-pseudoconsole-session.
/// </summary>
internal static class PseudoConsoleApi
{
    internal const uint PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE = 0x00020016;

    [StructLayout(LayoutKind.Sequential)]
    internal struct COORD
    {
        public short X;
        public short Y;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern int CreatePseudoConsole(COORD size, SafeFileHandle hInput, SafeFileHandle hOutput, uint dwFlags, out IntPtr phPC);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern int ResizePseudoConsole(IntPtr hPC, COORD size);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern int ClosePseudoConsole(IntPtr hPC);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    internal static extern bool CreatePipe(out SafeFileHandle hReadPipe, out SafeFileHandle hWritePipe, IntPtr lpPipeAttributes, int nSize);
}
