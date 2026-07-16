using MultiClod.Terminal.Abstractions;
using MultiClod.Terminal.ConPty;

namespace MultiClod.Terminal.Wpf;

/// <summary>
/// Composes a ConPTY-backed connection with a WpfTerminalPane into a full ISessionHost. This is
/// the WPF-flavored session host; a future WebView2-backed host would be a sibling class reusing
/// the same ConPtyConnection.
/// </summary>
public sealed class WpfSessionHost : ISessionHost
{
    private ConPtyConnection? connection;
    private bool disposed;

    public WpfSessionHost()
    {
        this.Pane = new WpfTerminalPane();

        // Forwarded 1:1 so callers only ever need to hold the host, not reach into Pane directly.
        this.Pane.CloseRequested += (sender, e) => this.CloseRequested?.Invoke(sender, e);
    }

    public event EventHandler<SessionState>? StateChanged;

    public event EventHandler? CloseRequested;

    public event EventHandler<string>? TitleChanged;

    public ITerminalPane Pane { get; }

    public SessionState State { get; private set; } = SessionState.NotStarted;

    public int? LastExitCode { get; private set; }

    public string LastOutputTail { get; private set; } = string.Empty;

    public void Start(TerminalLaunchOptions options)
    {
        this.SetState(SessionState.Starting);

        this.connection = new ConPtyConnection(options);
        this.connection.Exited += this.OnConnectionExited;
        this.connection.TitleChanged += this.OnConnectionTitleChanged;

        try
        {
            // WpfTerminalControl's own Connection setter (TerminalContainer.Connection) calls
            // Start() on whatever connection is assigned to it - do not call connection.Start()
            // again here, or the process gets spawned twice (and the first one leaks).
            this.Pane.Attach(this.connection);
            this.SetState(SessionState.Running);
        }
        catch (InvalidOperationException)
        {
            this.SetState(SessionState.Faulted);
            throw;
        }
    }

    public void Stop()
    {
        if (this.connection is not null)
        {
            this.connection.Exited -= this.OnConnectionExited;
            this.connection.TitleChanged -= this.OnConnectionTitleChanged;
        }

        this.Pane.Dispose();
        this.SetState(SessionState.Exited);
    }

    public void Dispose()
    {
        if (this.disposed)
        {
            return;
        }

        this.disposed = true;
        this.Stop();
    }

    private void OnConnectionExited(object? sender, ProcessExitedEventArgs e)
    {
        this.LastExitCode = e.ExitCode;
        this.LastOutputTail = e.OutputTail;
        this.SetState(e.ExitCode == 0 ? SessionState.Exited : SessionState.Faulted);
    }

    private void OnConnectionTitleChanged(object? sender, string title)
    {
        this.TitleChanged?.Invoke(this, title);
    }

    private void SetState(SessionState state)
    {
        this.State = state;
        this.StateChanged?.Invoke(this, state);
    }
}
