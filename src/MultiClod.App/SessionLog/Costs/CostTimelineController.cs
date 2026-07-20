using System.IO;
using MultiClod.App.SessionLog.Tree;

namespace MultiClod.App.SessionLog.Costs;

/// <summary>
/// Dispatcher-agnostic orchestrator for one Costs session (same posture as
/// SessionCostMonitorService: raises events on background threads, caller marshals to UI). Owns
/// the CostBucketIndex, the root CostAgentNode (main), and one CostLineTailer per known file.
/// Initialize always does a full teardown+rebuild - reused for the "all lines" toggle and for a
/// ClaudeSessionId change, and (per CostsView) called unconditionally every time the user switches
/// into Costs mode, never lazily-once like Tree.
/// </summary>
internal sealed class CostTimelineController : IDisposable
{
    private readonly object gate = new();
    private readonly Dictionary<string, CostLineTailer> tailersByAgentId = new(StringComparer.Ordinal);
    private readonly Dictionary<string, CostAgentNode> nodesByAgentId = new(StringComparer.Ordinal);
    private readonly CostAgentColorAssigner colorAssigner = new();
    private SubagentTranscriptWatcher? subagentWatcher;
    private bool includeAllLines;

    public CostBucketIndex BucketIndex { get; private set; } = new();

    public CostAgentNode MainNode { get; private set; } = null!;

    // Raised on a background thread (the discovering SubagentTranscriptWatcher's own thread) -
    // caller marshals to the UI thread. Deliberately does NOT add the node to its parent's
    // Children itself: that ObservableCollection is bound directly to a live WPF TreeView, and
    // WPF collection-changed notifications must originate on the dispatcher thread - the caller's
    // UI-thread-marshaled handler is responsible for `node.Parent!.Children.Add(node)`.
    public event Action<CostAgentNode>? AgentDiscovered;

    // Raised on a background thread (the emitting CostLineTailer's own tailer thread) - caller
    // marshals to the UI thread.
    public event Action<IReadOnlyList<CostTimelinePoint>>? PointsAvailable;

    public void Initialize(string mainFilePath, string sessionDir, bool includeAllLines)
    {
        this.TearDown();
        this.includeAllLines = includeAllLines;
        this.BucketIndex = new CostBucketIndex();
        this.nodesByAgentId.Clear();

        this.MainNode = new CostAgentNode(AgentNode.MainAgentId, mainFilePath, parent: null, CostAgentColorAssigner.MainColorHex, "Main Session");
        this.nodesByAgentId[AgentNode.MainAgentId] = this.MainNode;

        var mainTailer = new CostLineTailer(mainFilePath, AgentNode.MainAgentId, includeAllLines);
        this.MainNode.Timeline = mainTailer.Series;
        mainTailer.PointsAvailable += this.OnPointsAvailable;
        this.tailersByAgentId[AgentNode.MainAgentId] = mainTailer;

        var watcher = new SubagentTranscriptWatcher(sessionDir);
        watcher.SubagentDiscovered += this.OnSubagentDiscovered;
        this.subagentWatcher = watcher;
    }

    // Builds hierarchy directly from AgentMetaReader/AgentMeta.ParentAgentId - deliberately not
    // TreeGraphBuilder.Build, which exists only to compute Tree-view connector geometry via
    // spawn/tool_use_id box-matching; a flat parent/child hierarchy needs none of that. Falls back
    // to Main when the declared parent is null or not yet discovered - same fallback
    // TreeGraphBuilder.LinkSpawns uses for an orphan.
    private void OnSubagentDiscovered(SessionLogSourceViewModel source)
    {
        CostAgentNode node;
        CostLineTailer tailer;

        lock (this.gate)
        {
            var agentId = Path.GetFileNameWithoutExtension(source.FilePath);
            var meta = AgentMetaReader.TryRead(source.FilePath);
            var parent = meta?.ParentAgentId is { } parentId && this.nodesByAgentId.TryGetValue(parentId, out var declaredParent)
                ? declaredParent
                : this.MainNode;

            var displayName = meta is { Description.Length: > 0 } ? meta.Description : agentId;
            node = new CostAgentNode(agentId, source.FilePath, parent, this.colorAssigner.AssignNext(), displayName);
            this.nodesByAgentId[agentId] = node;

            tailer = new CostLineTailer(source.FilePath, agentId, this.includeAllLines);
            node.Timeline = tailer.Series;
            this.tailersByAgentId[agentId] = tailer;
        }

        tailer.PointsAvailable += this.OnPointsAvailable;
        this.AgentDiscovered?.Invoke(node);
    }

    private void OnPointsAvailable(IReadOnlyList<CostTimelinePoint> points)
    {
        foreach (var point in points)
        {
            this.BucketIndex.GetOrAddColumn(point.BucketKey);
        }

        this.PointsAvailable?.Invoke(points);
    }

    public void Dispose() => this.TearDown();

    private void TearDown()
    {
        foreach (var tailer in this.tailersByAgentId.Values)
        {
            tailer.Dispose();
        }

        this.tailersByAgentId.Clear();
        this.subagentWatcher?.Dispose();
        this.subagentWatcher = null;
    }
}
