namespace MultiClod.App.Persistence;

/// <summary>
/// The root of window-layout.json: MainWindow's last position/size/maximized state and the
/// tree column's width. Left/Top are nullable so a first-run (no saved layout yet) leaves the
/// window's placement up to WPF's own default instead of forcing it to (0,0).
/// </summary>
public sealed class WindowLayout
{
    public double? Left { get; init; }

    public double? Top { get; init; }

    public double Width { get; init; } = 1000;

    public double Height { get; init; } = 600;

    public bool IsMaximized { get; init; }

    public double TreeColumnWidth { get; init; } = 220;
}
