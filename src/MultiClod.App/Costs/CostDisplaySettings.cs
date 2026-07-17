using System.ComponentModel;

namespace MultiClod.App.Costs;

/// <summary>
/// Single app-wide "show cost badges" flag, bound (via CostVisibilityConverter) from every cost
/// TextBlock in MainWindow.xaml, SessionLogWindow.xaml, and TranscriptCategoryStyles.xaml - a plain
/// INotifyPropertyChanged singleton rather than plumbing this through every SessionNodeViewModel/
/// SessionLogSourceViewModel/TranscriptRowViewModel instance (there can be thousands of the last
/// one across a long transcript). WPF's binding engine refreshes every bound element automatically
/// the instant ShowCosts changes here, with no per-instance subscription needed on either side.
/// MainWindow.xaml.cs is the only writer: it seeds this from AppSettings.ShowCosts at startup and
/// updates it (plus persisting) whenever the Settings toggle changes.
/// </summary>
public sealed class CostDisplaySettings : INotifyPropertyChanged
{
    public static readonly CostDisplaySettings Instance = new();

    private bool showCosts = true;

    private CostDisplaySettings()
    {
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool ShowCosts
    {
        get => this.showCosts;
        set
        {
            if (this.showCosts == value)
            {
                return;
            }

            this.showCosts = value;
            this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(this.ShowCosts)));
        }
    }
}
