using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MultiClod.App.Costs;
using MultiClod.App.Native;
using MultiClod.App.Validation;

namespace MultiClod.App.SessionLog;

/// <summary>
/// Non-modal per-session log window: left panel picks a source (the pinned Main Session entry, or
/// a live-discovered subagent transcript), right panel is the reusable TranscriptViewerControl for
/// whichever source is selected. Holds the live SessionNodeViewModel (not a path snapshot) so it
/// can re-point Main Session if ClaudeSessionId changes mid-session (e.g. /clear, /resume).
/// </summary>
public partial class SessionLogWindow : Window
{
    private readonly SessionNodeViewModel session;
    private readonly SessionCostMonitorService costMonitor;
    private readonly ObservableCollection<SessionLogSourceViewModel> subagents = new();
    private SessionLogSourceViewModel mainSessionSource;
    private SubagentTranscriptWatcher? subagentWatcher;
    private string mainSessionFilePath;
    private bool isMainSessionSelected = true;
    private bool isTreeInitialized;
    private ViewMode currentViewMode = ViewMode.List;

    private enum ViewMode
    {
        List,
        Tree,
        Costs,
    }

    public SessionLogWindow(SessionNodeViewModel session, SessionCostMonitorService costMonitor, bool startInCostsView = false)
    {
        this.InitializeComponent();

        this.session = session;
        this.costMonitor = costMonitor;
        this.Title = $"Session Log - {session.DisplayTitle}";
        this.mainSessionFilePath = ClaudeProjectPath.GetSessionFilePath(session.WorkingDirectory, session.ClaudeSessionId);
        this.mainSessionSource = this.CreateMainSessionSource();
        this.MainSessionHeaderPanel.DataContext = this.mainSessionSource;

        this.SubagentsListBox.ItemsSource = this.subagents;
        this.session.PropertyChanged += this.OnSessionPropertyChanged;
        this.costMonitor.SubagentFileCostUpdated += this.OnSubagentFileCostUpdated;
        this.CostsView.NavigateToListRequested += (filePath, lineOrdinal) => this.Dispatcher.Invoke(() => this.NavigateToListEntry(filePath, lineOrdinal));
        this.Closed += (_, _) =>
        {
            this.subagentWatcher?.Dispose();
            this.TreeView.Dispose();
            this.CostsView.Dispose();
            this.costMonitor.SubagentFileCostUpdated -= this.OnSubagentFileCostUpdated;
        };

        this.StartWatchingSubagents();
        this.SelectMainSession();
        this.SetViewMode(startInCostsView ? ViewMode.Costs : ViewMode.List);
    }

    // Used by SessionLogWindowRegistry when "View Session Costs" targets a session whose log
    // window is already open - ShowOrFocus can't pass startInCostsView through the constructor in
    // that case, since the existing instance is reused rather than recreated.
    public void ShowCostsView()
    {
        this.SetViewMode(ViewMode.Costs);
    }

    // Seeded from the session node's own aggregate (already summed across main + all subagent
    // logs by SessionCostMonitorService) - Main Session mirrors the same total shown on the tree
    // badge, not just the main log file's own contribution.
    private SessionLogSourceViewModel CreateMainSessionSource()
    {
        var source = new SessionLogSourceViewModel("Main Session", this.mainSessionFilePath, DateTime.MinValue);
        source.UpdateCostSummary(this.session.CostSummary);
        return source;
    }

    // A subagent file may already have been processed by the shared monitor before this window's
    // own SubagentTranscriptWatcher discovers it - seed from whatever's on disk in its sidecar so
    // there's no gap until the next SubagentFileCostUpdated event for that file.
    private static SessionCostSummary ReadSidecarSummary(string jsonlPath)
    {
        var sidecar = SessionCostCacheFile.TryLoad(SessionCostCacheFile.SidecarPathFor(jsonlPath));
        return sidecar is null ? SessionCostSummary.NoData : SessionCostAggregator.Aggregate([sidecar.ModelCostsUsd]);
    }

