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

    internal void Resize(int width, int height)
    {
        var resizeResult = ResizePseudoConsole(this.Handle, new COORD { X = (short)width, Y = (short)height });
        if (resizeResult != 0)
        {
            throw new InvalidOperationException("Could not resize pseudo console. Error Code " + resizeResult);
        }
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
