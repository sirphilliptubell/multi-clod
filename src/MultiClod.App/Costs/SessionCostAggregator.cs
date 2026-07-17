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
        var byModel = new Dictionary<string, decimal?>();

        foreach (var fileCosts in perFileModelCosts)
        {
            foreach (var (model, cost) in fileCosts)
            {
                if (cost is { } amount)
                {
                    known += amount;
                }
                else
                {
                    hasUnknown = true;
                }

                // Merge into the per-model breakdown, summing the same model's contributions across
                // files (e.g. the main log and a subagent both using claude-opus-4-8 collapse into
                // one breakdown line). Sticky-null, same rule as SessionCostFileProcessor's own
                // per-file accumulation: once a model's cost is unknown in any one file, the whole
                // model reads as unknown rather than an understated partial sum.
                if (byModel.TryGetValue(model, out var existing) && existing is null)
                {
                    continue;
                }

                byModel[model] = cost is null ? null : (existing ?? 0m) + cost.Value;
            }
        }

        // Highest-cost-first; unpriced (null) models last regardless of amount - matches the
        // approved "most expensive first, null-costed models last" breakdown ordering.
        var breakdown = byModel
            .Select(kvp => new ModelCostEntry(kvp.Key, kvp.Value))
            .OrderBy(entry => entry.AmountUsd is null)
            .ThenByDescending(entry => entry.AmountUsd ?? 0m)
            .ThenBy(entry => entry.ModelSlug, StringComparer.Ordinal)
            .ToList();

        return SessionCostSummary.Of(known, hasUnknown, breakdown);
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

    // Tooltip content for a cost badge: one "slug: $X.XX" line per model, already in the order
    // ModelBreakdown was sorted in (highest-cost-first, unpriced last). Null when there's nothing
    // to show - no data at all, or an aggregate that never accumulated any model contribution.
    public static string? FormatBreakdown(SessionCostSummary summary)
    {
        if (!summary.HasAnyData || summary.ModelBreakdown.Count == 0)
        {
            return null;
        }

        return string.Join(Environment.NewLine, summary.ModelBreakdown.Select(FormatBreakdownLine));
    }

    private static string FormatBreakdownLine(ModelCostEntry entry) =>
        $"{entry.ModelSlug}: {(entry.AmountUsd is { } amount ? CostFormatting.FormatKnownAmount(amount) : "$?.??")}";
}
