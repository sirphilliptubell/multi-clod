namespace MultiClod.App.SessionLog.Rendering;

/// <summary>
/// Prefixes a row's summary body with its formatted timestamp, when it has one.
/// </summary>
internal static class TranscriptSummary
{
    public static string WithTimestamp(DateTimeOffset? timestamp, string body)
    {
        var formatted = TranscriptTimestamp.Format(timestamp);
        return formatted.Length == 0 ? body : $"{formatted}  {body}";
    }
}
