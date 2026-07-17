using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using MultiClod.App.Costs;

namespace MultiClod.App;

/// <summary>
/// Base for a node in the Project/Session tree bound to the TreeView. <see cref="ProjectNodeViewModel"/>
/// and <see cref="SessionNodeViewModel"/> add their own identity and state on top of the shared
/// name/children/parent bookkeeping here.
/// </summary>
public abstract class TreeNodeViewModel : INotifyPropertyChanged
{
    private string name;
    private bool isSelected;
    private bool isExpanded;

    protected TreeNodeViewModel(string name)
    {
        this.name = name;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<TreeNodeViewModel> Children { get; } = new();

    public TreeNodeViewModel? Parent { get; internal set; }

    public string Name
    {
        get => this.name;
        set => this.SetField(ref this.name, value);
    }

    // Bound two-way against TreeViewItem.IsSelected/IsExpanded via MainWindow.xaml's TreeViewItem
    // style, so WPF applies the value whenever the container materializes instead of MainWindow
    // having to poll ItemContainerGenerator for it.
    public bool IsSelected
    {
        get => this.isSelected;
        set => this.SetField(ref this.isSelected, value);
    }

    public bool IsExpanded
    {
        get => this.isExpanded;
        set => this.SetField(ref this.isExpanded, value);
    }

    // Defaults to "no cost data at all" for every node type, including ProjectNodeViewModel - the
    // shared TreeViewItem ControlTemplate binds a CostBadge control's CostText against whatever
    // node is bound (project or session), so a project row needs a safe, always-null answer rather
    // than a binding error. SessionNodeViewModel is the only node type that ever overrides this
    // with real backing state (see its UpdateCostSummary). A null/empty CostBadgeText is itself
    // enough for CostBadge to hide - no separate Has* flag needed.
    internal virtual SessionCostSummary CostSummary => SessionCostSummary.NoData;

    public virtual string? CostBadgeText => SessionCostAggregator.FormatBadge(this.CostSummary);

    // Per-model "slug: $X.XX" lines, most expensive first, unpriced models last - bound to
    // CostBadge's tooltip so hovering the badge explains what actually contributed to the total.
    public virtual string? CostBreakdownText => SessionCostAggregator.FormatBreakdown(this.CostSummary);

    protected void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (Equals(field, value))
        {
            return;
        }

        field = value;
        this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    protected void RaisePropertyChanged(string propertyName)
    {
        this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
