namespace MultiClod.Terminal.Abstractions;

public sealed class ProcessExitedEventArgs : EventArgs
{
    public ProcessExitedEventArgs(int exitCode, string outputTail)
    {
        this.ExitCode = exitCode;
        this.OutputTail = outputTail;
    }

    public int ExitCode { get; }

    /// <summary>
    /// The last portion of raw terminal output received before the process exited (see
    /// ConPtyConnection's own cap on how much it keeps) - most useful when ExitCode is non-zero,
    /// to see whatever the process itself printed right before dying without needing to catch it
    /// live in the UI before the pane moves on or the window closes.
    /// </summary>
    public string OutputTail { get; }
}
