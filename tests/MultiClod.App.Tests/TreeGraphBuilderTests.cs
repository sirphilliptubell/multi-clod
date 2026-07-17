using MultiClod.App.SessionLog.Tree;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace MultiClod.App.Tests;

public sealed class TreeGraphBuilderTests
{
    [Test]
    public async Task Build_ForegroundSpawn_ChildLinkedWithSpawnAndReturnBox()
    {
        var root = CreateScratchDirectory();
        try
        {
            var mainPath = Path.Combine(root, "main.jsonl");
            File.WriteAllLines(mainPath, [ToolUseLine("toolu_1", "Agent"), ToolResultLine("toolu_1", "done")]);

            var sessionDir = Path.Combine(root, "session");
            WriteSubagent(sessionDir, "agent-a", "toolu_1", parentAgentId: null, spawnDepth: 1, [AssistantTextLine("working")]);

            var agents = TreeGraphBuilder.Build(mainPath, sessionDir, showAllEvents: false);

            var main = agents.Single(a => a.AgentId == AgentNode.MainAgentId);
            var child = agents.Single(a => a.AgentId == "agent-a");

            await Assert.That(child.IsOrphan).IsFalse();
            await Assert.That(child.Depth).IsEqualTo(1);
            await Assert.That(child.SpawnBox).IsNotNull();
            await Assert.That(child.SpawnBox!.Kind).IsEqualTo(BoxKind.SubagentSpawn);
            await Assert.That(child.ReturnBox).IsNotNull();
            await Assert.That(child.ReturnBox!.Kind).IsEqualTo(BoxKind.SubagentReturn);

            // Foreground: the parent logs nothing between the spawn and the result it collects.
            await Assert.That(string.Join(",", main.Boxes.Select(b => b.Kind))).IsEqualTo("SubagentSpawn,SubagentReturn");
        }
        finally
        {
            DeleteScratchDirectory(root);
        }
    }

    [Test]
    public async Task Build_BackgroundSpawn_MainEntriesInterleaveBetweenSpawnAndReturn()
    {
        var root = CreateScratchDirectory();
        try
        {
            var mainPath = Path.Combine(root, "main.jsonl");
            File.WriteAllLines(
                mainPath,
                [ToolUseLine("toolu_1", "Agent"), UserTextLine("keep working"), UserTextLine("still working"), ToolResultLine("toolu_1", "done")]);

            var sessionDir = Path.Combine(root, "session");
            WriteSubagent(sessionDir, "agent-a", "toolu_1", parentAgentId: null, spawnDepth: 1, [AssistantTextLine("working")]);

            var agents = TreeGraphBuilder.Build(mainPath, sessionDir, showAllEvents: false);
            var main = agents.Single(a => a.AgentId == AgentNode.MainAgentId);

            await Assert.That(string.Join(",", main.Boxes.Select(b => b.Kind))).IsEqualTo("SubagentSpawn,Entry,Entry,SubagentReturn");
        }
        finally
        {
            DeleteScratchDirectory(root);
        }
    }

    [Test]
    public async Task Build_SubagentMissingMetaJson_IsOrphanAtDepthOne()
    {
        var root = CreateScratchDirectory();
        try
        {
            var mainPath = Path.Combine(root, "main.jsonl");
            File.WriteAllLines(mainPath, [UserTextLine("hello")]);

            var sessionDir = Path.Combine(root, "session");
            var subagentsDir = Path.Combine(sessionDir, "subagents");
            Directory.CreateDirectory(subagentsDir);
            File.WriteAllLines(Path.Combine(subagentsDir, "agent-orphan.jsonl"), [AssistantTextLine("lost")]);

            var agents = TreeGraphBuilder.Build(mainPath, sessionDir, showAllEvents: false);
            var orphan = agents.Single(a => a.AgentId == "agent-orphan");

            await Assert.That(orphan.IsOrphan).IsTrue();
            await Assert.That(orphan.Depth).IsEqualTo(1);
            await Assert.That(orphan.SpawnBox).IsNull();
        }
        finally
        {
            DeleteScratchDirectory(root);
        }
    }

