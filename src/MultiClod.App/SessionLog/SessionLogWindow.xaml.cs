using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
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
    private readonly ObservableCollection<SessionLogSourceViewModel> subagents = new();
    private SubagentTranscriptWatcher? subagentWatcher;
    private string mainSessionFilePath;
    private bool isMainSessionSelected = true;

    public SessionLogWindow(SessionNodeViewModel session)
    {
        this.InitializeComponent();

        this.session = session;
        this.Title = $"Session Log - {session.DisplayTitle}";
        this.mainSessionFilePath = ClaudeProjectPath.GetSessionFilePath(session.WorkingDirectory, session.ClaudeSessionId);

        this.SubagentsListBox.ItemsSource = this.subagents;
        this.session.PropertyChanged += this.OnSessionPropertyChanged;
        this.Closed += (_, _) => this.subagentWatcher?.Dispose();

        this.StartWatchingSubagents();
        this.SelectMainSession();
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
        if (e.PropertyName != nameof(SessionNodeViewModel.ClaudeSessionId))
        {
            return;
        }

        this.Dispatcher.Invoke(() =>
        {
            this.mainSessionFilePath = ClaudeProjectPath.GetSessionFilePath(this.session.WorkingDirectory, this.session.ClaudeSessionId);
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
        this.MainSessionHeader.Background = new SolidColorBrush(Color.FromRgb(0x28, 0x48, 0x61));
        this.Viewer.SetSource(this.mainSessionFilePath);
    }

    private void OnSubagentSelected(object sender, SelectionChangedEventArgs e)
    {
        if (this.SubagentsListBox.SelectedItem is not SessionLogSourceViewModel source)
        {
            return;
        }

        this.isMainSessionSelected = false;
        this.MainSessionHeader.Background = Brushes.Transparent;
        this.Viewer.SetSource(source.FilePath);
    }
}
