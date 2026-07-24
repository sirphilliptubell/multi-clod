using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MultiClod.App.Native;

namespace MultiClod.App.Deeplink;

/// <summary>
/// Read-only viewer for an imported deeplink session zip - structurally similar to
/// SessionLogWindow (left picker + right TranscriptViewerControl, same "Explore to" context-menu
/// pattern) but takes an already-classified ClassifiedImportContents and does no I/O of its own.
/// Never live-tails: every file it points TranscriptViewerControl/PlainTextFileViewer at is a
/// static extracted copy that will never grow.
/// </summary>
public partial class DeeplinkImportWindow : Window
{
    private enum ViewMode
    {
        Sessions,
        OtherFiles,
    }

    public DeeplinkImportWindow(string source, ClassifiedImportContents contents)
    {
        this.InitializeComponent();
        this.Title = $"Session Log (Imported) - {source}";

        var sessionItems = BuildSessionTree(contents.Sessions);
        this.SessionsTreeView.ItemsSource = sessionItems;

        this.OtherFilesListBox.ItemsSource = contents.OtherFilePaths
            .Select(path => new DeeplinkOtherFileItem(Path.GetFileName(path), path))
            .ToList();

        this.Closed += (_, _) => this.Viewer.SetSource(null);

        // No sessions (a zip with only "other files") lands directly on that tab instead of an
        // empty Sessions view - see ImportZipClassifier, a zip with only other files is valid.
        this.SetViewMode(sessionItems.Count > 0 ? ViewMode.Sessions : ViewMode.OtherFiles);

        if (sessionItems.Count > 0)
        {
            this.Viewer.SetSource(sessionItems[0].FilePath);
        }
    }

    private static List<DeeplinkSessionTreeItem> BuildSessionTree(IReadOnlyList<DeeplinkImportedSession> sessions)
    {
        var items = new List<DeeplinkSessionTreeItem>();
        foreach (var session in sessions)
        {
            var children = session.SubagentFilePaths
                .Select(path => DeeplinkSessionTreeItem.Leaf(Path.GetFileName(path), path))
                .ToList();
            items.Add(new DeeplinkSessionTreeItem(session.DisplayLabel, session.MainFilePath, children));
        }

        return items;
    }

    private void OnSessionsModeClicked(object sender, RoutedEventArgs e) => this.SetViewMode(ViewMode.Sessions);

    private void OnOtherFilesModeClicked(object sender, RoutedEventArgs e) => this.SetViewMode(ViewMode.OtherFiles);

    private void SetViewMode(ViewMode mode)
    {
        this.SessionsBodyRoot.Visibility = mode == ViewMode.Sessions ? Visibility.Visible : Visibility.Collapsed;
        this.OtherFilesBodyRoot.Visibility = mode == ViewMode.OtherFiles ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnSessionTreeSelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is DeeplinkSessionTreeItem item)
        {
            this.Viewer.SetSource(item.FilePath);
        }
    }

    private void OnOtherFileSelected(object sender, SelectionChangedEventArgs e)
    {
        if (this.OtherFilesListBox.SelectedItem is DeeplinkOtherFileItem item)
        {
            this.OtherFileViewer.SetSource(item.FilePath);
        }
    }

    private void OnSessionsTreeMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (ContextMenuHelper.FindAncestor<TreeViewItem>(e.OriginalSource as DependencyObject) is { } item)
        {
            item.IsSelected = true;
        }
    }

    private void OnSessionsTreeContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        this.SessionsTreeContextMenu.Items.Clear();

        if (this.SessionsTreeView.SelectedItem is not DeeplinkSessionTreeItem item)
        {
            return;
        }

        this.SessionsTreeContextMenu.Items.Add(ContextMenuHelper.CreateMenuItem("Explore to", () => ExploreTo(item.FilePath)));
        this.SessionsTreeContextMenu.Items.Add(ContextMenuHelper.CreateMenuItem("Copy Full Path", () => Clipboard.SetText(item.FilePath)));
    }

    private void OnOtherFilesListMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        var item = ContextMenuHelper.FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject);
        if (item is not null && !item.IsSelected)
        {
            item.IsSelected = true;
        }
    }

    private void OnOtherFilesListContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        this.OtherFilesContextMenu.Items.Clear();

        if (this.OtherFilesListBox.SelectedItem is not DeeplinkOtherFileItem item)
        {
            return;
        }

        this.OtherFilesContextMenu.Items.Add(ContextMenuHelper.CreateMenuItem("Explore to", () => ExploreTo(item.FilePath)));
        this.OtherFilesContextMenu.Items.Add(ContextMenuHelper.CreateMenuItem("Copy Full Path", () => Clipboard.SetText(item.FilePath)));
    }

    private static void ExploreTo(string filePath) => WindowsExplorer.OpenAndSelect(filePath);
}
