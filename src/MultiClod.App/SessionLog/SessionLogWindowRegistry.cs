using System.Windows;
using MultiClod.App.Costs;

namespace MultiClod.App.SessionLog;

/// <summary>
/// Tracks open SessionLogWindows keyed by SessionNodeViewModel.Id - the stable tree identity, not
/// ClaudeSessionId, which can change under a running session via /clear or an in-CLI /resume.
/// Reopening the log for a session that already has one open focuses it instead of duplicating;
/// different sessions get independent windows.
/// </summary>
public sealed class SessionLogWindowRegistry
{
    private readonly Dictionary<Guid, SessionLogWindow> openWindows = new();

    public void ShowOrFocus(SessionNodeViewModel session, Window owner, SessionCostMonitorService costMonitor)
    {
        if (this.openWindows.TryGetValue(session.Id, out var existing))
        {
            if (existing.WindowState == WindowState.Minimized)
            {
                existing.WindowState = WindowState.Normal;
            }

            existing.Activate();
            return;
        }

        var window = new SessionLogWindow(session, costMonitor) { Owner = owner };
        window.Closed += (_, _) => this.openWindows.Remove(session.Id);
        this.openWindows[session.Id] = window;
        window.Show();
    }

    public void CloseAll()
    {
        foreach (var window in this.openWindows.Values.ToList())
        {
            window.Close();
        }
    }
}
