namespace MultiClod.App.Costs;

/// <summary>
/// One model's contribution to a SessionCostSummary's breakdown - AmountUsd is null when that model
/// was seen but unpriced (same sticky-null meaning as SessionCostCacheFile.ModelCostsUsd's values).
/// </summary>
internal readonly record struct ModelCostEntry(string ModelSlug, decimal? AmountUsd);

/// <summary>
/// The result of aggregating one or more per-file model-cost dictionaries. HasAnyData is false only
/// for a session that's never been started (no log file exists yet at all) - that's the "show no
/// badge" case, distinct from a started session that simply hasn't spent anything yet.
/// </summary>
internal readonly struct SessionCostSummary
{
    // Explicit (not `default`) so ModelBreakdown is a real empty list rather than a null reference
    // - `default(IReadOnlyList<T>)` is null, which would contradict its own "never null" doc below.
    public static readonly SessionCostSummary NoData = new(hasAnyData: false, knownTotalUsd: 0m, hasUnknownContribution: false, modelBreakdown: []);

    private SessionCostSummary(bool hasAnyData, decimal knownTotalUsd, bool hasUnknownContribution, IReadOnlyList<ModelCostEntry> modelBreakdown)
    {
        this.HasAnyData = hasAnyData;
        this.KnownTotalUsd = knownTotalUsd;
        this.HasUnknownContribution = hasUnknownContribution;
        this.ModelBreakdown = modelBreakdown;
    }

    public bool HasAnyData { get; }

    public decimal KnownTotalUsd { get; }

    public bool HasUnknownContribution { get; }

    // Per-model share of KnownTotalUsd, already sorted highest-cost-first with unpriced (null)
    // models last - see SessionCostAggregator.Aggregate. Empty (never null) for NoData.
    public IReadOnlyList<ModelCostEntry> ModelBreakdown { get; }

    public static SessionCostSummary Of(decimal knownTotalUsd, bool hasUnknownContribution, IReadOnlyList<ModelCostEntry> modelBreakdown) =>
        new(hasAnyData: true, knownTotalUsd, hasUnknownContribution, modelBreakdown);
}
