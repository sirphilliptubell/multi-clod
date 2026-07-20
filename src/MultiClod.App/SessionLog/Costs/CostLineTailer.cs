using MultiClod.App.SessionLog.Parsing;
using MultiClod.App.SessionLog.Tailing;

namespace MultiClod.App.SessionLog.Costs;

/// <summary>
/// Per-file glue between a live TranscriptFileTailer and a CostSeriesTimeline. A raw line that
/// fails TranscriptLineParser.Parse, or that CostSeriesTimeline.TryAppend otherwise declines to
/// turn into a point, is silently skipped - the same posture SessionCostFileProcessor.ProcessLine
/// already has ("contributes nothing and never faults the file"). File-level IO errors are already
/// fully handled inside the wrapped TranscriptFileTailer itself; no additional handling here.
/// </summary>
internal sealed class CostLineTailer : IDisposable
{
    private readonly TranscriptFileTailer tailer;

    public CostLineTailer(string filePath, string agentId, bool includeAllLines)
    {
        this.Series = new CostSeriesTimeline(agentId, filePath);
        this.tailer = new TranscriptFileTailer(filePath);
        this.tailer.LinesAvailable += rawLines =>
        {
            List<CostTimelinePoint>? newPoints = null;
            foreach (var rawLine in rawLines)
            {
                var parsed = TranscriptLineParser.Parse(rawLine);
                if (this.Series.TryAppend(parsed, includeAllLines) is { } point)
                {
                    (newPoints ??= new List<CostTimelinePoint>()).Add(point);
                }
            }

            if (newPoints is { Count: > 0 })
            {
                this.PointsAvailable?.Invoke(newPoints);
            }
        };
    }

    public CostSeriesTimeline Series { get; }

    // Raised on the wrapped tailer's own background thread - caller marshals to the UI thread.
    public event Action<IReadOnlyList<CostTimelinePoint>>? PointsAvailable;

    public void Dispose() => this.tailer.Dispose();
}
