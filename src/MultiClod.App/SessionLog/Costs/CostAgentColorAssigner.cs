namespace MultiClod.App.SessionLog.Costs;

/// <summary>
/// Assigns each agent a stable color for the lifetime of one Costs session: main always gets a
/// fixed green (not part of the cycling palette), every subagent gets the next SessionLogPalette
/// entry in first-seen/spawn order (the same order SubagentTranscriptWatcher.SubagentDiscovered
/// fires - pre-existing files by creation time, then live-discovered ones), cycling back to the
/// start once the palette is exhausted.
/// </summary>
internal sealed class CostAgentColorAssigner
{
    public const string MainColorHex = "#3AA76D";

    private int nextIndex;

    public string AssignNext() =>
        SessionLogPalette.ConnectorPalette[this.nextIndex++ % SessionLogPalette.ConnectorPalette.Count];
}
