using System.ComponentModel;
using MultiClod.App.Costs;

namespace MultiClod.App.SessionLog;

/// <summary>
/// One entry in a SessionLogWindow's left-panel source picker - either the pinned "Main Session"
/// entry or one discovered subagent transcript. Selecting one points
/// TranscriptViewerControl.SetSource at FilePath. CostSummary is mutable (unlike the rest of this
/// otherwise-immutable class) because SessionLogWindow updates it live as
/// SessionCostMonitorService reports new per-file totals - see UpdateCostSummary.
/// </summary>
public sealed class SessionLogSourceViewModel : INotifyPropertyChanged
{
    private SessionCostSummary costSummary = SessionCostSummary.NoData;

    public SessionLogSourceViewModel(string displayName, string filePath, DateTime createdAtUtc)
    {
        this.DisplayName = displayName;
        this.FilePath = filePath;
        this.CreatedAtUtc = createdAtUtc;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string DisplayName { get; }

    public string FilePath { get; }

    public DateTime CreatedAtUtc { get; }

    internal SessionCostSummary CostSummary => this.costSummary;

    public string? CostBadgeText => SessionCostAggregator.FormatBadge(this.costSummary);

    internal void UpdateCostSummary(SessionCostSummary summary)
    {
        this.costSummary = summary;
        this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(this.CostSummary)));
        this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(this.CostBadgeText)));
    }
}
