namespace MultiClod.Terminal.Abstractions;

/// <summary>
/// Owns one session's full lifecycle: the pty connection, the visual pane, and the state
/// transitions between them.
/// </summary>
public interface ISessionHost : IDisposable
{
    ITerminalPane Pane { get; }

    SessionState State { get; }

    /// <summary>
    /// The connected process's exit code, as of the most recent StateChanged transition into
    /// Exited or Faulted - null before that first happens. See LastOutputTail for what it printed
    /// right before exiting.
    /// </summary>
    int? LastExitCode { get; }

    /// <summary>
    /// The tail of raw terminal output captured right before the most recent Exited/Faulted
    /// transition - see ConPtyConnection.OutputTail. Empty (not null) before that first happens.
    /// </summary>
    string LastOutputTail { get; }

    event EventHandler<SessionState> StateChanged;

    event EventHandler? CloseRequested;

    event EventHandler<string> TitleChanged;

    void Start(TerminalLaunchOptions options);

    void Stop();
}
