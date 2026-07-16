using Microsoft.Win32.SafeHandles;
using static MultiClod.Terminal.ConPty.Native.PseudoConsoleApi;

namespace MultiClod.Terminal.ConPty;

/// <summary>
/// Wraps the Win32 pseudoconsole handle. Ported from Microsoft's own samples/ConPTY/MiniTerm
/// (microsoft/terminal repo), with Resize added since MiniTerm's own console window never resizes.
/// </summary>
internal sealed class PseudoConsole : IDisposable
{
    public static readonly IntPtr PseudoConsoleThreadAttribute = (IntPtr)PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE;

    private bool disposed;

    private PseudoConsole(IntPtr handle)
    {
        this.Handle = handle;
    }

    public IntPtr Handle { get; }

    internal static PseudoConsole Create(SafeFileHandle inputReadSide, SafeFileHandle outputWriteSide, int width, int height)
    {
        var createResult = CreatePseudoConsole(
            new COORD { X = (short)width, Y = (short)height },
            inputReadSide,
            outputWriteSide,
            0,
            out IntPtr hPC);
        if (createResult != 0)
        {
            throw new InvalidOperationException("Could not create pseudo console. Error Code " + createResult);
        }

        return new PseudoConsole(hPC);
    }

    // Best-effort, not throw-on-failure - every caller (see TerminalContainer's various resize
    // paths, all the way up through ConPtyConnection.Resize) fires this from layout/window-message
    // callbacks with no way to act on a failure. A resize racing the pseudoconsole/process already
    // tearing down (observed: ERROR_NO_DATA / "the pipe is being closed") is an entirely expected,
    // harmless case - not a bug worth an unhandled exception crashing the whole app over, which is
    // what this used to do unconditionally.
    internal void Resize(int width, int height)
    {
        if (this.disposed)
        {
            return;
        }

        ResizePseudoConsole(this.Handle, new COORD { X = (short)width, Y = (short)height });
    }

    public void Dispose()
    {
        if (this.disposed)
        {
            return;
        }

        this.disposed = true;
        ClosePseudoConsole(this.Handle);
    }
}