    private void OnSubagentFileCostUpdated(string filePath, SessionCostSummary summary)
    {
        this.Dispatcher.Invoke(() =>
        {
            foreach (var source in this.subagents)
            {
                if (string.Equals(source.FilePath, filePath, StringComparison.OrdinalIgnoreCase))
                {
                    source.UpdateCostSummary(summary);
                    return;
                }
            }
        });
    }

    private void StartWatchingSubagents()
    {
        this.subagentWatcher?.Dispose();
        this.subagents.Clear();

        var watcher = new SubagentTranscriptWatcher(this.GetSessionDir());
        watcher.SubagentDiscovered += this.OnSubagentDiscovered;
        this.subagentWatcher = watcher;
    }

    private string GetSessionDir()
    {
        var projectDir = Path.GetDirectoryName(this.mainSessionFilePath)!;
        return Path.Combine(projectDir, this.session.ClaudeSessionId.ToString());
    }

    private void OnListModeClicked(object sender, RoutedEventArgs e)
    {
        this.SetViewMode(ViewMode.List);
    }

    private void OnTreeModeClicked(object sender, RoutedEventArgs e)
    {
        this.SetViewMode(ViewMode.Tree);
    }

    private void OnCostsModeClicked(object sender, RoutedEventArgs e)
    {
        this.SetViewMode(ViewMode.Costs);
    }

    private void SetViewMode(ViewMode mode)
    {
        this.currentViewMode = mode;

        this.ListBodyRoot.Visibility = mode == ViewMode.List ? Visibility.Visible : Visibility.Collapsed;
        this.TreeView.Visibility = mode == ViewMode.Tree ? Visibility.Visible : Visibility.Collapsed;
        this.CostsView.Visibility = mode == ViewMode.Costs ? Visibility.Visible : Visibility.Collapsed;

        var selectedBrush = new SolidColorBrush(Color.FromRgb(0x28, 0x48, 0x61));
        this.ListModeButton.Background = mode == ViewMode.List ? selectedBrush : Brushes.Transparent;
        this.TreeModeButton.Background = mode == ViewMode.Tree ? selectedBrush : Brushes.Transparent;
        this.CostsModeButton.Background = mode == ViewMode.Costs ? selectedBrush : Brushes.Transparent;

        if (mode == ViewMode.Tree && !this.isTreeInitialized)
        {
            this.isTreeInitialized = true;
            this.TreeView.Initialize(this.mainSessionFilePath, this.GetSessionDir());
        }

        // Unlike List/Tree's lazy-init-once convention, Costs always fully resets (checkboxes,
        // zoom/pan, "show all events") every time the user switches into it - no lazy guard here.
        if (mode == ViewMode.Costs)
        {
            this.CostsView.Initialize(this.mainSessionFilePath, this.GetSessionDir());
        }
    }

