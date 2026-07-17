using MultiClod.App.SessionLog.Tree;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace MultiClod.App.Tests;

// Reuses TreeGraphBuilderTests' internal jsonl-line/fixture helpers (ToolUseLine, WriteSubagent,
// etc.) rather than duplicating them - these tests exercise TreeLayoutEngine on graphs produced by
// the real TreeGraphBuilder, since the two are meant to run back-to-back (see SessionTreeView).
public sealed class TreeLayoutEngineTests
{
    [Test]
    public async Task Layout_ForegroundSpawn_ChildPinnedToSpawnRowAndReturnRightAfterChildTail()
    {
        var root = CreateScratchDirectory();
        try
        {
            var mainPath = Path.Combine(root, "main.jsonl");
            File.WriteAllLines(
                mainPath,
                [TreeGraphBuilderTests.ToolUseLine("toolu_1", "Agent"), TreeGraphBuilderTests.ToolResultLine("toolu_1", "done")]);

            var sessionDir = Path.Combine(root, "session");
            TreeGraphBuilderTests.WriteSubagent(
                sessionDir, "agent-a", "toolu_1", parentAgentId: null, spawnDepth: 1, [TreeGraphBuilderTests.AssistantTextLine("working")]);

            var agents = TreeGraphBuilder.Build(mainPath, sessionDir, showAllEvents: false);
            var graph = TreeLayoutEngine.Layout(agents, TreeLayoutEngine.DefaultMetrics);

            var main = graph.MainSession;
            var child = graph.Agents.Single(a => a.AgentId == "agent-a");
            var spawnBox = main.Boxes.Single(b => b.Kind == BoxKind.SubagentSpawn);
            var returnBox = main.Boxes.Single(b => b.Kind == BoxKind.SubagentReturn);

            // Child's first box shares the spawn box's row (placed to its right via column).
            await Assert.That(child.Boxes[0].Row).IsEqualTo(spawnBox.Row);

            // No wasted gap: the return lands exactly one row below the child's tail, not further.
            await Assert.That(returnBox.Row).IsEqualTo(child.LastRow + 1);

            AssertNoTwoBoxesShareAGridCell(graph);
        }
        finally
        {
            DeleteScratchDirectory(root);
        }
    }

    [Test]
    public async Task Layout_BackgroundSpawn_ParentEntriesOccupyRowsBesideChildAndOnlyReturnIsPushedDown()
    {
        var root = CreateScratchDirectory();
        try
        {
            var mainPath = Path.Combine(root, "main.jsonl");
            File.WriteAllLines(
                mainPath,
                [
                    TreeGraphBuilderTests.ToolUseLine("toolu_1", "Agent"),
                    TreeGraphBuilderTests.UserTextLine("keep working"),
                    TreeGraphBuilderTests.UserTextLine("still working"),
                    TreeGraphBuilderTests.ToolResultLine("toolu_1", "done"),
                ]);

            var sessionDir = Path.Combine(root, "session");
            TreeGraphBuilderTests.WriteSubagent(
                sessionDir, "agent-a", "toolu_1", parentAgentId: null, spawnDepth: 1, [TreeGraphBuilderTests.AssistantTextLine("working")]);

            var agents = TreeGraphBuilder.Build(mainPath, sessionDir, showAllEvents: false);
            var graph = TreeLayoutEngine.Layout(agents, TreeLayoutEngine.DefaultMetrics);

            var main = graph.MainSession;
            var child = graph.Agents.Single(a => a.AgentId == "agent-a");
            var spawnBox = main.Boxes.Single(b => b.Kind == BoxKind.SubagentSpawn);
            var returnBox = main.Boxes.Single(b => b.Kind == BoxKind.SubagentReturn);
            var entries = main.Boxes.Where(b => b.Kind == BoxKind.Entry).OrderBy(b => b.Row).ToList();

            // The two "keep/still working" entries occupy the rows immediately after the spawn -
            // right beside the child's own (single) row, not skipped or pushed past it.
            await Assert.That(entries[0].Row).IsEqualTo(spawnBox.Row + 1);
            await Assert.That(entries[1].Row).IsEqualTo(spawnBox.Row + 2);

            // Only the return is forced to wait for the child's tail.
            await Assert.That(returnBox.Row).IsEqualTo(Math.Max(entries[1].Row + 1, child.LastRow + 1));

            AssertNoTwoBoxesShareAGridCell(graph);
        }
        finally
        {
            DeleteScratchDirectory(root);
        }
    }

    [Test]
    public async Task Layout_NestedSubagent_GetsColumnOneStepRightOfItsParent()
    {
        var root = CreateScratchDirectory();
        try
        {
            var mainPath = Path.Combine(root, "main.jsonl");
            File.WriteAllLines(
                mainPath,
                [TreeGraphBuilderTests.ToolUseLine("toolu_1", "Agent"), TreeGraphBuilderTests.ToolResultLine("toolu_1", "done")]);

            var sessionDir = Path.Combine(root, "session");
            TreeGraphBuilderTests.WriteSubagent(
                sessionDir,
                "agent-a",
                "toolu_1",
                parentAgentId: null,
                spawnDepth: 1,
                [TreeGraphBuilderTests.ToolUseLine("toolu_2", "Agent"), TreeGraphBuilderTests.ToolResultLine("toolu_2", "done")]);
            TreeGraphBuilderTests.WriteSubagent(
                sessionDir, "agent-b", "toolu_2", parentAgentId: "agent-a", spawnDepth: 2, [TreeGraphBuilderTests.AssistantTextLine("deep work")]);

            var agents = TreeGraphBuilder.Build(mainPath, sessionDir, showAllEvents: false);
            var graph = TreeLayoutEngine.Layout(agents, TreeLayoutEngine.DefaultMetrics);

            var main = graph.MainSession;
            var child = graph.Agents.Single(a => a.AgentId == "agent-a");
            var grandchild = graph.Agents.Single(a => a.AgentId == "agent-b");

            await Assert.That(main.Column).IsEqualTo(0);
            await Assert.That(child.Column).IsEqualTo(1);
            await Assert.That(grandchild.Column).IsEqualTo(2);

            AssertNoTwoBoxesShareAGridCell(graph);
        }
        finally
        {
            DeleteScratchDirectory(root);
        }
    }

