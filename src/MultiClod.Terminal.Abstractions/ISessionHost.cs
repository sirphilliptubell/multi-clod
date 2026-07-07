namespace MultiClod.Terminal.Abstractions;

/// <summary>
/// Owns one session's full lifecycle: the pty connection, the visual pane, and the state
/// transitions between them.
/// </summary>
public interface ISessionHost : IDisposable
{
    ITerminalPane Pane { get; }

    SessionState State { get; }

    event EventHandler<SessionState> StateChanged;

    event EventHandler? CloseRequested;

    event EventHandler<string> TitleChanged;

    void Start(TerminalLaunchOptions options);

    void Stop();
}
