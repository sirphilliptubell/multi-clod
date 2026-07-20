using MultiClod.App.Costs;
using MultiClod.App.SessionLog.Parsing;
using MultiClod.App.SessionLog.Rendering;

namespace MultiClod.App.SessionLog.Costs;

/// <summary>
/// One agent's (main or one subagent) append-only cost timeline. Owns the running cumulative total
/// and a per-model running-cost dictionary (for the legend/left-panel total, sticky-null just like
/// SessionCostAggregator elsewhere), and appends one CostTimelinePoint per line that produces one.
///
/// Deliberate deviation from SessionCostFileProcessor's sticky-null policy: an unpriced line
/// contributes $0 to the PLOTTED running total (so the graph always keeps drawing), while
/// CostByModel still tracks sticky-null (so the legend can still flag "unknown contribution") -
/// sticky-null on the graph itself would freeze a whole series the first time it hit one unpriced
/// model, which is unacceptable for a live view.
/// </summary>
internal sealed class CostSeriesTimeline
{
    private readonly string agentId;
    private readonly string filePath;
    private readonly List<CostTimelinePoint> points = new();
    private readonly Dictionary<string, decimal?> costByModel = new(StringComparer.Ordinal);
    private decimal cumulativeUsd;
    private int nextOrdinal;

    public CostSeriesTimeline(string agentId, string filePath)
    {
        this.agentId = agentId;
        this.filePath = filePath;
    }

    public IReadOnlyDictionary<string, decimal?> CostByModel => this.costByModel;

    // One entry per distinct BucketKey, last-writer-wins - the single deterministic rule for
    // "which point represents this series at this column," used identically by rendering, the
    // hover tooltip, and right-click navigation. `points` is append-only so GroupBy/Last is stable.
    public IReadOnlyList<CostTimelinePoint> EffectivePoints() =>
        this.points.GroupBy(p => p.BucketKey).Select(g => g.Last()).ToList();

    public CostTimelinePoint? TryAppend(ParsedLine parsed, bool includeAllLines)
    {
        var ordinal = this.nextOrdinal++;

        if (!parsed.IsValidJson)
        {
            return null;
        }

        var timestamp = TranscriptTimestamp.TryRead(parsed.Root);
        if (timestamp is null)
        {
            return null;
        }

        var bucketKey = ComputeBucketKey(timestamp.Value);

        if (parsed.TypeValue == "assistant" && ClaudeUsageReader.TryRead(parsed.Root) is { ModelSlug.Length: > 0 } usageLine)
        {
            var cost = ClaudeCostCalculator.TryComputeUsd(usageLine.ModelSlug, usageLine.Usage, timestamp);
            var delta = cost ?? 0m;
            this.cumulativeUsd += delta;

            // Sticky-null, same rule SessionCostFileProcessor/SessionCostAggregator use elsewhere:
            // once a model's legend contribution has gone unknown, it stays unknown.
            if (!(this.costByModel.TryGetValue(usageLine.ModelSlug, out var existing) && existing is null))
            {
                this.costByModel[usageLine.ModelSlug] = cost is null ? null : (existing ?? 0m) + cost.Value;
            }

            var point = new CostTimelinePoint(
                this.agentId, this.filePath, ordinal, bucketKey, timestamp.Value,
                IsCostBearing: true, usageLine.ModelSlug, this.cumulativeUsd, delta, IsPriceKnown: cost is not null);
            this.points.Add(point);
            return point;
        }

        if (!includeAllLines)
        {
            return null;
        }

        var flatPoint = new CostTimelinePoint(
            this.agentId, this.filePath, ordinal, bucketKey, timestamp.Value,
            IsCostBearing: false, ModelSlug: null, this.cumulativeUsd, DeltaUsd: 0m, IsPriceKnown: true);
        this.points.Add(flatPoint);
        return flatPoint;
    }

    // Standard round-half-away-from-zero, applied identically everywhere a BucketKey is derived.
    private static long ComputeBucketKey(DateTimeOffset timestamp) =>
        (long)Math.Round(timestamp.ToUnixTimeMilliseconds() / 1000.0, MidpointRounding.AwayFromZero);
}
