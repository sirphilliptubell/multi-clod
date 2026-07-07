using System.Windows;

namespace MultiClod.Terminal.Abstractions;

/// <summary>
/// Hides which terminal-rendering technology backs a session's visual surface (currently
/// WpfTerminalControl; a WebView2+xterm.js implementation is the documented fallback in
/// todo-features.md). Nothing outside a pane implementation and its owning ISessionHost should
/// reference the concrete rendering technology directly.
/// </summary>
public interface ITerminalPane : IDisposable
{
    FrameworkElement View { get; }

    string Title { set; }

    event EventHandler? CloseRequested;

    void Attach(IPtyConnection connection);

    void ApplyTheme(TerminalPaneTheme theme);

    void Focus();
}
