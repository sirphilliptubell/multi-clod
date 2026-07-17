namespace MultiClod.App.Costs;

/// <summary>
/// The result of aggregating one or more per-file model-cost dictionaries. HasAnyData is false only
/// for a session that's never been started (no log file exists yet at all) - that's the "show no
/// badge" case, distinct from a started session that simply hasn't spent anything yet.
/// </summary>
internal readonly struct SessionCostSummary
{
    public static readonly SessionCostSummary NoData = default;

    private SessionCostSummary(bool hasAnyData, decimal knownTotalUsd, bool hasUnknownContribution)
    {
        this.HasAnyData = hasAnyData;
        this.KnownTotalUsd = knownTotalUsd;
        this.HasUnknownContribution = hasUnknownContribution;
    }

    public bool HasAnyData { get; }

    public decimal KnownTotalUsd { get; }

    public bool HasUnknownContribution { get; }

    public static SessionCostSummary Of(decimal knownTotalUsd, bool hasUnknownContribution) =>
        new(hasAnyData: true, knownTotalUsd, hasUnknownContribution);
}
