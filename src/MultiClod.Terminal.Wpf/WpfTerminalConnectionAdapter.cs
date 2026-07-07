using MultiClod.Terminal.Abstractions;
using WpfTerminalConnection = Microsoft.Terminal.Wpf.ITerminalConnection;
using WpfTerminalOutputEventArgs = Microsoft.Terminal.Wpf.TerminalOutputEventArgs;

namespace MultiClod.Terminal.Wpf;

/// <summary>
/// Adapts an <see cref="IPtyConnection"/> to the vendored WpfTerminalControl's own
/// ITerminalConnection interface. This is the one seam where our abstraction meets the vendored
/// control's own contract - everything else in this project only knows about IPtyConnection.
/// </summary>
internal sealed class WpfTerminalConnectionAdapter : WpfTerminalConnection
{
    private readonly IPtyConnection inner;

    public WpfTerminalConnectionAdapter(IPtyConnection inner)
    {
        this.inner = inner;
        this.inner.OutputReceived += this.OnOutputReceived;
    }

    public event EventHandler<WpfTerminalOutputEventArgs>? TerminalOutput;

    public void Start()
    {
        this.inner.Start();
    }

    public void WriteInput(string data)
    {
        this.inner.WriteInput(data);
    }

    public void Resize(uint rows, uint columns)
    {
        this.inner.Resize(rows, columns);
    }

    public void Close()
    {
        this.inner.OutputReceived -= this.OnOutputReceived;
        this.inner.Dispose();
    }

    private void OnOutputReceived(object? sender, string data)
    {
        this.TerminalOutput?.Invoke(this, new WpfTerminalOutputEventArgs(data));
    }
}
