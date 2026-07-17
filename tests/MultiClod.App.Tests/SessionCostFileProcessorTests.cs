using MultiClod.App.Costs;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace MultiClod.App.Tests;

public sealed class SessionCostFileProcessorTests
{
    [Test]
    public async Task ProcessFile_FileDoesNotExist_ReturnsEmpty()
    {
        var scratchDir = CreateScratchDirectory();
        try
        {
            var result = SessionCostFileProcessor.ProcessFile(Path.Combine(scratchDir, "nope.jsonl"));

            await Assert.That(result).IsEmpty();
        }
        finally
        {
            DeleteScratchDirectory(scratchDir);
        }
    }

    [Test]
    public async Task ProcessFile_NewFile_ComputesCostAndWritesSidecar()
    {
        var scratchDir = CreateScratchDirectory();
        try
        {
            var jsonlPath = Path.Combine(scratchDir, "abc.jsonl");
            File.WriteAllText(jsonlPath, AssistantLine("claude-opus-4-8", inputTokens: 1_000_000, outputTokens: 1_000_000) + "\n");

            var result = SessionCostFileProcessor.ProcessFile(jsonlPath);

            await Assert.That(result.ContainsKey("claude-opus-4-8")).IsTrue();
            await Assert.That(result["claude-opus-4-8"]).IsEqualTo(5m + 25m);

            var sidecarPath = Path.Combine(scratchDir, "abc.mc.json");
            await Assert.That(File.Exists(sidecarPath)).IsTrue();
        }
        finally
        {
            DeleteScratchDirectory(scratchDir);
        }
    }

    [Test]
    public async Task ProcessFile_UnchangedMtime_TrustsSidecarWithoutReparsing()
    {
        var scratchDir = CreateScratchDirectory();
        try
        {
            var jsonlPath = Path.Combine(scratchDir, "abc.jsonl");
            File.WriteAllText(jsonlPath, AssistantLine("claude-opus-4-8", 100, 100) + "\n");
            var actualMtime = File.GetLastWriteTimeUtc(jsonlPath);

            // A sidecar claiming the file was already fully read, with a cost value that could not
            // possibly have come from the real 100/100-token line above - if ProcessFile trusts the
            // mtime match and skips reparsing, this bogus value comes back unchanged.
            var sidecarPath = SessionCostCacheFile.SidecarPathFor(jsonlPath);
            SessionCostCacheFile.Save(sidecarPath, new SessionCostCacheFile
            {
                LastReadOffset = new FileInfo(jsonlPath).Length,
                SourceLastWriteUtc = actualMtime,
                ModelCostsUsd = new Dictionary<string, decimal?> { ["claude-opus-4-8"] = 999.99m },
            });

            var result = SessionCostFileProcessor.ProcessFile(jsonlPath);

            await Assert.That(result["claude-opus-4-8"]).IsEqualTo(999.99m);
        }
        finally
        {
            DeleteScratchDirectory(scratchDir);
        }
    }

    [Test]
    public async Task ProcessFile_AppendedLine_IncrementallyAddsToExistingTotal()
    {
        var scratchDir = CreateScratchDirectory();
        try
        {
            var jsonlPath = Path.Combine(scratchDir, "abc.jsonl");
            File.WriteAllText(jsonlPath, AssistantLine("claude-opus-4-8", 1_000_000, 0) + "\n"); // $5

            var first = SessionCostFileProcessor.ProcessFile(jsonlPath);
            await Assert.That(first["claude-opus-4-8"]).IsEqualTo(5m);

            File.AppendAllText(jsonlPath, AssistantLine("claude-opus-4-8", 1_000_000, 0) + "\n"); // another $5

            var second = SessionCostFileProcessor.ProcessFile(jsonlPath);
            await Assert.That(second["claude-opus-4-8"]).IsEqualTo(10m);
        }
        finally
        {
            DeleteScratchDirectory(scratchDir);
        }
    }

    [Test]
    public async Task ProcessFile_FileReplacedShorter_FullyReparsesFromScratch()
    {
        var scratchDir = CreateScratchDirectory();
        try
        {
            var jsonlPath = Path.Combine(scratchDir, "abc.jsonl");
            File.WriteAllText(jsonlPath, AssistantLine("claude-opus-4-8", 1_000_000, 0) + "\n" + AssistantLine("claude-opus-4-8", 1_000_000, 0) + "\n");
            var first = SessionCostFileProcessor.ProcessFile(jsonlPath);
            await Assert.That(first["claude-opus-4-8"]).IsEqualTo(10m);

            // Simulate Claude Code replacing the transcript file (e.g. /clear) with a shorter one.
            File.WriteAllText(jsonlPath, AssistantLine("claude-haiku-4-5", 1_000_000, 0) + "\n"); // $1

            var second = SessionCostFileProcessor.ProcessFile(jsonlPath);

            await Assert.That(second.ContainsKey("claude-opus-4-8")).IsFalse();
            await Assert.That(second["claude-haiku-4-5"]).IsEqualTo(1m);
        }
        finally
        {
            DeleteScratchDirectory(scratchDir);
        }
    }

    [Test]
    public async Task ProcessFile_TrulyUnknownModel_RecordsNullCost()
    {
        var scratchDir = CreateScratchDirectory();
        try
        {
            var jsonlPath = Path.Combine(scratchDir, "abc.jsonl");
            File.WriteAllText(jsonlPath, AssistantLine("definitely-not-a-real-model", 100, 100) + "\n");

            var result = SessionCostFileProcessor.ProcessFile(jsonlPath);

            await Assert.That(result.ContainsKey("definitely-not-a-real-model")).IsTrue();
            await Assert.That(result["definitely-not-a-real-model"]).IsNull();
        }
        finally
        {
            DeleteScratchDirectory(scratchDir);
        }
    }

    [Test]
    public async Task ProcessFile_PreviouslyUnknownModelNowKnown_SelfHealsWithFullReparse()
    {
        var scratchDir = CreateScratchDirectory();
        try
        {
            var jsonlPath = Path.Combine(scratchDir, "abc.jsonl");
            File.WriteAllText(jsonlPath, AssistantLine("claude-opus-4-8", 1_000_000, 1_000_000) + "\n"); // $30 if priced
            var actualMtime = File.GetLastWriteTimeUtc(jsonlPath);

            // Sidecar claims this file was already fully read, with "claude-opus-4-8" recorded as
            // unknown/null at the time - even though it's actually a known model (and mtime still
            // matches, which would normally short-circuit to "nothing changed").
            var sidecarPath = SessionCostCacheFile.SidecarPathFor(jsonlPath);
            SessionCostCacheFile.Save(sidecarPath, new SessionCostCacheFile
            {
                LastReadOffset = new FileInfo(jsonlPath).Length,
                SourceLastWriteUtc = actualMtime,
                ModelCostsUsd = new Dictionary<string, decimal?> { ["claude-opus-4-8"] = null },
            });

            var result = SessionCostFileProcessor.ProcessFile(jsonlPath);

            await Assert.That(result["claude-opus-4-8"]).IsEqualTo(30m);
        }
        finally
        {
            DeleteScratchDirectory(scratchDir);
        }
    }

    private static string AssistantLine(string model, long inputTokens, long outputTokens) =>
        "{\"type\":\"assistant\",\"timestamp\":\"2026-07-16T20:53:27.143Z\",\"message\":{\"model\":\""
        + model + "\",\"usage\":{\"input_tokens\":" + inputTokens + ",\"output_tokens\":" + outputTokens
        + ",\"cache_read_input_tokens\":0,\"cache_creation_input_tokens\":0}}}";

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
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
