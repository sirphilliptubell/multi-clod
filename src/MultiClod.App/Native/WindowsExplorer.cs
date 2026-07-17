using System.Diagnostics;
using System.IO;

namespace MultiClod.App.Native;

// Centralizes explorer.exe launches behind "Explore to" context-menu actions across the app, so
// the auto-select behavior (highlighting the specific file, not just opening its folder) stays
// consistent everywhere a file path is exposed via right-click.
internal static class WindowsExplorer
{
    // Windows paths can never contain '"', so building the raw Arguments string this way is safe.
    public static void OpenAndSelect(string filePath)
    {
        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{filePath}\"") { UseShellExecute = true });
    }

    public static void OpenFolder(string folder)
    {
        Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
    }

    // Walks up from a not-yet-created path (e.g. an @import that hasn't been created yet, or a
    // session transcript that hasn't started writing) until it finds a directory that actually
    // exists - the immediate parent (and possibly several levels above that) may not exist yet.
    public static void OpenNearestExistingAncestorFolder(string path)
    {
        var folder = Path.GetDirectoryName(path);
        while (folder is not null && !Directory.Exists(folder))
        {
            folder = Path.GetDirectoryName(folder);
        }

        if (folder is not null)
        {
            OpenFolder(folder);
        }
    }
}
