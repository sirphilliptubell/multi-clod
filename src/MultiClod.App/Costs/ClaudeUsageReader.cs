using System.Text.Json;
using MultiClod.App.SessionLog.Rendering;

namespace MultiClod.App.Costs;

/// <summary>
/// One JSONL assistant line's model + usage + timestamp, ready for pricing. Null only when the
/// line has no message.usage at all (e.g. a user/tool_result line, or an assistant line that
/// somehow lacks usage) - a present-but-unrecognized model slug still produces a line here; that's
/// ClaudeCostCalculator's job to reject, not this reader's.
/// </summary>
internal sealed record ClaudeUsageLine(string ModelSlug, ClaudeUsage Usage, DateTimeOffset? Timestamp);

/// <summary>
/// Reads message.model + message.usage off an already-parsed JSONL line root - the same JsonElement
/// TranscriptRowFactory/SessionCostFileProcessor already have in hand, so this never does its own
/// file or JSON parsing. Never throws: a missing/malformed field just means the value that
/// depended on it comes back 0/null.
/// </summary>
internal static class ClaudeUsageReader
{
    public static ClaudeUsageLine? TryRead(JsonElement lineRoot)
    {
        if (lineRoot.ValueKind != JsonValueKind.Object
            || !lineRoot.TryGetProperty("message", out var message)
            || message.ValueKind != JsonValueKind.Object
            || !message.TryGetProperty("usage", out var usage)
            || usage.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var modelSlug = message.TryGetProperty("model", out var modelProperty) && modelProperty.ValueKind == JsonValueKind.String
            ? modelProperty.GetString() ?? string.Empty
            : string.Empty;

        var inputTokens = GetLong(usage, "input_tokens");
        var outputTokens = GetLong(usage, "output_tokens");
        var cacheReadTokens = GetLong(usage, "cache_read_input_tokens");

        long cache5m;
        long cache1h;
        if (usage.TryGetProperty("cache_creation", out var cacheCreation) && cacheCreation.ValueKind == JsonValueKind.Object)
        {
            cache5m = GetLong(cacheCreation, "ephemeral_5m_input_tokens");
            cache1h = GetLong(cacheCreation, "ephemeral_1h_input_tokens");
        }
        else
        {
            // No breakdown available - the flat cache_creation_input_tokens total (if any) is
            // treated as entirely 5-minute-cache-write tokens per the approved plan's default.
            cache5m = GetLong(usage, "cache_creation_input_tokens");
            cache1h = 0;
        }

        var normalizedUsage = new ClaudeUsage(inputTokens, outputTokens, cacheReadTokens, cache5m, cache1h);
        return new ClaudeUsageLine(modelSlug, normalizedUsage, TranscriptTimestamp.TryRead(lineRoot));
    }

    private static long GetLong(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var longValue)
            ? longValue
            : 0;
}
