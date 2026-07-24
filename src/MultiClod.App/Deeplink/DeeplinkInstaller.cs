using Microsoft.Win32;
using System.IO;
using System.Security;

namespace MultiClod.App.Deeplink;

/// <summary>
/// Self-installs the multi-clod:// URL protocol registration, pointing directly at this process's
/// own exe (Environment.ProcessPath - Velopack's stable "current\" path, so it survives version
/// updates without re-registering). Called once from App.OnStartup, only by the process that wins
/// the single-instance mutex (see App.xaml.cs) - mirrors FromHereInstaller.Install's timing and
/// best-effort posture exactly (every step swallows registry failures rather than surfacing them).
///
/// Debug and Release builds intentionally share ONE registration (no separate scheme name, unlike
/// FromHereProtocol's separate Debug Mutex/PipeName/DataDirectoryName) - a URI scheme can only be
/// owned by one HKCU registration at a time, so whichever build launched most recently wins. This
/// is an accepted tradeoff, not an oversight: it keeps testing simple (one scheme to target) at the
/// cost of a Debug build transiently stealing real multi-clod:// links away from an installed
/// Release copy until the Release copy next runs.
/// </summary>
internal static class DeeplinkInstaller
{
    private const string SchemeKeyPath = @"Software\Classes\" + DeeplinkProtocol.UriScheme;
    private const string DisplayText = "URL:Multi-Clod Session Log Protocol";

    public static void Install()
    {
        var exePath = Environment.ProcessPath;
        if (exePath is null)
        {
            return;
        }

        try
        {
            using var schemeKey = Registry.CurrentUser.CreateSubKey(SchemeKeyPath);
            if (schemeKey is null)
            {
                return;
            }

            schemeKey.SetValue(null, DisplayText);
            schemeKey.SetValue("URL Protocol", string.Empty);

            using var iconKey = schemeKey.CreateSubKey("DefaultIcon");
            iconKey?.SetValue(null, $"{exePath},0");

            using var commandKey = schemeKey.CreateSubKey(@"shell\open\command");
            commandKey?.SetValue(null, $"\"{exePath}\" \"%1\"");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
        }
    }
}
