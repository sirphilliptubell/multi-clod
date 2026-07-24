using System.Windows;

namespace MultiClod.App.Deeplink;

/// <summary>
/// Tracks open DeeplinkImportWindows keyed by the normalized source string (DeeplinkSourceKey) -
/// mirrors SessionLogWindowRegistry's keyed reuse-or-create-and-track pattern. Re-triggering the
/// same deeplink focuses its existing window instead of opening a duplicate; a source with no
/// window yet falls through to a fresh fetch/extract/open (see MainWindow.HandleDeeplinkRequest).
/// </summary>
internal sealed class DeeplinkImportWindowRegistry
{
    private readonly Dictionary<string, DeeplinkImportWindow> openWindows = new(StringComparer.Ordinal);

    public bool TryFocus(string key)
    {
        if (!this.openWindows.TryGetValue(key, out var existing))
        {
            return false;
        }

        if (existing.WindowState == WindowState.Minimized)
        {
            existing.WindowState = WindowState.Normal;
        }

        existing.Activate();
        return true;
    }

    public void Register(string key, DeeplinkImportWindow window)
    {
        window.Closed += (_, _) => this.openWindows.Remove(key);
        this.openWindows[key] = window;
    }

    public void CloseAll()
    {
        foreach (var window in this.openWindows.Values.ToList())
        {
            window.Close();
        }
    }
}
