namespace MultiClod.Terminal.Abstractions;

public sealed class ProcessExitedEventArgs : EventArgs
{
    public ProcessExitedEventArgs(int exitCode)
    {
        this.ExitCode = exitCode;
    }

    public int ExitCode { get; }
}
