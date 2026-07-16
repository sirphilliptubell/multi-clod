using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MultiClod.App.Import;
using MultiClod.App.Native;

namespace MultiClod.App;

public partial class ImportSessionWindow : Window
{
    private readonly ClaudeSessionSearchService searchService;
    private readonly ObservableCollection<ClaudeSessionSearchResult> results = new();
    private CancellationTokenSource? searchCts;

    public ImportSessionWindow(string? projectsRootOverride = null)
    {
        this.InitializeComponent();
        DarkTitleBar.Apply(this);

        var root = projectsRootOverride
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "projects");
        this.searchService = new ClaudeSessionSearchService(root);
        this.ResultsGrid.ItemsSource = this.results;

        // Cancels an in-flight scan rather than letting it run to completion against a window
        // that's already gone.
        this.Closed += (_, _) => this.searchCts?.Cancel();
        this.Loaded += (_, _) => this.SearchBox.Focus();
    }

    internal ClaudeSessionSearchResult? SelectedResult { get; private set; }

    private async void OnSearchClick(object sender, RoutedEventArgs e)
    {
        var text = this.SearchBox.Text.Trim();
        if (text.Length == 0)
        {
            this.StatusText.Text = "Type one or more words to search for.";
            return;
        }

        this.searchCts?.Cancel();
        this.searchCts?.Dispose();
        var cts = new CancellationTokenSource();
        this.searchCts = cts;

        this.SearchButton.IsEnabled = false;
        this.ImportButton.IsEnabled = false;
        this.results.Clear();
        this.StatusText.Text = "Searching...";

        // Progress<T> captures the UI thread's SynchronizationContext at construction (here, on
        // the UI thread), so reports from the search's worker threads marshal back automatically.
        var progress = new Progress<(int FilesScanned, int TotalFiles)>(p => this.StatusText.Text = $"Scanning... {p.FilesScanned}/{p.TotalFiles}");

        try
        {
            var found = await this.searchService.SearchAsync(text, cts.Token, progress);

            // Sorted desc by LastModified by default (most recently touched session first) -
            // ModifiedColumn's SortDirection="Descending" in XAML is just the matching header
            // arrow; the DataGrid's built-in column-header sort still takes over from here if the
            // user clicks any column.
            foreach (var result in found.OrderByDescending(r => r.LastModified))
            {
                this.results.Add(result);
            }

            this.StatusText.Text = found.Count == 0 ? "No matches found." : $"{found.Count} match(es) found.";
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer search, or the window is closing - nothing to show.
        }
        finally
        {
            this.SearchButton.IsEnabled = true;
        }
    }

    private void OnSearchBoxKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            this.OnSearchClick(sender, e);
        }
    }

    private void OnResultsSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        this.ImportButton.IsEnabled = this.ResultsGrid.SelectedItem is not null;
    }

    private void OnImportClick(object sender, RoutedEventArgs e)
    {
        this.Confirm();
    }

    private void OnResultsDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (this.ResultsGrid.SelectedItem is not null)
        {
            this.Confirm();
        }
    }

    private void Confirm()
    {
        if (this.ResultsGrid.SelectedItem is ClaudeSessionSearchResult result)
        {
            this.SelectedResult = result;
            this.DialogResult = true;
        }
    }
}
