namespace MultiClod.App.SessionLog.Tree;

/// <summary>
/// Assigns every box a Row/Column/X/Y and builds connector geometry for one TreeGraphBuilder
/// result. Pure - no WPF types. See specs/session-log-tree-view.md section 5 for the full algorithm
/// rationale; this is a direct implementation of that reference pseudocode.
/// </summary>
internal static class TreeLayoutEngine
{
    public sealed record Metrics(double BoxWidth, double BoxHeight, double HorizontalGap, double VerticalGap, double Padding, double ConnectorGap);

    public static readonly Metrics DefaultMetrics = new(BoxWidth: 160, BoxHeight: 40, HorizontalGap: 40, VerticalGap: 12, Padding: 24, ConnectorGap: 10);

    // Reuses the existing category accent hues (CategoryToBrushConverter) so Tree connector colors
    // read as part of the same visual language as the row glyphs/borders.
    private static readonly string[] ConnectorPalette = ["#3A96DD", "#DA7756", "#9B8AC4", "#D08B2C", "#8A8A8A", "#D9A93A"];

    public static TreeGraph Layout(IReadOnlyList<AgentNode> agents, Metrics metrics)
    {
        var main = agents.First(a => a.AgentId == AgentNode.MainAgentId);

        var childByToolUseId = new Dictionary<string, AgentNode>(StringComparer.Ordinal);
        foreach (var agent in agents)
        {
            if (!agent.IsOrphan && agent.SpawnToolUseId is { } id)
            {
                childByToolUseId[id] = agent;
            }
        }

        var childTailRow = new Dictionary<AgentNode, int>();
        LayoutAgentRows(main, startRow: 0, childByToolUseId, childTailRow);

        AssignColumns(agents);

        var boxes = new List<BoxNode>();
        double maxRight = 0;
        double maxBottom = 0;

        foreach (var agent in agents)
        {
            foreach (var box in agent.Boxes)
            {
                box.Column = agent.Column;
                box.X = metrics.Padding + box.Column * (metrics.BoxWidth + metrics.HorizontalGap);
                box.Y = metrics.Padding + box.Row * (metrics.BoxHeight + metrics.VerticalGap);
                maxRight = Math.Max(maxRight, box.X + metrics.BoxWidth);
                maxBottom = Math.Max(maxBottom, box.Y + metrics.BoxHeight);
                boxes.Add(box);
            }
        }

        var extent = new TreeSize(maxRight + metrics.Padding, maxBottom + metrics.Padding);
        var connectors = BuildConnectors(agents, metrics);

        return new TreeGraph(main, agents, boxes, connectors, extent);
    }

    // DFS: a subagent's first box is pinned to the same row as the parent's spawn box (placed to
    // its right via column assignment, done afterward); the parent's SubagentReturn box is forced
    // to a row strictly below the child's last box. This single rule is what makes
    // foreground-vs-background concurrency emerge without special-casing either - see the spec.
    private static int LayoutAgentRows(AgentNode agent, int startRow, Dictionary<string, AgentNode> childByToolUseId, Dictionary<AgentNode, int> childTailRow)
    {
        var row = startRow;
        agent.FirstRow = startRow;

        foreach (var box in agent.Boxes)
        {
            switch (box.Kind)
            {
                case BoxKind.SubagentSpawn:
                    box.Row = row;
                    if (box.LinkedChild is { } child)
                    {
                        var childLastRow = LayoutAgentRows(child, row, childByToolUseId, childTailRow);
                        childTailRow[child] = childLastRow;
                    }

                    row++;
                    break;

                case BoxKind.SubagentReturn:
                    // Ordering invariant: a SubagentReturn always follows its SubagentSpawn in file
                    // order, so the child is already laid out here - if that invariant is ever
                    // violated (a malformed transcript with the result before the call), fall back
                    // to treating it as a plain entry rather than forcing rows around a tail we
                    // don't have.
                    var childTail = box.ToolUseId is { } toolUseId
                        && childByToolUseId.TryGetValue(toolUseId, out var returningChild)
                        && childTailRow.TryGetValue(returningChild, out var tail)
                        ? tail
                        : (int?)null;

                    box.Row = childTail is { } tailRow ? Math.Max(row, tailRow + 1) : row;
                    row = box.Row + 1;
                    break;

                default:
                    box.Row = row;
                    row++;
                    break;
            }
        }

        agent.LastRow = Math.Max(agent.FirstRow, row - 1);
        return agent.LastRow;
    }

