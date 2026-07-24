using System.IO;

namespace MultiClod.App.Deeplink;

/// <summary>
/// Orchestrates one deeplink import: fetch (or reuse an already-extracted copy) -> extract ->
/// classify. A ".complete" marker file, written only after a fully successful prior run at the same
/// hashed import directory, lets a re-click of the same link skip straight to classification
/// without re-downloading - independent of whether a viewer window for that source is still open
/// (DeeplinkImportWindowRegistry governs window reuse separately).
/// </summary>
internal static class DeeplinkImportCoordinator
{
    public static async Task<ClassifiedImportContents> ImportAsync(
        string source,
        string importDirectory,
        IProgress<DeeplinkFetchProgress>? progress,
        CancellationToken cancellationToken)
    {
        var contentDirectory = DeeplinkImportStorage.GetContentDirectory(importDirectory);
        var markerPath = DeeplinkImportStorage.GetCompleteMarkerPath(importDirectory);

        if (!File.Exists(markerPath))
        {
            Directory.CreateDirectory(importDirectory);

            var zipPath = DeeplinkImportStorage.GetTempZipPath(importDirectory);
            try
            {
                await DeeplinkSourceFetcher.FetchToFileAsync(source, zipPath, progress, cancellationToken);
                SafeZipExtractor.Extract(zipPath, contentDirectory);
                await File.WriteAllTextAsync(markerPath, DateTime.UtcNow.ToString("O"), cancellationToken);
            }
            catch
            {
                // No ".complete" marker was written, so a retry wouldn't normally re-fetch on its
                // own - but a partially-extracted "content\" from this failed attempt would still
                // be sitting there for the retry to (incorrectly) build on top of. Clear it so a
                // retry starts clean.
                TryDeleteDirectory(contentDirectory);
                throw;
            }
            finally
            {
                // The zip is only a means to extract "content\" - never left behind for
                // classification to trip over, success or failure.
                TryDelete(zipPath);
            }
        }

        var contents = await ImportZipClassifier.ClassifyAsync(contentDirectory, cancellationToken);
        if (!contents.HasContent)
        {
            throw new DeeplinkImportException("The zip didn't contain any recognizable session transcript or other files.");
        }

        return contents;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (IOException)
        {
        }
    }
}
