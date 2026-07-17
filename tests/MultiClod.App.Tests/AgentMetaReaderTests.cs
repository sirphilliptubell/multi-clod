using MultiClod.App.SessionLog.Tree;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace MultiClod.App.Tests;

public sealed class AgentMetaReaderTests
{
    [Test]
    public async Task TryRead_ValidMetaJson_ReturnsPopulatedAgentMeta()
    {
        var root = CreateScratchDirectory();
        try
        {
            var agentPath = Path.Combine(root, "agent-abc123.jsonl");
            File.WriteAllText(agentPath, "{}\n");
            File.WriteAllText(
                Path.Combine(root, "agent-abc123.meta.json"),
                "{\"agentType\":\"general-purpose\",\"description\":\"Do a thing\"," +
                "\"toolUseId\":\"toolu_01ABC\",\"parentAgentId\":\"agentXYZ\",\"spawnDepth\":2}");

            var meta = AgentMetaReader.TryRead(agentPath);

            await Assert.That(meta).IsNotNull();
            await Assert.That(meta!.AgentType).IsEqualTo("general-purpose");
            await Assert.That(meta.Description).IsEqualTo("Do a thing");
            await Assert.That(meta.ToolUseId).IsEqualTo("toolu_01ABC");
            await Assert.That(meta.ParentAgentId).IsEqualTo("agentXYZ");
            await Assert.That(meta.SpawnDepth).IsEqualTo(2);
        }
        finally
        {
            DeleteScratchDirectory(root);
        }
    }

    [Test]
    public async Task TryRead_MissingFile_ReturnsNull()
    {
        var root = CreateScratchDirectory();
        try
        {
            var meta = AgentMetaReader.TryRead(Path.Combine(root, "agent-nope.jsonl"));

            await Assert.That(meta).IsNull();
        }
        finally
        {
            DeleteScratchDirectory(root);
        }
    }

    [Test]
    public async Task TryRead_MalformedJson_ReturnsNull()
    {
        var root = CreateScratchDirectory();
        try
        {
            var agentPath = Path.Combine(root, "agent-bad.jsonl");
            File.WriteAllText(Path.Combine(root, "agent-bad.meta.json"), "not json");

            var meta = AgentMetaReader.TryRead(agentPath);

            await Assert.That(meta).IsNull();
        }
        finally
        {
            DeleteScratchDirectory(root);
        }
    }

    // A meta.json missing "toolUseId" can't be linked back to any spawn box - TreeGraphBuilder
    // treats a null result here as the signal to render this agent as an orphan.
    [Test]
    public async Task TryRead_MissingToolUseId_ReturnsNull()
    {
        var root = CreateScratchDirectory();
        try
        {
            var agentPath = Path.Combine(root, "agent-orphan.jsonl");
            File.WriteAllText(Path.Combine(root, "agent-orphan.meta.json"), "{\"agentType\":\"general-purpose\"}");

            var meta = AgentMetaReader.TryRead(agentPath);

            await Assert.That(meta).IsNull();
        }
        finally
        {
            DeleteScratchDirectory(root);
        }
    }

    [Test]
    public async Task TryRead_MissingParentAgentIdAndSpawnDepth_DefaultsToDepthOneNullParent()
    {
        var root = CreateScratchDirectory();
        try
        {
            var agentPath = Path.Combine(root, "agent-depth1.jsonl");
            File.WriteAllText(Path.Combine(root, "agent-depth1.meta.json"), "{\"toolUseId\":\"toolu_01XYZ\"}");

            var meta = AgentMetaReader.TryRead(agentPath);

            await Assert.That(meta).IsNotNull();
            await Assert.That(meta!.ParentAgentId).IsNull();
            await Assert.That(meta.SpawnDepth).IsEqualTo(1);
        }
        finally
        {
            DeleteScratchDirectory(root);
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
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
