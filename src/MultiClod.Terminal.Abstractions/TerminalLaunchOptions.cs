namespace MultiClod.Terminal.Abstractions;

public sealed class TerminalLaunchOptions
{
    // npm-installed CLIs on Windows are typically .cmd shims; CreateProcess (used by ConPTY)
    // doesn't do the PATH/extension resolution cmd.exe does, so route through cmd.exe by default.
    public string CommandLine { get; init; } = "cmd.exe /c claude";

    public required string WorkingDirectory { get; init; }

    public uint InitialRows { get; init; } = 30;

    public uint InitialColumns { get; init; } = 120;
}
