namespace MultiClod.App.Deeplink;

/// <summary>
/// Reported by DeeplinkSourceFetcher while it fetches - TotalBytes is null when unknown (no
/// Content-Length header), which DeeplinkProgressWindow renders as an indeterminate bar instead of
/// a percentage.
/// </summary>
public readonly record struct DeeplinkFetchProgress(long BytesRead, long? TotalBytes);
