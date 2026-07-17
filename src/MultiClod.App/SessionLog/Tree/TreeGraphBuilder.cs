using System.IO;
using System.Text.Json;
using MultiClod.App.SessionLog.Parsing;
using MultiClod.App.SessionLog.Rendering;

namespace MultiClod.App.SessionLog.Tree;

/// <summary>
/// Builds one Tree snapshot from disk: the main session file plus every subagent file under
/// &lt;sessionDir&gt;/subagents/. Pure and synchronous (no WPF, no live watching, no tailer) -
/// TreeLayoutEngine runs afterward to assign Column/Row/X/Y and connector geometry. See
/// specs/session-log-tree-view.md for the full design rationale behind every decision here.
/// </summary>
internal static class TreeGraphBuilder
{
    public static IReadOnlyList<AgentNode> Build(string mainFilePath, string sessionDir, bool showAllEvents)
    {
        var agents = new List<AgentNode>
        {
            new(AgentNode.MainAgentId, mainFilePath, depth: 0, parentAgentId: null, spawnToolUseId: null),
        };

        var subagentsDir = Path.Combine(sessionDir, "subagents");
        if (Directory.Exists(subagentsDir))
        {
            // Ordered so the graph is deterministic across rebuilds of the same on-disk state -
            // creation order doesn't matter for row/column assignment (that comes from each agent's
            // spawn linkage), only for stable, reproducible output.
            foreach (var agentFile in Directory.EnumerateFiles(subagentsDir, "agent-*.jsonl").OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            {
                var meta = AgentMetaReader.TryRead(agentFile);
                var agentId = Path.GetFileNameWithoutExtension(agentFile);
                agents.Add(new AgentNode(agentId, agentFile, depth: meta?.SpawnDepth ?? 1, meta?.ParentAgentId, meta?.ToolUseId)
                {
                    IsOrphan = meta is null,
                });
            }
        }

        var spawnToolUseIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var agent in agents)
        {
            if (agent.SpawnToolUseId is { } id)
            {
                spawnToolUseIds.Add(id);
            }
        }

        foreach (var agent in agents)
        {
            ParseFile(agent, spawnToolUseIds, showAllEvents);
        }

        LinkSpawns(agents);

        return agents;
    }

    // Reads one agent's file top-to-bottom with its OWN TranscriptRowFactory (tool_use/tool_result
    // pairing never crosses agent files - same rule TranscriptViewerControl.SetSource follows for
    // the List view). A row whose Category is SystemMeta is skipped entirely (not just hidden) when
    // showAllEvents is false, since hiding it after layout would leave a gap - the tree must
    // relayout on toggle instead (see SessionTreeView).
    private static void ParseFile(AgentNode agent, HashSet<string> spawnToolUseIds, bool showAllEvents)
    {
        if (!File.Exists(agent.FilePath))
        {
            return;
        }

        var factory = new TranscriptRowFactory();
        var ordinal = 0;

        try
        {
            foreach (var rawLine in File.ReadLines(agent.FilePath))
            {
                foreach (var row in factory.ProcessLine(rawLine))
                {
                    if (!showAllEvents && row.Category == TranscriptRowCategory.SystemMeta)
                    {
                        continue;
                    }

                    var toolUseId = row is ToolCallRowViewModel toolCall ? toolCall.ToolUseId : null;
                    var kind = toolUseId is not null && spawnToolUseIds.Contains(toolUseId) ? BoxKind.SubagentSpawn : BoxKind.Entry;
                    agent.Boxes.Add(new BoxNode(row, kind, agent, ordinal, toolUseId));
                }

                var returnToolUseId = TryPeekSubagentToolResult(rawLine, spawnToolUseIds);
                if (returnToolUseId is not null)
                {
                    var spawnBox = agent.Boxes.LastOrDefault(b => b.Kind == BoxKind.SubagentSpawn && b.ToolUseId == returnToolUseId);
                    if (spawnBox is not null)
                    {
                        // Reuses the SAME RowVm as the spawn box - the factory already merged this
                        // tool_result into it and produced no row of its own (ProcessLine returned
                        // nothing above for this line). This box is a distinct grid slot positioned
                        // at THIS line's ordinal, purely for layout/connector purposes.
                        agent.Boxes.Add(new BoxNode(spawnBox.RowVm, BoxKind.SubagentReturn, agent, ordinal, returnToolUseId));
                    }
                }

                ordinal++;
            }
        }
        catch (IOException)
        {
            // A snapshot build shouldn't crash for one locked/unreadable agent file - keep whatever
            // was parsed before the failure.
        }
    }

