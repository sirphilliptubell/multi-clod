namespace MultiClod.App.SessionLog.Costs;

/// <summary>
/// One plotted point for one agent's cost timeline. BucketKey is the point's timestamp rounded to
/// the nearest second (unix seconds) - the shared key CostBucketIndex resolves into an X-axis
/// column. LineOrdinal is the 0-based raw jsonl line this point came from within its own file
/// (FilePath), the same identity CostsView's right-click navigation hands to
/// TranscriptViewerControl.ScrollToAndHighlightLine.
/// </summary>
internal sealed record CostTimelinePoint(
    string AgentId,
    string FilePath,
    int LineOrdinal,
    long BucketKey,
    DateTimeOffset TimestampUtc,
    bool IsCostBearing,
    string? ModelSlug,
    decimal CumulativeUsd,
    decimal DeltaUsd,
    bool IsPriceKnown);
