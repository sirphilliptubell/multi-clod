using Microsoft.Win32.SafeHandles;
using static MultiClod.Terminal.ConPty.Native.PseudoConsoleApi;

namespace MultiClod.Terminal.ConPty;

/// <summary>
/// A pipe used to talk to the pseudoconsole, as described in
/// https://learn.microsoft.com/en-us/windows/console/creating-a-pseudoconsole-session. We have two
/// instances of this class per connection: one for input, one for output. Ported from Microsoft's
/// own samples/ConPTY/MiniTerm (microsoft/terminal repo).
/// </summary>
internal sealed class PseudoConsolePipe : IDisposable
{
    public readonly SafeFileHandle ReadSide;
    public readonly SafeFileHandle WriteSide;

    public PseudoConsolePipe()
    {
        if (!CreatePipe(out this.ReadSide, out this.WriteSide, IntPtr.Zero, 0))
        {
            throw new InvalidOperationException("Failed to create pipe.");
        }
    }

    public void Dispose()
    {
        this.ReadSide?.Dispose();
        this.WriteSide?.Dispose();
    }
}
