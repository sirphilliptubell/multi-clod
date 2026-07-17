namespace MultiClod.App.SessionLog.Tree;

/// <summary>
/// One agent (the main session, or one subagent) in a Tree snapshot - owns the ordered list of
/// boxes parsed from its own file (main session or agent-&lt;id&gt;.jsonl), using its own
/// Rendering.TranscriptRowFactory exactly like TranscriptViewerControl.SetSource does per source
/// for the List view (tool_use/tool_result pairing never crosses agent files). Layout outputs
/// (Column/FirstRow/LastRow) are populated by TreeLayoutEngine after TreeGraphBuilder finishes
/// linking spawns.
/// </summary>
public sealed class AgentNode
{
    public const string MainAgentId = "main";

    public AgentNode(string agentId, string filePath, int depth, string? parentAgentId, string? spawnToolUseId)
    {
        this.AgentId = agentId;
        this.FilePath = filePath;
        this.Depth = depth;
        this.ParentAgentId = parentAgentId;
        this.SpawnToolUseId = spawnToolUseId;
    }

    public string AgentId { get; }

    public string FilePath { get; }

    // Resolved depth (0 = main session). Derived from the actual resolved parent chain
    // (parent.Depth + 1) during LinkSpawns, NOT taken verbatim from meta.json's spawnDepth - the
    // latter is used only as the initial value / fallback for an orphan agent. See the spec's risk
    // note on spawnDepth vs. the actual parent chain disagreeing.
    public int Depth { get; internal set; }

    public string? ParentAgentId { get; }

    // The meta.json "toolUseId" this agent claims spawned it - null for the main session, or for
    // an agent whose meta.json was missing/malformed (see IsOrphan).
    public string? SpawnToolUseId { get; }

    // True when this agent's meta.json is missing/malformed, or its SpawnToolUseId doesn't match
    // any box anywhere in the graph - rendered as an unlinked column at depth 1, no connector.
    public bool IsOrphan { get; internal set; }

    public List<BoxNode> Boxes { get; } = new();

    // The SubagentSpawn box (owned by the PARENT agent) that spawned this agent - null while
    // orphan/unlinked.
    public BoxNode? SpawnBox { get; internal set; }

    // The SubagentReturn box (also owned by the parent) that collects this agent's result - null
    // until/unless the parent's tool_result line for this agent has been parsed. Absent forever for
    // a still-running child - the return connector is simply omitted in that case.
    public BoxNode? ReturnBox { get; internal set; }

    public int Column { get; internal set; }

    public int FirstRow { get; internal set; }

    public int LastRow { get; internal set; }
}
