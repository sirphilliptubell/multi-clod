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

    void Start();

    void WriteInput(string data);

    void Resize(uint rows, uint columns);
}