    [Test]
    public async Task Layout_SequentialNonOverlappingSubagents_ReuseTheSameLane()
    {
        var root = CreateScratchDirectory();
        try
        {
            var mainPath = Path.Combine(root, "main.jsonl");
            File.WriteAllLines(
                mainPath,
                [
                    TreeGraphBuilderTests.ToolUseLine("toolu_1", "Agent"),
                    TreeGraphBuilderTests.ToolResultLine("toolu_1", "done"),
                    TreeGraphBuilderTests.ToolUseLine("toolu_2", "Agent"),
                    TreeGraphBuilderTests.ToolResultLine("toolu_2", "done"),
                ]);

            var sessionDir = Path.Combine(root, "session");
            TreeGraphBuilderTests.WriteSubagent(
                sessionDir, "agent-a", "toolu_1", parentAgentId: null, spawnDepth: 1, [TreeGraphBuilderTests.AssistantTextLine("a")]);
            TreeGraphBuilderTests.WriteSubagent(
                sessionDir, "agent-b", "toolu_2", parentAgentId: null, spawnDepth: 1, [TreeGraphBuilderTests.AssistantTextLine("b")]);

            var agents = TreeGraphBuilder.Build(mainPath, sessionDir, showAllEvents: false);
            var graph = TreeLayoutEngine.Layout(agents, TreeLayoutEngine.DefaultMetrics);

            var agentA = graph.Agents.Single(a => a.AgentId == "agent-a");
            var agentB = graph.Agents.Single(a => a.AgentId == "agent-b");

            // agent-a finishes (LastRow) strictly before agent-b starts (FirstRow) - non-overlapping,
            // so lane packing must reuse one column rather than widening the canvas.
            await Assert.That(agentA.LastRow).IsLessThan(agentB.FirstRow);
            await Assert.That(agentA.Column).IsEqualTo(agentB.Column);

            AssertNoTwoBoxesShareAGridCell(graph);
        }
        finally
        {
            DeleteScratchDirectory(root);
        }
    }

    [Test]
    public async Task Layout_ConcurrentSiblingSubagents_GetAdjacentLanes()
    {
        var root = CreateScratchDirectory();
        try
        {
            var mainPath = Path.Combine(root, "main.jsonl");

            // agent-a is spawned first and runs across 3 rows; agent-b is spawned immediately after
            // (still "inside" agent-a's row span) - the two lanes must genuinely overlap.
            File.WriteAllLines(
                mainPath,
                [
                    TreeGraphBuilderTests.ToolUseLine("toolu_1", "Agent"),
                    TreeGraphBuilderTests.ToolUseLine("toolu_2", "Agent"),
                    TreeGraphBuilderTests.ToolResultLine("toolu_2", "done"),
                    TreeGraphBuilderTests.ToolResultLine("toolu_1", "done"),
                ]);

            var sessionDir = Path.Combine(root, "session");
            TreeGraphBuilderTests.WriteSubagent(
                sessionDir,
                "agent-a",
                "toolu_1",
                parentAgentId: null,
                spawnDepth: 1,
                [
                    TreeGraphBuilderTests.AssistantTextLine("a1"),
                    TreeGraphBuilderTests.AssistantTextLine("a2"),
                    TreeGraphBuilderTests.AssistantTextLine("a3"),
                ]);
            TreeGraphBuilderTests.WriteSubagent(
                sessionDir, "agent-b", "toolu_2", parentAgentId: null, spawnDepth: 1, [TreeGraphBuilderTests.AssistantTextLine("b1")]);

            var agents = TreeGraphBuilder.Build(mainPath, sessionDir, showAllEvents: false);
            var graph = TreeLayoutEngine.Layout(agents, TreeLayoutEngine.DefaultMetrics);

            var agentA = graph.Agents.Single(a => a.AgentId == "agent-a");
            var agentB = graph.Agents.Single(a => a.AgentId == "agent-b");

            var overlaps = agentB.FirstRow <= agentA.LastRow && agentA.FirstRow <= agentB.LastRow;
            await Assert.That(overlaps).IsTrue();
            await Assert.That(agentA.Column).IsNotEqualTo(agentB.Column);

            AssertNoTwoBoxesShareAGridCell(graph);
        }
        finally
        {
            DeleteScratchDirectory(root);
        }
    }

    private static void AssertNoTwoBoxesShareAGridCell(TreeGraph graph)
    {
        var seen = new HashSet<(int Column, int Row)>();
        foreach (var box in graph.Boxes)
        {
            if (!seen.Add((box.Column, box.Row)))
            {
                throw new InvalidOperationException($"Two boxes share the same grid cell ({box.Column},{box.Row}).");
            }
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
