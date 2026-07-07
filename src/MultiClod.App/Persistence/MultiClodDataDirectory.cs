using System.IO;
using MultiClod.Shared;

namespace MultiClod.App.Persistence;

/// <summary>
/// The app's single dotfile-style data root, ~/.multi-clod - deliberately not %LOCALAPPDATA%,
/// to match ~/.claude itself. Shared by <see cref="SessionStore"/> (sessions.json) and the
/// "from here" feature (from-here-tool/, from-here-config.json) so both agree on one location
/// without either hardcoding it.
/// </summary>
public static class MultiClodDataDirectory
{
    public static string Root { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), FromHereProtocol.DataDirectoryName);
}
