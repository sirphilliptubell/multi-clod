using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using MultiClod.App.SessionLog.Rendering;

namespace MultiClod.App.SessionLog.Tree;

/// <summary>
/// The Tree body: a zoom/pan canvas showing the whole session (main + every subagent) as one graph,
/// plus a right-docked detail pane reusing the List view's own row templates. Snapshot + manual
/// refresh only (decision D7) - BuildSnapshot rebuilds the whole TreeGraph from disk; nothing here
/// live-tails. See specs/session-log-tree-view.md for the full design rationale behind every
/// decision referenced in these comments.
/// </summary>
public partial class SessionTreeView : UserControl, IDisposable
{
    private const double ZoomStepFactor = 1.1;
    private const double WheelPanPixelsPerNotch = 40.0;
    private const double MinimumZoomFloor = 0.05;
    private const double DragThresholdPixels = 4.0;
    private static readonly TimeSpan BannerFadeDuration = TimeSpan.FromMilliseconds(200);

    private readonly ConnectorOverlay connectorOverlay;
    private string? mainFilePath;
    private string? sessionDir;
    private TreeGraph? graph;
    private TreeSnapshotWatcher? snapshotWatcher;
    private BoxNode? selectedBox;
    private double currentScale = 1.0;
    private double fitScale = 1.0;
    private Point? dragStartMouse;
    private double dragStartHOffset;
    private double dragStartVOffset;
    private bool isDragPanning;

    public SessionTreeView()
    {
        this.InitializeComponent();

        this.connectorOverlay = new ConnectorOverlay();
        this.GraphCanvas.Children.Insert(0, this.connectorOverlay);

        this.CommandBindings.Add(new CommandBinding(SessionLogCommands.CopyEntryJson, this.OnCopyEntryJsonExecuted));
    }

    // Called once when the user first switches to Tree mode, and again if the underlying session
    // is re-pointed (e.g. /clear, /resume changes ClaudeSessionId) - mirrors
    // TranscriptViewerControl.SetSource's "fully reset for an explicit source change" behavior.
    public void Initialize(string newMainFilePath, string newSessionDir)
    {
        this.mainFilePath = newMainFilePath;
        this.sessionDir = newSessionDir;
        this.BuildSnapshot(preserveView: false);
    }

    public void Dispose()
    {
        this.snapshotWatcher?.Dispose();
        this.snapshotWatcher = null;
    }

