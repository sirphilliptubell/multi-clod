using System.IO;
using System.Security.Cryptography;
using System.Text;
using MultiClod.App.Persistence;

namespace MultiClod.App.Deeplink;

/// <summary>
/// Owns the on-disk layout for extracted deeplink imports under
/// ~/.multi-clod/deeplink-imports/&lt;hash&gt;/, split into "content\" (the zip's actual extracted
/// files - what ImportZipClassifier scans) and a sibling ".complete" marker + "source.zip" temp
/// download, both OUTSIDE "content\" so neither one is ever mistaken for part of the imported
/// session's own files. Contents are wiped on every app launch (SweepOnLaunch, called once from
/// App.OnStartup before any extraction could start this run), not on window close - so
/// "Explore To" and copying files out of an imported session keep working for the lifetime of the
/// running app, at the cost of needing a fresh download the next time the app starts.
/// </summary>
internal static class DeeplinkImportStorage
{
    public static string Root { get; } = Path.Combine(MultiClodDataDirectory.Root, "deeplink-imports");

    // Deterministic (not a random temp name) - re-clicking the same link during the same app run
    // always resolves to the same folder, so DeeplinkImportCoordinator's on-disk cache-hit check
    // (the ".complete" marker) works without a separate lookup table.
    public static string GetImportDirectory(string normalizedSourceKey)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalizedSourceKey)));
        return Path.Combine(Root, hash);
    }

    public static string GetContentDirectory(string importDirectory) => Path.Combine(importDirectory, "content");

    public static string GetTempZipPath(string importDirectory) => Path.Combine(importDirectory, "source.zip");

    public static string GetCompleteMarkerPath(string importDirectory) => Path.Combine(importDirectory, ".complete");

    public static void SweepOnLaunch()
    {
        try
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
