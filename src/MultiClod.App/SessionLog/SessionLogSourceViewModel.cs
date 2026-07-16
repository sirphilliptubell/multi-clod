namespace MultiClod.App.SessionLog;

/// <summary>
/// One entry in a SessionLogWindow's left-panel source picker - either the pinned "Main Session"
/// entry or one discovered subagent transcript. Selecting one points
/// TranscriptViewerControl.SetSource at FilePath.
/// </summary>
public sealed class SessionLogSourceViewModel
{
    public SessionLogSourceViewModel(string displayName, string filePath, DateTime createdAtUtc)
    {
        this.DisplayName = displayName;
        this.FilePath = filePath;
        this.CreatedAtUtc = createdAtUtc;
    }

    public string DisplayName { get; }

    public string FilePath { get; }

    public DateTime CreatedAtUtc { get; }
}
