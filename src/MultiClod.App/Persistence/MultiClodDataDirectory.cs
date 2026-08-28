using System.IO;
using MultiClod.Shared;

namespace MultiClod.App.Persistence;

/// <summary>
/// The app's single dotfile-style data root, ~/.multi-clod - deliberately not %LOCALAPPDATA%,
/// to match ~/.claude itself. Shared by <see cref="SessionStore"/> (sessions.json) and the
/// "from here" feature (from-here-tool/, from-here-config.json) so both agree on one location
/// without either hardcoding it.
///
/// Debug and Release both resolve to this same path by default, since dev convenience (seeing
/// your real sessions in a Debug build) outweighs isolation - but that means a manually-launched
/// second instance restores/relaunches whatever the other config already has open. Setting
/// MULTICLOD_DATA_DIR points a one-off instance at a scratch directory instead, for exactly that
/// kind of manual test harness, without changing default behavior for anyone who hasn't set it.
/// </summary>
public static class MultiClodDataDirectory
{
    public static string Root { get; } = Environment.GetEnvironmentVariable("MULTICLOD_DATA_DIR") is { Length: > 0 } overridePath
        ? overridePath
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), FromHereProtocol.DataDirectoryName);
}