    // Interval-partition lane packing per depth, ascending: a subagent takes the leftmost lane (at
    // its depth) whose last-used row is before this agent's FirstRow - non-overlapping agents reuse
    // a lane, concurrent siblings get adjacent lanes, and every depth block sits to the right of all
    // shallower depths.
    private static void AssignColumns(IReadOnlyList<AgentNode> agents)
    {
        var main = agents.First(a => a.AgentId == AgentNode.MainAgentId);
        main.Column = 0;

        var columnBase = new Dictionary<int, int> { [0] = 0 };
        var laneCountAtDepth = new Dictionary<int, int> { [0] = 1 };

        var depthsAscending = agents.Where(a => a.Depth >= 1).Select(a => a.Depth).Distinct().OrderBy(d => d);

        foreach (var depth in depthsAscending)
        {
            var previousBase = columnBase.GetValueOrDefault(depth - 1, 0);
            var previousLanes = laneCountAtDepth.GetValueOrDefault(depth - 1, 0);
            columnBase[depth] = previousBase + previousLanes;

            var laneEndRow = new List<int>();
            var agentsAtDepth = agents
                .Where(a => a.Depth == depth)
                .OrderBy(a => a.FirstRow)
                .ThenBy(a => a.SpawnBox?.Column ?? int.MaxValue);

            foreach (var agent in agentsAtDepth)
            {
                var lane = laneEndRow.FindIndex(endRow => endRow < agent.FirstRow);
                if (lane == -1)
                {
                    lane = laneEndRow.Count;
                    laneEndRow.Add(agent.LastRow);
                }
                else
                {
                    laneEndRow[lane] = agent.LastRow;
                }

                agent.Column = columnBase[depth] + lane;
            }

            laneCountAtDepth[depth] = laneEndRow.Count;
        }
    }

    // Spawn connectors are grouped by (owning agent, row) - always groups of 1 under the current
    // one-box-per-row-VM layout (each tool_use gets its own row), but the offset/color/under-route
    // logic stays generically correct if a future change ever puts multiple spawn boxes on one row
    // (e.g. several parallel Task calls emitted within a single assistant turn).
    private static IReadOnlyList<TreeConnector> BuildConnectors(IReadOnlyList<AgentNode> agents, Metrics metrics)
    {
        var connectors = new List<TreeConnector>();

        var spawnBoxes = agents
            .SelectMany(a => a.Boxes)
            .Where(b => b.Kind == BoxKind.SubagentSpawn && b.LinkedChild is not null)
            .GroupBy(b => (b.Owner, b.Row));

        foreach (var group in spawnBoxes)
        {
            var siblings = group.ToList();
            for (var i = 0; i < siblings.Count; i++)
            {
                var spawnBox = siblings[i];
                var childFirst = spawnBox.LinkedChild!.Boxes.FirstOrDefault();
                if (childFirst is null)
                {
                    continue;
                }

                var color = ConnectorPalette[i % ConnectorPalette.Length];
                var fromY = spawnBox.Y + metrics.BoxHeight * 0.25 + i * metrics.ConnectorGap;
                var toY = childFirst.Y + metrics.BoxHeight * 0.25;

                IReadOnlyList<TreePoint> points = siblings.Count == 1
                    ? [new TreePoint(spawnBox.X + metrics.BoxWidth, fromY), new TreePoint(childFirst.X, toY)]
                    :
                    [
                        new TreePoint(spawnBox.X + metrics.BoxWidth, fromY),
                        new TreePoint(spawnBox.X + metrics.BoxWidth + metrics.HorizontalGap * 0.5, spawnBox.Y + metrics.BoxHeight + i * metrics.ConnectorGap),
                        new TreePoint(childFirst.X - metrics.HorizontalGap * 0.5, spawnBox.Y + metrics.BoxHeight + i * metrics.ConnectorGap),
                        new TreePoint(childFirst.X, toY),
                    ];

                connectors.Add(new TreeConnector(TreeConnectorKind.Spawn, points, color));
            }
        }

        foreach (var agent in agents)
        {
            if (agent.AgentId == AgentNode.MainAgentId || agent.ReturnBox is not { } returnBox || agent.Boxes.Count == 0)
            {
                continue;
            }

            var childLast = agent.Boxes[^1];
            var points = new List<TreePoint>
            {
                new(childLast.X + metrics.BoxWidth / 2, childLast.Y + metrics.BoxHeight),
                new(returnBox.X, returnBox.Y + metrics.BoxHeight / 2),
            };

            connectors.Add(new TreeConnector(TreeConnectorKind.Return, points, ConnectorPalette[0]));
        }

        return connectors;
    }
}
