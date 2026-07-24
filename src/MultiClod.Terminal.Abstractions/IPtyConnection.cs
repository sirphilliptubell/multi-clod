namespace MultiClod.Terminal.Abstractions;

/// <summary>
/// A live connection to a child process running behind a pseudoconsole. Implementations own the
/// process and pipe lifecycle; callers only see text in/out and resize/exit notifications.
/// </summary>
public interface IPtyConnection : IDisposable
{
    event EventHandler<string> OutputReceived;

    event EventHandler<ProcessExitedEventArgs> Exited;

    // Raised when the child process sets its terminal title via an OSC 0/2 escape sequence
    // (e.g. `ESC ] 0 ; <title> BEL`). Purely observational - it does not affect what's forwarded
    // via OutputReceived.
    event EventHandler<string> TitleChanged;

    // Raised when the child process's own screen output contains the literal text Claude Code's
    // CLI prints when a turn is cancelled (see ConPtyConnection.ScanForInterruptMarker) - a
    // best-effort text scan, not a structured signal like TitleChanged, since Claude Code's Stop
    // hook never fires for a user-interrupted (e.g. Escape) turn. Purely observational, same as
    // TitleChanged.
    event EventHandler InterruptDetected;

    void Start();

    void WriteInput(string data);

    void Resize(uint rows, uint columns);
}