    // Navigation target for CostsView's right-click "go to entry in List view" - switches to List
    // mode, re-points the Viewer at the point's own source (main or the specific subagent), and
    // scrolls to/flashes the matching row.
    private void NavigateToListEntry(string filePath, int lineOrdinal)
    {
        this.SetViewMode(ViewMode.List);

        if (string.Equals(filePath, this.mainSessionFilePath, StringComparison.OrdinalIgnoreCase))
        {
            this.SubagentsListBox.SelectedItem = null;
            this.SelectMainSession();
        }
        else
        {
            var match = this.subagents.FirstOrDefault(s => string.Equals(s.FilePath, filePath, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                this.SubagentsListBox.SelectedItem = match;
            }
        }

        this.Viewer.ScrollToAndHighlightLine(lineOrdinal);
    }

    private void OnSubagentDiscovered(SessionLogSourceViewModel source)
    {
        this.Dispatcher.Invoke(() =>
        {
            source.UpdateCostSummary(ReadSidecarSummary(source.FilePath));

            var insertIndex = 0;
            while (insertIndex < this.subagents.Count && this.subagents[insertIndex].CreatedAtUtc <= source.CreatedAtUtc)
            {
                insertIndex++;
            }

            this.subagents.Insert(insertIndex, source);
        });
    }

    private void OnSessionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SessionNodeViewModel.CostSummary))
        {
            this.Dispatcher.Invoke(() => this.mainSessionSource.UpdateCostSummary(this.session.CostSummary));
            return;
        }

        if (e.PropertyName != nameof(SessionNodeViewModel.ClaudeSessionId))
        {
            return;
        }

        this.Dispatcher.Invoke(() =>
        {
            this.mainSessionFilePath = ClaudeProjectPath.GetSessionFilePath(this.session.WorkingDirectory, this.session.ClaudeSessionId);

            // A new underlying jsonl (e.g. after /clear) means Main Session's own FilePath is
            // stale - recreate it rather than mutate a supposedly-immutable path in place.
            this.mainSessionSource = this.CreateMainSessionSource();
            this.MainSessionHeaderPanel.DataContext = this.mainSessionSource;

            this.StartWatchingSubagents();

            if (this.isMainSessionSelected)
            {
                this.Viewer.SetSource(this.mainSessionFilePath);
            }

            // Only re-point the Tree if it's already been initialized once (lazy - the user may
            // never have switched to Tree mode at all) - the next switch to Tree mode will
            // initialize it fresh with the current paths regardless.
            if (this.isTreeInitialized)
            {
                this.TreeView.Initialize(this.mainSessionFilePath, this.GetSessionDir());
            }

            // Costs has no lazy-once guard to check - it always re-Initializes on the next mode
            // switch regardless - but if it's the currently VISIBLE mode, it needs to re-point
            // immediately rather than silently going stale until the user switches away and back.
            if (this.currentViewMode == ViewMode.Costs)
            {
                this.CostsView.Initialize(this.mainSessionFilePath, this.GetSessionDir());
            }
        });
    }

    private void OnMainSessionSelected(object sender, MouseButtonEventArgs e)
    {
        this.SubagentsListBox.SelectedItem = null;
        this.SelectMainSession();
    }

    private void SelectMainSession()
    {
        this.isMainSessionSelected = true;
        this.MainSessionHeaderPanel.Background = new SolidColorBrush(Color.FromRgb(0x28, 0x48, 0x61));
        this.Viewer.SetSource(this.mainSessionFilePath);
    }

    private void OnSubagentSelected(object sender, SelectionChangedEventArgs e)
    {
        if (this.SubagentsListBox.SelectedItem is not SessionLogSourceViewModel source)
        {
            return;
        }

        this.isMainSessionSelected = false;
        this.MainSessionHeaderPanel.Background = Brushes.Transparent;
        this.Viewer.SetSource(source.FilePath);
    }

    private void OnMainSessionContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        this.MainSessionContextMenu.Items.Clear();
        this.MainSessionContextMenu.Items.Add(ContextMenuHelper.CreateMenuItem("Explore to", () => ExploreToSource(this.mainSessionSource)));
        this.MainSessionContextMenu.Items.Add(ContextMenuHelper.CreateMenuItem("Copy Full Path", () => Clipboard.SetText(this.mainSessionSource.FilePath)));
    }

    private void OnSubagentsListMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        var item = ContextMenuHelper.FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject);

        if (item is not null && !item.IsSelected)
        {
            item.IsSelected = true;
        }
    }

    private void OnSubagentsListContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        this.SubagentsContextMenu.Items.Clear();

        if (this.SubagentsListBox.SelectedItem is not SessionLogSourceViewModel source)
        {
            return;
        }

        this.SubagentsContextMenu.Items.Add(ContextMenuHelper.CreateMenuItem("Explore to", () => ExploreToSource(source)));
        this.SubagentsContextMenu.Items.Add(ContextMenuHelper.CreateMenuItem("Copy Full Path", () => Clipboard.SetText(source.FilePath)));
    }

    // Main Session's file may not exist yet (window opens in a "waiting for session to start..."
    // state before the transcript file appears) - fall back to the nearest existing ancestor
    // folder rather than failing to launch explorer.exe at all.
    private static void ExploreToSource(SessionLogSourceViewModel source)
    {
        if (File.Exists(source.FilePath))
        {
            WindowsExplorer.OpenAndSelect(source.FilePath);
        }
        else
        {
            WindowsExplorer.OpenNearestExistingAncestorFolder(source.FilePath);
        }
    }
}
