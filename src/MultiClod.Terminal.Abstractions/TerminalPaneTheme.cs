using System.Windows.Media;

namespace MultiClod.Terminal.Abstractions;

public sealed class TerminalPaneTheme
{
    public required Color Background { get; init; }

    public required Color Foreground { get; init; }

    public required Color CursorColor { get; init; }

    public required Color SelectionBackground { get; init; }

    public string FontFamily { get; init; } = "Cascadia Mono";

    public short FontSize { get; init; } = 12;
}
