using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using MultiClod.App.Costs;

namespace MultiClod.App.SessionLog.Costs;

/// <summary>
/// One node (main session, or one subagent) in the Costs view's hierarchy - built directly from
/// AgentMetaReader/AgentMeta.ParentAgentId (see CostTimelineController), not TreeGraphBuilder,
/// since a flat parent/child hierarchy needs none of the latter's spawn/tool_use_id box-matching.
/// </summary>
internal sealed class CostAgentNode : INotifyPropertyChanged
{
    private bool isVisible = true;
    private string totalCostText = string.Empty;

    public CostAgentNode(string agentId, string filePath, CostAgentNode? parent, string colorHex, string displayName)
    {
        this.AgentId = agentId;
        this.FilePath = filePath;
        this.Parent = parent;
        this.ColorHex = colorHex;
        this.ColorBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colorHex));
        this.ColorBrush.Freeze();
        this.DisplayName = displayName;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string AgentId { get; }

    public string FilePath { get; }

    public CostAgentNode? Parent { get; }

    public ObservableCollection<CostAgentNode> Children { get; } = new();

    public string ColorHex { get; }

    public Brush ColorBrush { get; }

    public string DisplayName { get; }

    // Set once, immediately after this node's CostLineTailer is created (null only for the brief
    // window between node construction and tailer creation in CostTimelineController).
    public CostSeriesTimeline? Timeline { get; internal set; }

    public bool IsVisible
    {
        get => this.isVisible;
        set => this.SetField(ref this.isVisible, value);
    }

    public string TotalCostText
    {
        get => this.totalCostText;
        private set => this.SetField(ref this.totalCostText, value);
    }

    // Refreshed once per CostsView redraw tick from this node's own CostByModel breakdown - not a
    // function of IsVisible, so the left panel always shows this agent's own total regardless of
    // whether its line is currently drawn.
    public void RefreshTotalCostText()
    {
        if (this.Timeline is null)
        {
            return;
        }

        var summary = SessionCostAggregator.Aggregate([this.Timeline.CostByModel]);
        this.TotalCostText = SessionCostAggregator.FormatBadge(summary) ?? CostFormatting.FormatKnownAmount(0m);
    }

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (Equals(field, value))
        {
            return;
        }

        field = value;
        this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
