namespace MultiClod.App.SessionLog.Tree;

/// <summary>
/// Width/height of the laid-out graph in unscaled pixel space - used to size the zoom/pan canvas
/// and to compute the "fit entire graph in viewport" zoom-out floor (see SessionTreeView).
/// </summary>
public readonly record struct TreeSize(double Width, double Height);

/// <summary>
/// A single point in unscaled graph-pixel space, used to build connector polylines.
/// </summary>
public readonly record struct TreePoint(double X, double Y);

public enum TreeConnectorKind
{
    Spawn,
    Return,
}

/// <summary>
/// One drawn edge in the Tree canvas, rendered by ConnectorOverlay in a single OnRender pass. A
/// Spawn connector runs from a parent's SubagentSpawn box to its child's first box - Points has more
/// than 2 entries only when routed under sibling boxes (see TreeLayoutEngine.BuildConnectors for the
/// multi-child offset/under-routing rule). A Return connector is an arrow from a child's last box to
/// its parent's SubagentReturn box, and is simply absent from the graph for a still-running child.
/// </summary>
public sealed record TreeConnector(TreeConnectorKind Kind, IReadOnlyList<TreePoint> Points, string ColorHex);

/// <summary>
/// One immutable snapshot of a session's Tree view: every agent (main + subagents) with its boxes
/// laid out on a shared row/column grid, plus the connector geometry between them. Rebuilt whole on
/// every BuildSnapshot (see SessionTreeView) - never mutated in place, so a Refresh can't leave the
/// canvas half-updated.
/// </summary>
public sealed class TreeGraph
{
    public TreeGraph(AgentNode mainSession, IReadOnlyList<AgentNode> agents, IReadOnlyList<BoxNode> boxes, IReadOnlyList<TreeConnector> connectors, TreeSize pixelExtent)
    {
        this.MainSession = mainSession;
        this.Agents = agents;
        this.Boxes = boxes;
        this.Connectors = connectors;
        this.PixelExtent = pixelExtent;
    }

    public AgentNode MainSession { get; }

    public IReadOnlyList<AgentNode> Agents { get; }

    public IReadOnlyList<BoxNode> Boxes { get; }

    public IReadOnlyList<TreeConnector> Connectors { get; }

    public TreeSize PixelExtent { get; }
}
