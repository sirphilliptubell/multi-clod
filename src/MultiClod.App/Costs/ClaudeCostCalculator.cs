namespace MultiClod.App.Costs;

/// <summary>
/// Prices one line's usage against ClaudeModelPricing. Null means "can't price this line" - either
/// the timestamp is missing, or no rate entry covers this (slug, timestamp) pair (unknown model, or
/// a model that's known but wasn't priced yet at this particular point in time).
/// </summary>
internal static class ClaudeCostCalculator
{
    private const decimal Million = 1_000_000m;

    public static decimal? TryComputeUsd(string modelSlug, ClaudeUsage usage, DateTimeOffset? timestamp)
    {
        if (timestamp is not { } ts || ClaudeModelPricing.TryGetRate(modelSlug, ts) is not { } rate)
        {
            return null;
        }

        return usage.InputTokens * rate.InputPerMillionUsd / Million
             + usage.OutputTokens * rate.OutputPerMillionUsd / Million
             + usage.CacheReadInputTokens * rate.CacheReadPerMillionUsd / Million
             + usage.CacheCreation5mInputTokens * rate.CacheWrite5mPerMillionUsd / Million
             + usage.CacheCreation1hInputTokens * rate.CacheWrite1hPerMillionUsd / Million;
    }
}
