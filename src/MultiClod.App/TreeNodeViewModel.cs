using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

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
