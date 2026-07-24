using System.IO.Compression;
using MultiClod.App.Deeplink;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace MultiClod.App.Tests;

public sealed class SafeZipExtractorTests
{
    [Test]
    public async Task Extract_ValidZipWithNestedFolders_ExtractsAllEntries()
    {
        var scratch = CreateScratchDirectory();
        try
        {
            var zipPath = Path.Combine(scratch, "source.zip");
            var sessionId = Guid.NewGuid();
            WriteZip(zipPath, new Dictionary<string, string>
            {
                [$"{sessionId}.jsonl"] = "main session content",
                [$"{sessionId}/subagents/agent-abc.jsonl"] = "subagent content",
            });

            var destination = Path.Combine(scratch, "content");
            SafeZipExtractor.Extract(zipPath, destination);

            await Assert.That(File.Exists(Path.Combine(destination, $"{sessionId}.jsonl"))).IsTrue();
            await Assert.That(File.Exists(Path.Combine(destination, sessionId.ToString(), "subagents", "agent-abc.jsonl"))).IsTrue();
            await Assert.That(File.ReadAllText(Path.Combine(destination, $"{sessionId}.jsonl"))).IsEqualTo("main session content");
        }
        finally
        {
            DeleteScratchDirectory(scratch);
        }
    }

    [Test]
    public async Task Extract_ZipSlipEntry_ThrowsAndLeavesDestinationClean()
    {
        var scratch = CreateScratchDirectory();
        try
        {
            var zipPath = Path.Combine(scratch, "malicious.zip");
            WriteZip(zipPath, new Dictionary<string, string>
            {
                ["../escaped.txt"] = "should never land outside destination",
            });

            var destination = Path.Combine(scratch, "content");

            var threw = false;
            try
            {
                SafeZipExtractor.Extract(zipPath, destination);
            }
            catch (DeeplinkImportException)
            {
                threw = true;
            }

            await Assert.That(threw).IsTrue();
            await Assert.That(File.Exists(Path.Combine(scratch, "escaped.txt"))).IsFalse();
        }
        finally
        {
            DeleteScratchDirectory(scratch);
        }
    }

    [Test]
    public async Task Extract_CorruptZip_ThrowsDeeplinkImportException()
    {
        var scratch = CreateScratchDirectory();
        try
        {
            var zipPath = Path.Combine(scratch, "corrupt.zip");
            File.WriteAllText(zipPath, "this is not a zip file");

            var threw = false;
            try
            {
                SafeZipExtractor.Extract(zipPath, Path.Combine(scratch, "content"));
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
