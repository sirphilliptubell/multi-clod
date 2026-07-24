using System.IO;
using System.IO.Compression;

namespace MultiClod.App.Deeplink;

/// <summary>
/// Extracts a downloaded/read deeplink zip into a destination directory, defending against
/// zip-slip (entries escaping the destination via ".." segments, rooted paths, or a different
/// drive) and decompression bombs (unbounded entry count/size) - mandatory regardless of the
/// "no confirmation prompt before download" UX decision, since any web page can trigger this path.
/// </summary>
internal static class SafeZipExtractor
{
    private const int MaxEntryCount = 5000;
    private const long MaxSingleEntryBytes = 500L * 1024 * 1024;
    private const long MaxTotalUncompressedBytes = 1024L * 1024 * 1024;

    public static void Extract(string zipFilePath, string destinationDirectory)
    {
        Directory.CreateDirectory(destinationDirectory);
        var destinationRoot = Path.GetFullPath(destinationDirectory);
        var destinationRootWithSeparator = destinationRoot.EndsWith(Path.DirectorySeparatorChar)
            ? destinationRoot
            : destinationRoot + Path.DirectorySeparatorChar;

        try
        {
            using var archive = ZipFile.OpenRead(zipFilePath);

            if (archive.Entries.Count > MaxEntryCount)
            {
                throw new DeeplinkImportException($"The zip contains too many entries ({archive.Entries.Count}, max {MaxEntryCount}).");
            }

            var totalUncompressed = 0L;

            foreach (var entry in archive.Entries)
            {
                if (entry.Length > MaxSingleEntryBytes)
                {
                    throw new DeeplinkImportException($"Zip entry '{entry.FullName}' is too large (max {MaxSingleEntryBytes / (1024 * 1024)} MB).");
                }

                totalUncompressed += entry.Length;
                if (totalUncompressed > MaxTotalUncompressedBytes)
                {
                    throw new DeeplinkImportException($"The zip's total uncompressed size exceeds the {MaxTotalUncompressedBytes / (1024 * 1024)} MB limit.");
                }

                // Path.GetFullPath resolves any ".." segments/rooted-path tricks in entry.FullName
                // before the containment check below runs - the check is the actual defense, not
                // Path.Combine's own (platform-dependent) handling of a rooted second argument.
                var destinationPath = Path.GetFullPath(Path.Combine(destinationRoot, entry.FullName));
                if (!destinationPath.StartsWith(destinationRootWithSeparator, StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(destinationPath, destinationRoot, StringComparison.OrdinalIgnoreCase))
                {
                    throw new DeeplinkImportException($"Zip entry '{entry.FullName}' would extract outside the destination folder.");
                }

                // Directory entries have an empty Name (their FullName ends with '/') - no file
                // content to write, just ensure the folder exists.
                if (entry.Name.Length == 0)
                {
                    Directory.CreateDirectory(destinationPath);
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
                entry.ExtractToFile(destinationPath, overwrite: true);
            }
        }
        catch (InvalidDataException ex)
        {
            throw new DeeplinkImportException("The downloaded file is not a valid zip archive.", ex);
        }
    }
}