    // Independent, minimal read of a "user" line's tool_result content blocks - deliberately NOT
    // routed through TranscriptRowFactory (which already merged this away and returned no row for
    // it). Returns the tool_use_id only when it belongs to a KNOWN subagent spawn (i.e. some
    // agent's meta.json names it); returns null for a regular tool's result, which the factory
    // already rendered correctly as one merged ToolCallRowViewModel with no separate node needed.
    private static string? TryPeekSubagentToolResult(string rawLine, HashSet<string> spawnToolUseIds)
    {
        var parsed = TranscriptLineParser.Parse(rawLine);
        if (!parsed.IsValidJson || parsed.TypeValue != "user")
        {
            return null;
        }

        if (!parsed.Root.TryGetProperty("message", out var message)
            || !message.TryGetProperty("content", out var content)
            || content.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var block in content.EnumerateArray())
        {
            if (block.ValueKind != JsonValueKind.Object
                || !block.TryGetProperty("type", out var typeValue)
                || typeValue.ValueKind != JsonValueKind.String
                || typeValue.GetString() != "tool_result")
            {
                continue;
            }

            if (block.TryGetProperty("tool_use_id", out var idValue)
                && idValue.ValueKind == JsonValueKind.String
                && idValue.GetString() is { } id
                && spawnToolUseIds.Contains(id))
            {
                return id;
            }
        }

        return null;
    }

    // Resolves each subagent's SpawnBox/LinkedChild/ReturnBox and derives Depth from the actual
    // resolved parent chain (parent.Depth + 1), using meta.spawnDepth only as the pre-set fallback
    // for an agent that ends up orphaned. An agent becomes orphaned (unlinked, depth 1, no
    // connector) when its meta is missing, its claimed toolUseId matches no spawn box anywhere, or
    // the matched spawn box doesn't actually belong to its declared parent (a corrupt/inconsistent
    // meta.json) - see specs/session-log-tree-view.md section 10 for the error-handling policy.
    private static void LinkSpawns(List<AgentNode> agents)
    {
        var byId = agents.ToDictionary(a => a.AgentId, StringComparer.Ordinal);

        var spawnBoxByToolUseId = new Dictionary<string, BoxNode>(StringComparer.Ordinal);
        foreach (var box in agents.SelectMany(a => a.Boxes))
        {
            if (box.Kind == BoxKind.SubagentSpawn && box.ToolUseId is { } id)
            {
                spawnBoxByToolUseId.TryAdd(id, box);
            }
        }

        foreach (var agent in agents)
        {
            if (agent.AgentId == AgentNode.MainAgentId)
            {
                continue;
            }

            var parent = agent.ParentAgentId is { } parentId && byId.TryGetValue(parentId, out var declaredParent)
                ? declaredParent
                : byId[AgentNode.MainAgentId];

            if (agent.IsOrphan
                || agent.SpawnToolUseId is not { } spawnToolUseId
                || !spawnBoxByToolUseId.TryGetValue(spawnToolUseId, out var spawnBox)
                || spawnBox.Owner != parent)
            {
                agent.IsOrphan = true;
                agent.Depth = 1;
                continue;
            }

            agent.Depth = parent.Depth + 1;
            agent.SpawnBox = spawnBox;
            spawnBox.LinkedChild = agent;
            agent.ReturnBox = parent.Boxes.FirstOrDefault(b => b.Kind == BoxKind.SubagentReturn && b.ToolUseId == spawnToolUseId);
        }
    }
}
