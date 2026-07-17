namespace MultiClod.App.Costs;

/// <summary>
/// Combines one or more per-file model-cost dictionaries (main log + zero or more subagent logs,
/// or just a single file for a Session Log window's per-thread total) into one SessionCostSummary,
/// and formats that summary for display. Aggregate() always returns a "has data" summary for its
/// input - callers decide the "never started, no badge at all" case themselves by simply never
/// calling this (or UpdateCostSummary) for a session that hasn't been started yet.
/// </summary>
internal static class SessionCostAggregator
{
    public static SessionCostSummary Aggregate(IEnumerable<IReadOnlyDictionary<string, decimal?>> perFileModelCosts)
    {
        var known = 0m;
        var hasUnknown = false;

        foreach (var fileCosts in perFileModelCosts)
        {
            foreach (var modelCost in fileCosts.Values)
            {
                if (modelCost is { } amount)
                {
                    known += amount;
                }
                else
                {
                    hasUnknown = true;
                }
            }
        }

        return SessionCostSummary.Of(known, hasUnknown);
    }

    // "$X.XX" / ">$X.XX" / "<$0.01" / null (no badge) - null only for SessionCostSummary.NoData,
    // i.e. a session that's never been started. The "<$0.01" floor only applies when every
    // contributing model was known: with an unknown contribution, the ">" prefix already signals
    // "at least this much, possibly more," so a plain "$0.00" underneath is fine rather than
    // stacking both hedges into something like "><$0.01".
    public static string? FormatBadge(SessionCostSummary summary)
    {
        if (!summary.HasAnyData)
        {
            return null;
        }

        return summary.HasUnknownContribution
            ? $">${summary.KnownTotalUsd:F2}"
            : CostFormatting.FormatKnownAmount(summary.KnownTotalUsd);
    }
}
