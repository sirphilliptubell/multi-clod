using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MultiClod.App.Costs;
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

    public SessionLogWindow(SessionNodeViewModel session, SessionCostMonitorService costMonitor)
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
        this.Closed += (_, _) =>
        {
            this.subagentWatcher?.Dispose();
            this.costMonitor.SubagentFileCostUpdated -= this.OnSubagentFileCostUpdated;
        };

        this.StartWatchingSubagents();
        this.SelectMainSession();
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

        var projectDir = Path.GetDirectoryName(this.mainSessionFilePath)!;
        var sessionDir = Path.Combine(projectDir, this.session.ClaudeSessionId.ToString());

        var watcher = new SubagentTranscriptWatcher(sessionDir);
        watcher.SubagentDiscovered += this.OnSubagentDiscovered;
        this.subagentWatcher = watcher;
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
}
