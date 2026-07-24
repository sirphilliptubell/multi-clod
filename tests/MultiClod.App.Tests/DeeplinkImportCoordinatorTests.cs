using System.IO.Compression;
using MultiClod.App.Deeplink;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace MultiClod.App.Tests;

public sealed class DeeplinkImportCoordinatorTests
{
    [Test]
    public async Task ImportAsync_LocalZipSource_ExtractsAndClassifies()
    {
        var scratch = CreateScratchDirectory();
        try
        {
            var sessionId = Guid.NewGuid();
            var zipPath = Path.Combine(scratch, "source.zip");
            WriteZip(zipPath, new Dictionary<string, string> { [$"{sessionId}.jsonl"] = "content" });

            var importDirectory = Path.Combine(scratch, "import");
            var contents = await DeeplinkImportCoordinator.ImportAsync(zipPath, importDirectory, progress: null, CancellationToken.None);

            await Assert.That(contents.Sessions).Count().IsEqualTo(1);
            await Assert.That(contents.Sessions[0].SessionId).IsEqualTo(sessionId);

            // The temp zip is cleaned up - only "content\" and the ".complete" marker remain.
            await Assert.That(File.Exists(Path.Combine(importDirectory, "source.zip"))).IsFalse();
            await Assert.That(File.Exists(Path.Combine(importDirectory, ".complete"))).IsTrue();
        }
        finally
        {
            DeleteScratchDirectory(scratch);
        }
    }

    [Test]
    public async Task ImportAsync_SecondCallForSameDirectory_SkipsRefetchViaCompleteMarker()
    {
        var scratch = CreateScratchDirectory();
        try
        {
            var sessionId = Guid.NewGuid();
            var zipPath = Path.Combine(scratch, "source.zip");
            WriteZip(zipPath, new Dictionary<string, string> { [$"{sessionId}.jsonl"] = "content" });

            var importDirectory = Path.Combine(scratch, "import");
            await DeeplinkImportCoordinator.ImportAsync(zipPath, importDirectory, progress: null, CancellationToken.None);

            // Deleting the source zip proves the second call never re-fetches - it must be
            // reading the ".complete" marker and going straight to classification.
            File.Delete(zipPath);

            var contents = await DeeplinkImportCoordinator.ImportAsync(zipPath, importDirectory, progress: null, CancellationToken.None);

            await Assert.That(contents.Sessions).Count().IsEqualTo(1);
        }
        finally
        {
            DeleteScratchDirectory(scratch);
        }
    }

    [Test]
    public async Task ImportAsync_ZipWithNoRecognizableContent_ThrowsDeeplinkImportException()
    {
        var scratch = CreateScratchDirectory();
        try
        {
            var zipPath = Path.Combine(scratch, "empty.zip");
            WriteZip(zipPath, new Dictionary<string, string>());

            var importDirectory = Path.Combine(scratch, "import");

            var threw = false;
            try
            {
                await DeeplinkImportCoordinator.ImportAsync(zipPath, importDirectory, progress: null, CancellationToken.None);
            }
            catch (DeeplinkImportException)
            {
                threw = true;
            }

            await Assert.That(threw).IsTrue();
        }
        finally
        {
            DeleteScratchDirectory(scratch);
        }
    }

    private static void WriteZip(string zipPath, IReadOnlyDictionary<string, string> entries)
    {
        using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        foreach (var (name, content) in entries)
        {
            var entry = archive.CreateEntry(name);
            using var writer = new StreamWriter(entry.Open());
            writer.Write(content);
        }
    }

    private static string CreateScratchDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "MultiClod.App.Tests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteScratchDirectory(string path)
    {
        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
