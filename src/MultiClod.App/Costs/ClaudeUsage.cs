namespace MultiClod.App.Costs;

/// <summary>
/// Normalized token-usage fields from one JSONL assistant line's message.usage object. Cache-write
/// tokens are already split into 5-minute vs 1-hour buckets by the time this is constructed - see
/// ClaudeUsageReader, which applies the "no cache_creation breakdown -> assume 5-minute" rule.
/// </summary>
internal readonly record struct ClaudeUsage(
    long InputTokens,
    long OutputTokens,
    long CacheReadInputTokens,
    long CacheCreation5mInputTokens,
    long CacheCreation1hInputTokens);