    [Test]
    public async Task Build_NestedSubagent_ResolvesDepthTwoFromParentChain()
    {
        var root = CreateScratchDirectory();
        try
        {
            var mainPath = Path.Combine(root, "main.jsonl");
            File.WriteAllLines(mainPath, [ToolUseLine("toolu_1", "Agent"), ToolResultLine("toolu_1", "done")]);

            var sessionDir = Path.Combine(root, "session");
            WriteSubagent(
                sessionDir,
                "agent-a",
                "toolu_1",
                parentAgentId: null,
                spawnDepth: 1,
                [ToolUseLine("toolu_2", "Agent"), ToolResultLine("toolu_2", "done")]);
            WriteSubagent(sessionDir, "agent-b", "toolu_2", parentAgentId: "agent-a", spawnDepth: 2, [AssistantTextLine("deep work")]);

            var agents = TreeGraphBuilder.Build(mainPath, sessionDir, showAllEvents: false);
            var child = agents.Single(a => a.AgentId == "agent-a");
            var grandchild = agents.Single(a => a.AgentId == "agent-b");

            await Assert.That(grandchild.IsOrphan).IsFalse();
            await Assert.That(grandchild.Depth).IsEqualTo(2);
            await Assert.That(grandchild.SpawnBox!.Owner).IsEqualTo(child);
        }
        finally
        {
            DeleteScratchDirectory(root);
        }
    }

    [Test]
    public async Task Build_ChildStillRunning_HasSpawnBoxButNoReturnBox()
    {
        var root = CreateScratchDirectory();
        try
        {
            var mainPath = Path.Combine(root, "main.jsonl");
            File.WriteAllLines(mainPath, [ToolUseLine("toolu_1", "Agent")]);

            var sessionDir = Path.Combine(root, "session");
            WriteSubagent(sessionDir, "agent-a", "toolu_1", parentAgentId: null, spawnDepth: 1, [AssistantTextLine("still going")]);

            var agents = TreeGraphBuilder.Build(mainPath, sessionDir, showAllEvents: false);
            var child = agents.Single(a => a.AgentId == "agent-a");

            await Assert.That(child.SpawnBox).IsNotNull();
            await Assert.That(child.ReturnBox).IsNull();
        }
        finally
        {
            DeleteScratchDirectory(root);
        }
    }

    [Test]
    public async Task Build_ShowAllEventsFalse_SkipsSystemMetaRows()
    {
        var root = CreateScratchDirectory();
        try
        {
            var mainPath = Path.Combine(root, "main.jsonl");
            File.WriteAllLines(mainPath, ["{\"type\":\"some-system-event\"}", UserTextLine("hello")]);

            var sessionDir = Path.Combine(root, "session");

            var hidden = TreeGraphBuilder.Build(mainPath, sessionDir, showAllEvents: false);
            var shown = TreeGraphBuilder.Build(mainPath, sessionDir, showAllEvents: true);

            await Assert.That(hidden.Single(a => a.AgentId == AgentNode.MainAgentId).Boxes).Count().IsEqualTo(1);
            await Assert.That(shown.Single(a => a.AgentId == AgentNode.MainAgentId).Boxes).Count().IsEqualTo(2);
        }
        finally
        {
            DeleteScratchDirectory(root);
        }
    }

    internal static string ToolUseLine(string toolUseId, string toolName) =>
        $"{{\"type\":\"assistant\",\"message\":{{\"role\":\"assistant\",\"content\":[{{\"type\":\"tool_use\",\"id\":\"{toolUseId}\",\"name\":\"{toolName}\",\"input\":{{}}}}]}}}}";

    internal static string ToolResultLine(string toolUseId, string content) =>
        $"{{\"type\":\"user\",\"message\":{{\"role\":\"user\",\"content\":[{{\"type\":\"tool_result\",\"tool_use_id\":\"{toolUseId}\",\"content\":\"{content}\"}}]}}}}";

    internal static string UserTextLine(string text) =>
        $"{{\"type\":\"user\",\"message\":{{\"role\":\"user\",\"content\":\"{text}\"}}}}";

    internal static string AssistantTextLine(string text) =>
        $"{{\"type\":\"assistant\",\"message\":{{\"role\":\"assistant\",\"content\":[{{\"type\":\"text\",\"text\":\"{text}\"}}]}}}}";

    internal static void WriteSubagent(string sessionDir, string agentFileStem, string toolUseId, string? parentAgentId, int spawnDepth, string[] lines)
    {
        var subagentsDir = Path.Combine(sessionDir, "subagents");
        Directory.CreateDirectory(subagentsDir);
        File.WriteAllLines(Path.Combine(subagentsDir, $"{agentFileStem}.jsonl"), lines);

        var parentJson = parentAgentId is null ? "null" : $"\"{parentAgentId}\"";
        File.WriteAllText(
            Path.Combine(subagentsDir, $"{agentFileStem}.meta.json"),
            $"{{\"agentType\":\"general-purpose\",\"description\":\"desc\",\"toolUseId\":\"{toolUseId}\",\"parentAgentId\":{parentJson},\"spawnDepth\":{spawnDepth}}}");
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
