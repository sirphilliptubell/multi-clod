namespace MultiClod.App.SessionLog.Tree;

/// <summary>
/// Deserialized agent-&lt;id&gt;.meta.json sidecar - links a subagent transcript back to the
/// tool_use entry (in its parent's transcript) that spawned it. ParentAgentId is null when
/// SpawnDepth == 1 (parent is the main session); populated for depth >= 2 (parent is another
/// subagent). See AgentMetaReader for how this is read and TreeGraphBuilder for how it's resolved
/// into the graph.
/// </summary>
public sealed record AgentMeta(string AgentType, string Description, string ToolUseId, string? ParentAgentId, int SpawnDepth);