    // Rebuilds the whole graph from disk. preserveView is true for Refresh and the "Show all
    // events" toggle (decision D9's rebuild-on-toggle note) - the user's zoom/scroll/selection
    // survive instead of jumping back to the origin; it's false only for the very first build.
    private void BuildSnapshot(bool preserveView)
    {
        if (this.mainFilePath is null || this.sessionDir is null)
        {
            return;
        }

        if (!File.Exists(this.mainFilePath))
        {
            this.WaitingText.Visibility = Visibility.Visible;
            this.ViewPort.Visibility = Visibility.Collapsed;
            return;
        }

        this.WaitingText.Visibility = Visibility.Collapsed;
        this.ViewPort.Visibility = Visibility.Visible;

        var savedState = preserveView ? this.CaptureViewState() : null;

        this.HideRefreshBanner();
        this.snapshotWatcher?.Dispose();
        this.snapshotWatcher = null;

        var showAllEvents = this.ShowAllEventsCheckBox.IsChecked == true;
        var agents = TreeGraphBuilder.Build(this.mainFilePath, this.sessionDir, showAllEvents);
        this.graph = TreeLayoutEngine.Layout(agents, TreeLayoutEngine.DefaultMetrics);

        this.PopulateCanvas();

        // Deferred to Loaded priority so ViewPort/GraphCanvas complete a layout pass first -
        // ActualWidth/Height (needed for the fit-to-viewport zoom floor) would otherwise still
        // reflect a stale size, e.g. right after the List/Tree toggle just made this control
        // visible for the first time.
        this.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => this.ApplyViewState(savedState)));

        var knownAgentFiles = agents.Where(a => a.AgentId != AgentNode.MainAgentId).Select(a => a.FilePath).ToList();
        var watcher = new TreeSnapshotWatcher(this.mainFilePath, this.sessionDir, knownAgentFiles);
        watcher.ChangesPending += () => this.Dispatcher.Invoke(this.ShowRefreshBanner);
        this.snapshotWatcher = watcher;
    }

    private void PopulateCanvas()
    {
        if (this.graph is not { } currentGraph)
        {
            return;
        }

        this.connectorOverlay.Width = currentGraph.PixelExtent.Width;
        this.connectorOverlay.Height = currentGraph.PixelExtent.Height;
        this.connectorOverlay.SetConnectors(currentGraph.Connectors);

        this.BoxesItemsControl.ItemsSource = currentGraph.Boxes;
    }

    private sealed record ViewState(double Scale, double HorizontalOffset, double VerticalOffset, string? SelectedAgentId, int SelectedOrdinal);

    private ViewState CaptureViewState() => new(
        this.currentScale,
        this.ViewPort.HorizontalOffset,
        this.ViewPort.VerticalOffset,
        this.selectedBox?.Owner.AgentId,
        this.selectedBox?.SourceLineOrdinal ?? -1);

    private void ApplyViewState(ViewState? saved)
    {
        this.fitScale = this.ComputeFitScale();

        if (saved is { } state)
        {
            this.SetScale(state.Scale);
            this.ViewPort.ScrollToHorizontalOffset(state.HorizontalOffset);
            this.ViewPort.ScrollToVerticalOffset(state.VerticalOffset);

            var matched = state.SelectedAgentId is { } agentId
                ? this.graph?.Boxes.FirstOrDefault(b => b.Owner.AgentId == agentId && b.SourceLineOrdinal == state.SelectedOrdinal)
                : null;
            this.SelectBox(matched);
        }
        else
        {
            this.SetScale(1.0);
            this.ViewPort.ScrollToHorizontalOffset(0);
            this.ViewPort.ScrollToVerticalOffset(0);
            this.SelectBox(null);
        }
    }

    // min(viewportWidth/extentWidth, viewportHeight/extentHeight), hard-floored - this is the
    // zoom-out limit (decision D8): you can always pull back far enough to see the entire graph,
    // never further.
    private double ComputeFitScale()
    {
        if (this.graph is not { } currentGraph || currentGraph.PixelExtent.Width <= 0 || currentGraph.PixelExtent.Height <= 0)
        {
            return 1.0;
        }

        var viewportWidth = this.ViewPort.ActualWidth > 0 ? this.ViewPort.ActualWidth : this.ViewPort.ViewportWidth;
        var viewportHeight = this.ViewPort.ActualHeight > 0 ? this.ViewPort.ActualHeight : this.ViewPort.ViewportHeight;

        if (viewportWidth <= 0 || viewportHeight <= 0)
        {
            return 1.0;
        }

        var fit = Math.Min(viewportWidth / currentGraph.PixelExtent.Width, viewportHeight / currentGraph.PixelExtent.Height);
        return Math.Max(fit, MinimumZoomFloor);
    }

    // Keeps GraphCanvas.Width/Height equal to pixelExtent * scale - see the XAML file's remarks on
    // why this is what makes ScrollViewer's own clamping match the render-transformed visual size.
    private void SetScale(double scale)
    {
        this.currentScale = Math.Clamp(scale, Math.Min(this.fitScale, 1.0), 1.0);
        this.ZoomTransform.ScaleX = this.currentScale;
        this.ZoomTransform.ScaleY = this.currentScale;

        if (this.graph is { } currentGraph)
        {
            this.GraphCanvas.Width = currentGraph.PixelExtent.Width * this.currentScale;
            this.GraphCanvas.Height = currentGraph.PixelExtent.Height * this.currentScale;
        }
    }

    private void OnViewPortPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        e.Handled = true;

        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            this.ZoomAtCursor(e.Delta > 0 ? ZoomStepFactor : 1.0 / ZoomStepFactor, e.GetPosition(this.GraphCanvas), e.GetPosition(this.ViewPort));
            return;
        }

        var delta = e.Delta / 120.0 * WheelPanPixelsPerNotch;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            this.ViewPort.ScrollToHorizontalOffset(this.ViewPort.HorizontalOffset - delta);
        }
        else
        {
            this.ViewPort.ScrollToVerticalOffset(this.ViewPort.VerticalOffset - delta);
        }
    }

    // Cursor-anchored zoom: pointInGraphCanvas is already in UNSCALED content coordinates (WPF
    // resolves GetPosition against an element's own local space, independent of that element's own
    // RenderTransform) - after rescaling, we re-derive the scroll offsets that keep that same
    // content point under pointInViewport (the cursor's screen-relative position).
    private void ZoomAtCursor(double factor, Point pointInGraphCanvas, Point pointInViewport)
    {
        if (this.graph is null)
        {
            return;
        }

        var newScale = Math.Clamp(this.currentScale * factor, Math.Min(this.fitScale, 1.0), 1.0);
        this.SetScale(newScale);

        this.ViewPort.ScrollToHorizontalOffset(pointInGraphCanvas.X * newScale - pointInViewport.X);
        this.ViewPort.ScrollToVerticalOffset(pointInGraphCanvas.Y * newScale - pointInViewport.Y);
    }

    private void OnResetViewClicked(object sender, RoutedEventArgs e)
    {
        this.SetScale(1.0);
        this.ViewPort.ScrollToHorizontalOffset(0);
        this.ViewPort.ScrollToVerticalOffset(0);
    }

    private void OnShowAllEventsChanged(object sender, RoutedEventArgs e)
    {
        this.BuildSnapshot(preserveView: true);
    }

    private void OnRefreshBannerClicked(object sender, MouseButtonEventArgs e)
    {
        this.BuildSnapshot(preserveView: true);
    }

    private void ShowRefreshBanner()
    {
        this.RefreshBanner.Visibility = Visibility.Visible;
        this.RefreshBanner.BeginAnimation(OpacityProperty, new DoubleAnimation(1.0, BannerFadeDuration));
    }

    private void HideRefreshBanner()
    {
        var animation = new DoubleAnimation(0.0, BannerFadeDuration);
        animation.Completed += (_, _) => this.RefreshBanner.Visibility = Visibility.Collapsed;
        this.RefreshBanner.BeginAnimation(OpacityProperty, animation);
    }

    // Mouse-drag pan (any direction) - tracked on the same ScrollViewer the wheel handlers use.
    // Distinguishes a genuine box click from the start of a drag via DragThresholdPixels, so
    // clicking a box still selects it (see OnViewPortPreviewMouseLeftButtonUp) instead of every
    // click being swallowed as a zero-distance "drag".
    private void OnViewPortPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        this.dragStartMouse = e.GetPosition(this.ViewPort);
        this.dragStartHOffset = this.ViewPort.HorizontalOffset;
        this.dragStartVOffset = this.ViewPort.VerticalOffset;
        this.isDragPanning = false;
    }

    private void OnViewPortPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (this.dragStartMouse is not { } start || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var current = e.GetPosition(this.ViewPort);
        var offset = current - start;

        if (!this.isDragPanning && offset.Length < DragThresholdPixels)
        {
            return;
        }

        if (!this.isDragPanning)
        {
            this.isDragPanning = true;
            this.ViewPort.CaptureMouse();
        }

        this.ViewPort.ScrollToHorizontalOffset(this.dragStartHOffset - offset.X);
        this.ViewPort.ScrollToVerticalOffset(this.dragStartVOffset - offset.Y);
    }

    private void OnViewPortPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        var wasDragging = this.isDragPanning;

        if (this.isDragPanning)
        {
            this.ViewPort.ReleaseMouseCapture();
        }

        this.dragStartMouse = null;
        this.isDragPanning = false;

        if (!wasDragging)
        {
            this.TrySelectBoxAt(e.OriginalSource as DependencyObject);
        }
    }

    private void TrySelectBoxAt(DependencyObject? source)
    {
        var current = source;
        while (current is not null)
        {
            if (current is FrameworkElement { DataContext: BoxNode box })
            {
                this.SelectBox(box);
                return;
            }

            current = VisualTreeHelper.GetParent(current);
        }
    }

    private void SelectBox(BoxNode? box)
    {
        if (this.selectedBox is { } previous)
        {
            previous.IsSelected = false;
        }

        this.selectedBox = box;

        if (box is { } selected)
        {
            selected.IsSelected = true;
            selected.RowVm.IsExpanded = true;
            this.DetailHost.Content = selected.RowVm;
        }
        else
        {
            this.DetailHost.Content = null;
        }
    }

    private void OnCopyEntryJsonExecuted(object sender, ExecutedRoutedEventArgs e)
    {
        if (e.Parameter is TranscriptRowViewModel row)
        {
            Clipboard.SetText(row.CopyableJson);
        }
    }
}
