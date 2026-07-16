using System.IO;

namespace MultiClod.App;

/// <summary>
/// Shared by MainWindow (the "from here" launch path) and AddSessionDialog (auto-filling the
/// session name field from whatever folder is currently chosen).
/// </summary>
internal static class FolderDisplayName
{
    internal static string GetName(string directory)
    {
        var trimmed = directory.TrimEnd('\\', '/');
        var name = Path.GetFileName(trimmed);
        return string.IsNullOrEmpty(name) ? trimmed : name;
    }
}
