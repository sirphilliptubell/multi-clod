using System.Text.Json;

namespace MultiClod.App.SessionLog.Rendering;

/// <summary>
/// Reads/formats the top-level "timestamp" field transcript lines carry. Some infrastructure
/// entries have no timestamp at all - callers omit it from the summary rather than showing a
/// placeholder, per the approved plan.
/// </summary>
internal static class TranscriptTimestamp
{
    public static DateTimeOffset? TryRead(JsonElement lineRoot)
    {
        if (lineRoot.ValueKind == JsonValueKind.Object
            && lineRoot.TryGetProperty("timestamp", out var value)
            && value.ValueKind == JsonValueKind.String
            && DateTimeOffset.TryParse(value.GetString(), out var parsed))
        {
            return parsed;
        }

        return null;
    }

    public static string Format(DateTimeOffset? timestamp) =>
        timestamp is { } value ? value.ToLocalTime().ToString("HH:mm:ss") : string.Empty;
}
