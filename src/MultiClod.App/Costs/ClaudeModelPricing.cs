namespace MultiClod.App.Costs;

/// <summary>
/// One date-ranged rate entry for a single model slug. EffectiveUntilUtc null means open-ended
/// (still in effect). A model's price for a given line is resolved by matching both Slug AND a
/// timestamp falling within [EffectiveFromUtc, EffectiveUntilUtc) - see
/// ClaudeModelPricing.TryGetRate. Rates are USD per 1,000,000 tokens.
/// </summary>
internal sealed record ClaudeModelRateEntry(
    string Slug,
    DateTimeOffset EffectiveFromUtc,
    DateTimeOffset? EffectiveUntilUtc,
    decimal InputPerMillionUsd,
    decimal CacheWrite5mPerMillionUsd,
    decimal CacheWrite1hPerMillionUsd,
    decimal CacheReadPerMillionUsd,
    decimal OutputPerMillionUsd);

/// <summary>
/// Static, hand-maintained, append-only pricing table - the mc-update-costs skill
/// (.claude/skills/mc-update-costs/SKILL.md) is the intended way to keep this current over time.
/// New entries are always ADDED, never edited/removed: a price change appends a new entry and
/// closes the previous open-ended entry's EffectiveUntilUtc, and a model that disappears from
/// Anthropic's pricing pages simply keeps its last entry forever (see the skill for the exact
/// update procedure). This lets a session run months ago still price correctly using whatever rate
/// was actually in effect at the time, even after later price changes are appended.
///
/// A display model can have more than one slug (an alias plus a dated snapshot ID) - each gets its
/// own entry with identical rates rather than being grouped, so appending stays a flat list
/// operation with no grouping logic to get wrong.
///
/// Seed data current as of 2026-07-16, fetched live from:
/// - https://platform.claude.com/docs/en/about-claude/pricing.md
/// - https://platform.claude.com/docs/en/about-claude/models/overview
/// </summary>
internal static class ClaudeModelPricing
{
    private static readonly DateTimeOffset SonnetFivePriceChangeUtc = new(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);

    private static readonly IReadOnlyList<ClaudeModelRateEntry> Entries =
    [
        new("claude-fable-5", DateTimeOffset.MinValue, null, 10m, 12.50m, 20m, 1m, 50m),
        new("claude-mythos-5", DateTimeOffset.MinValue, null, 10m, 12.50m, 20m, 1m, 50m),

        new("claude-opus-4-8", DateTimeOffset.MinValue, null, 5m, 6.25m, 10m, 0.50m, 25m),
        new("claude-opus-4-7", DateTimeOffset.MinValue, null, 5m, 6.25m, 10m, 0.50m, 25m),
        new("claude-opus-4-6", DateTimeOffset.MinValue, null, 5m, 6.25m, 10m, 0.50m, 25m),
        new("claude-opus-4-5", DateTimeOffset.MinValue, null, 5m, 6.25m, 10m, 0.50m, 25m),
        new("claude-opus-4-5-20251101", DateTimeOffset.MinValue, null, 5m, 6.25m, 10m, 0.50m, 25m),
        new("claude-opus-4-1", DateTimeOffset.MinValue, null, 15m, 18.75m, 30m, 1.50m, 75m),
        new("claude-opus-4-1-20250805", DateTimeOffset.MinValue, null, 15m, 18.75m, 30m, 1.50m, 75m),
        new("claude-opus-4-0", DateTimeOffset.MinValue, null, 15m, 18.75m, 30m, 1.50m, 75m),
        new("claude-opus-4-20250514", DateTimeOffset.MinValue, null, 15m, 18.75m, 30m, 1.50m, 75m),

        // Sonnet 5 has a known scheduled price change - the reference example for how a price
        // change should be appended (two adjacent, non-overlapping date ranges under one slug).
        new("claude-sonnet-5", DateTimeOffset.MinValue, SonnetFivePriceChangeUtc, 2m, 2.50m, 4m, 0.20m, 10m),
        new("claude-sonnet-5", SonnetFivePriceChangeUtc, null, 3m, 3.75m, 6m, 0.30m, 15m),

        new("claude-sonnet-4-6", DateTimeOffset.MinValue, null, 3m, 3.75m, 6m, 0.30m, 15m),
        new("claude-sonnet-4-5", DateTimeOffset.MinValue, null, 3m, 3.75m, 6m, 0.30m, 15m),
        new("claude-sonnet-4-5-20250929", DateTimeOffset.MinValue, null, 3m, 3.75m, 6m, 0.30m, 15m),
        new("claude-sonnet-4-0", DateTimeOffset.MinValue, null, 3m, 3.75m, 6m, 0.30m, 15m),
        new("claude-sonnet-4-20250514", DateTimeOffset.MinValue, null, 3m, 3.75m, 6m, 0.30m, 15m),

        new("claude-haiku-4-5", DateTimeOffset.MinValue, null, 1m, 1.25m, 2m, 0.10m, 5m),
        new("claude-haiku-4-5-20251001", DateTimeOffset.MinValue, null, 1m, 1.25m, 2m, 0.10m, 5m),
        new("claude-3-5-haiku-20241022", DateTimeOffset.MinValue, null, 0.80m, 1m, 1.60m, 0.08m, 4m),
    ];

    public static ClaudeModelRateEntry? TryGetRate(string modelSlug, DateTimeOffset timestamp)
    {
        foreach (var entry in Entries)
        {
            if (entry.Slug == modelSlug
                && timestamp >= entry.EffectiveFromUtc
                && (entry.EffectiveUntilUtc is not { } until || timestamp < until))
            {
                return entry;
            }
        }

        return null;
    }

    // Slug-only check (ignores date range) - used by SessionCostFileProcessor's self-heal check to
    // decide whether a file previously marked "unknown model" is now worth a full reparse. The
    // reparse itself is what determines actual per-line coverage; this just answers "would it be
    // worth trying again."
    public static bool HasAnyRateFor(string modelSlug)
    {
        foreach (var entry in Entries)
        {
            if (entry.Slug == modelSlug)
            {
                return true;
            }
        }

        return false;
    }
}
