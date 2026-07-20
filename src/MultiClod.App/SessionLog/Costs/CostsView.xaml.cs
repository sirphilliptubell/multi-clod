using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using MultiClod.App.Costs;
using MultiClod.App.Native;
using ScottPlot;
using ScottPlot.WPF;

namespace MultiClod.App.SessionLog.Costs;

/// <summary>
/// The Costs mode body: a resizable hierarchical agent panel on the left, two X-locked ScottPlot
/// charts (cumulative cost, cost delta) plus a read-only model legend and crosshair tooltip on the
/// right. Unlike List/Tree's lazy-init-once convention, Initialize runs a full teardown-and-rebuild
/// every time - the whole view resets (visibility, zoom/pan, "show all events") each time the user
/// (re)opens Costs mode.
/// </summary>
public partial class CostsView : UserControl, IDisposable
{
    private const double ZoomStepFactor = 1.15;
    private const double MinimumSpanColumns = 1.0;
    private const double DragThresholdPixels = 4.0;

    private readonly ObservableCollection<CostAgentNode> rootItems = new();
    private readonly ObservableCollection<CostLegendEntry> legendEntries = new();
    private readonly DispatcherTimer redrawDebounce;

    private CostModelShapeAssigner shapeAssigner = new();
    private CostTimelineController? controller;
    private string? mainFilePath;
    private string? sessionDir;
    private bool suppressEvents;
    private double topYMax;
    private double bottomYMin;
    private double bottomYMax;
    private double xMin;
    private double xMax;
    private double lastKnownFullMaxColumn;
    private bool isFollowingLatest = true;
    private bool isHovering;
    private Pixel? dragStartMouse;
    private (double min, double max) dragStartRange;
    private bool isDragPanning;

    public CostsView()
    {
        this.InitializeComponent();

        this.AgentTree.ItemsSource = this.rootItems;
        this.LegendItemsControl.ItemsSource = this.legendEntries;

        this.redrawDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        this.redrawDebounce.Tick += (_, _) =>
        {
            this.redrawDebounce.Stop();
            this.RedrawTick();
        };

        // Frees up the plain WPF ContextMenu property (and lets our own custom X-only pan/zoom
        // drive SetLimitsX) instead of ScottPlot's own default mouse handling.
        this.TopPlot.UserInputProcessor.Disable();
        this.BottomPlot.UserInputProcessor.Disable();
    }

    public event Action<string, int>? NavigateToListRequested;

    // Called every time the user (re)opens Costs mode - never lazily-once like Tree. Resets the
    // "show all events" checkbox (which itself drives a full pipeline rebuild) so every reopen
    // starts from all-visible/full-auto-fit with no persisted state.
    public void Initialize(string mainFilePath, string sessionDir)
    {
        this.mainFilePath = mainFilePath;
        this.sessionDir = sessionDir;

        this.suppressEvents = true;
        this.ShowAllEventsCheckBox.IsChecked = false;
        this.suppressEvents = false;

        this.ReinitializePipeline(includeAllLines: false);
    }

    public void Dispose()
    {
        this.redrawDebounce.Stop();
        this.controller?.Dispose();
        this.controller = null;
    }

    private void ReinitializePipeline(bool includeAllLines)
    {
        if (this.mainFilePath is null || this.sessionDir is null)
        {
            return;
        }

        this.controller?.Dispose();
        this.controller = new CostTimelineController();
        this.controller.PointsAvailable += this.OnPointsAvailable;
        this.controller.AgentDiscovered += this.OnAgentDiscovered;

        this.shapeAssigner = new CostModelShapeAssigner();
        this.topYMax = 0;
        this.bottomYMin = 0;
        this.bottomYMax = 0;
        this.lastKnownFullMaxColumn = 0;
        this.isFollowingLatest = true;
        this.isHovering = false;
        this.HideTooltip();

        this.rootItems.Clear();
        this.legendEntries.Clear();

        this.TopPlot.Plot.Clear();
        this.BottomPlot.Plot.Clear();
        this.TopPlot.Plot.Axes.Rules.Clear();
        this.BottomPlot.Plot.Axes.Rules.Clear();
        this.TopPlot.Plot.Axes.Rules.Add(new FullRangeYAxisRule(this.TopPlot.Plot.Axes.Left, () => 0, () => this.topYMax));
        this.BottomPlot.Plot.Axes.Rules.Add(new FullRangeYAxisRule(this.BottomPlot.Plot.Axes.Left, () => this.bottomYMin, () => this.bottomYMax));

        // X is a column INDEX (see CostBucketIndex), never fractional - suppress ScottPlot's
        // default fractional tick labels (0, 0.2, 0.4, ...) in favor of whole-number ticks only.
        this.TopPlot.Plot.Axes.Bottom.TickGenerator = new ScottPlot.TickGenerators.NumericAutomatic { IntegerTicksOnly = true };
        this.BottomPlot.Plot.Axes.Bottom.TickGenerator = new ScottPlot.TickGenerators.NumericAutomatic { IntegerTicksOnly = true };

        this.controller.Initialize(this.mainFilePath, this.sessionDir, includeAllLines);
        this.WireNode(this.controller.MainNode);
        this.rootItems.Add(this.controller.MainNode);

        this.ApplyXRange(0, 0);
        this.RedrawTick();
    }

    // Raised on a background thread - marshal to the UI thread before touching topYMax/etc (read
    // by RedrawTick/FullRangeYAxisRule) or scheduling a redraw.
    private void OnPointsAvailable(IReadOnlyList<CostTimelinePoint> points)
    {
        this.Dispatcher.BeginInvoke(() =>
        {
            foreach (var point in points)
            {
                var cumulative = (double)point.CumulativeUsd;
                var delta = (double)point.DeltaUsd;

                if (cumulative > this.topYMax)
                {
                    this.topYMax = cumulative;
                }

                if (delta < this.bottomYMin)
                {
                    this.bottomYMin = delta;
                }

                if (delta > this.bottomYMax)
                {
                    this.bottomYMax = delta;
                }
            }

            this.ScheduleRedraw();
        });
    }

    // Raised on a background thread - the actual Children.Add must happen here, on the UI thread,
    // since CostAgentNode.Children is bound directly to a live TreeView (see
    // CostTimelineController.AgentDiscovered's own remarks on why it doesn't do this itself).
    private void OnAgentDiscovered(CostAgentNode node)
    {
        this.Dispatcher.BeginInvoke(() =>
        {
            this.WireNode(node);
            node.Parent?.Children.Add(node);
            this.ScheduleRedraw();
        });
    }

    private void WireNode(CostAgentNode node)
    {
        node.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(CostAgentNode.IsVisible))
            {
                this.ScheduleRedraw();
            }
        };
    }

    private void ScheduleRedraw()
    {
        if (!this.redrawDebounce.IsEnabled)
        {
            this.redrawDebounce.Start();
        }
    }

    private void RedrawTick()
    {
        if (this.controller is null)
        {
            return;
        }

        // Full rebuild every tick, not incremental - plottables are cheap to recreate at the
        // expected data volumes (a session's assistant turns, low thousands at most), and this
        // avoids any risk of accumulating stale/duplicate plottables across ticks. Plot.Axes.Rules
        // entries (the FullRangeYAxisRules) are NOT plottables and are unaffected by Clear() -
        // added once, in ReinitializePipeline. The zero line IS a plottable and must be re-added
        // every tick.
        this.TopPlot.Plot.Clear();
        this.BottomPlot.Plot.Clear();
        this.BottomPlot.Plot.Add.HorizontalLine(0, color: Color.FromHex("#666666"));

        var allNodes = Flatten(this.controller.MainNode).ToList();

        foreach (var node in allNodes)
        {
            node.RefreshTotalCostText();

            if (!node.IsVisible || node.Timeline is null)
            {
                continue;
            }

            var points = node.Timeline.EffectivePoints();
            if (points.Count == 0)
            {
                continue;
            }

            var xs = new double[points.Count];
            var cum = new double[points.Count];
            var delta = new double[points.Count];
            for (var i = 0; i < points.Count; i++)
            {
                xs[i] = this.controller.BucketIndex.TryGetColumn(points[i].BucketKey) ?? 0;
                cum[i] = (double)points[i].CumulativeUsd;
                delta[i] = (double)points[i].DeltaUsd;
            }

            var color = Color.FromHex(node.ColorHex);

            var topLine = this.TopPlot.Plot.Add.Scatter(xs, cum);
            topLine.Color = color;
            topLine.MarkerSize = 0;
            topLine.LineWidth = 2;

            var bottomLine = this.BottomPlot.Plot.Add.Scatter(xs, delta);
            bottomLine.Color = color;
            bottomLine.MarkerSize = 0;
            bottomLine.LineWidth = 2;

            var indexed = points.Select((point, index) => (Point: point, Index: index));
            foreach (var group in indexed.GroupBy(t => this.ShapeForPoint(t.Point)))
            {
                var groupList = group.ToList();
                var gxs = groupList.Select(t => xs[t.Index]).ToArray();
                var gCum = groupList.Select(t => cum[t.Index]).ToArray();
                var gDelta = groupList.Select(t => delta[t.Index]).ToArray();
                this.TopPlot.Plot.Add.Markers(gxs, gCum, group.Key, size: 8, color: color);
                this.BottomPlot.Plot.Add.Markers(gxs, gDelta, group.Key, size: 8, color: color);
            }
        }

        this.RefreshLegend(allNodes);

        this.lastKnownFullMaxColumn = Math.Max(0, this.controller.BucketIndex.ColumnCount - 1);
        if (this.isFollowingLatest && !this.isHovering)
        {
            this.ApplyXRange(0, this.lastKnownFullMaxColumn);
        }

        this.TopPlot.Refresh();
        this.BottomPlot.Refresh();
    }

    private MarkerShape ShapeForPoint(CostTimelinePoint point) =>
        point.IsCostBearing && point.ModelSlug is { } slug ? this.shapeAssigner.ShapeFor(slug) : MarkerShape.OpenCircle;

    private void RefreshLegend(IReadOnlyList<CostAgentNode> allNodes)
    {
        var visibleDicts = allNodes.Where(n => n.IsVisible && n.Timeline is not null).Select(n => n.Timeline!.CostByModel);
        var allDicts = allNodes.Where(n => n.Timeline is not null).Select(n => n.Timeline!.CostByModel);

        var visibleSummary = SessionCostAggregator.Aggregate(visibleDicts);
        var grandSummary = SessionCostAggregator.Aggregate(allDicts);

        this.legendEntries.Clear();
        foreach (var grandEntry in grandSummary.ModelBreakdown)
        {
            var visibleEntry = visibleSummary.ModelBreakdown.FirstOrDefault(e => e.ModelSlug == grandEntry.ModelSlug);
            var visibleAmount = visibleEntry.AmountUsd ?? 0m;
            var grandAmount = grandEntry.AmountUsd ?? 0m;
            var costText = visibleAmount == grandAmount
                ? CostFormatting.FormatKnownAmount(grandAmount)
                : $"{CostFormatting.FormatKnownAmount(visibleAmount)} / {CostFormatting.FormatKnownAmount(grandAmount)}";

            var shape = this.shapeAssigner.ShapeFor(grandEntry.ModelSlug);
            this.legendEntries.Add(new CostLegendEntry(CostGlyphs.For(shape), grandEntry.ModelSlug, costText));
        }
    }

    private static IEnumerable<CostAgentNode> Flatten(CostAgentNode root)
    {
        yield return root;
        foreach (var child in root.Children)
        {
            foreach (var descendant in Flatten(child))
            {
                yield return descendant;
            }
        }
    }

    private void OnShowAllEventsChanged(object sender, RoutedEventArgs e)
    {
        if (this.suppressEvents)
        {
            return;
        }

        this.ReinitializePipeline(this.ShowAllEventsCheckBox.IsChecked == true);
    }

    private void OnShowAllClicked(object sender, RoutedEventArgs e) => this.SetAllVisibility(true);

    private void OnHideAllClicked(object sender, RoutedEventArgs e) => this.SetAllVisibility(false);

    private void SetAllVisibility(bool visible)
    {
        if (this.controller is null)
        {
            return;
        }

        foreach (var node in Flatten(this.controller.MainNode))
        {
            node.IsVisible = visible;
        }
    }

    private void OnFitAllClicked(object sender, RoutedEventArgs e) => this.ApplyXRange(0, this.lastKnownFullMaxColumn);

    private void OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        e.Handled = true;
        var plot = (WpfPlot)sender;
        var pixel = plot.GetPlotPixelPosition(e);
        var pivotX = plot.Plot.GetCoordinates(pixel).X;
        var factor = e.Delta > 0 ? 1.0 / ZoomStepFactor : ZoomStepFactor;
        var newMin = pivotX - (pivotX - this.xMin) * factor;
        var newMax = pivotX + (this.xMax - pivotX) * factor;
        this.ApplyXRange(newMin, newMax);
    }

    private void OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var plot = (WpfPlot)sender;
        this.dragStartMouse = plot.GetPlotPixelPosition(e);
        this.dragStartRange = (this.xMin, this.xMax);
        this.isDragPanning = false;
        plot.CaptureMouse();
    }

    private void OnPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (this.dragStartMouse is not { } start || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var plot = (WpfPlot)sender;
        var current = plot.GetPlotPixelPosition(e);

        if (!this.isDragPanning && Math.Abs(current.X - start.X) < DragThresholdPixels)
        {
            return;
        }

        this.isDragPanning = true;

        var startCoord = plot.Plot.GetCoordinates(start);
        var currentCoord = plot.Plot.GetCoordinates(current);
        var dxData = currentCoord.X - startCoord.X;

        this.ApplyXRange(this.dragStartRange.min - dxData, this.dragStartRange.max - dxData);
    }

    private void OnPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        var plot = (WpfPlot)sender;
        plot.ReleaseMouseCapture();
        this.dragStartMouse = null;
        this.isDragPanning = false;
    }

    private void ApplyXRange(double min, double max)
    {
        var full = Math.Max(this.lastKnownFullMaxColumn, 0.0);

        if (full <= 0)
        {
            // No data yet - a placeholder range with nothing to actually "follow" outside of.
            // Must NOT set isFollowingLatest = false here: this runs on the very first redraw
            // tick, before any points have arrived, and incorrectly latching false at that point
            // would permanently disable auto-follow for the rest of the session (nothing else
            // ever flips it back to true once real data starts streaming in).
            min = 0;
            max = 1;
            this.isFollowingLatest = true;
        }
        else
        {
            min = Math.Clamp(min, 0, full);
            max = Math.Clamp(max, 0, full);
            if (max - min < MinimumSpanColumns)
            {
                var mid = (min + max) / 2;
                min = Math.Max(0, mid - MinimumSpanColumns / 2);
                max = Math.Min(full, min + MinimumSpanColumns);
                min = Math.Max(0, max - MinimumSpanColumns);
            }

            this.isFollowingLatest = min <= 0.01 && max >= full - 0.01;
        }

        this.xMin = min;
        this.xMax = max;
        this.TopPlot.Plot.Axes.SetLimitsX(min, max);
        this.BottomPlot.Plot.Axes.SetLimitsX(min, max);
        this.TopPlot.Refresh();
        this.BottomPlot.Refresh();
    }

    private void OnPlotMouseMove(object sender, MouseEventArgs e)
    {
        if (this.controller is null)
        {
            return;
        }

        var plot = (WpfPlot)sender;
        var pixel = plot.GetPlotPixelPosition(e);
        var column = (int)Math.Round(plot.Plot.GetCoordinates(pixel).X);

        if (column < 0 || column > this.lastKnownFullMaxColumn)
        {
            this.HideTooltip();
            return;
        }

        this.isHovering = true;

        var lines = new List<string>();
        foreach (var node in Flatten(this.controller.MainNode))
        {
            if (!node.IsVisible || node.Timeline is null)
            {
                continue;
            }

            var point = node.Timeline.EffectivePoints().FirstOrDefault(p => this.controller.BucketIndex.TryGetColumn(p.BucketKey) == column);
            if (point is null)
            {
                continue;
            }

            var deltaSign = point.DeltaUsd >= 0 ? "+" : "-";
            lines.Add(
                $"{node.DisplayName}: {CostFormatting.FormatKnownAmount(point.CumulativeUsd)}" +
                $"  (Δ {deltaSign}{CostFormatting.FormatKnownAmount(Math.Abs(point.DeltaUsd))})" +
                $"  {point.TimestampUtc.ToLocalTime():HH:mm:ss}");
        }

        if (lines.Count == 0)
        {
            this.HideTooltip();
            return;
        }

        this.ShowTooltip(lines, e.GetPosition(this.PlotsGrid));
    }

    private void OnPlotMouseLeave(object sender, MouseEventArgs e)
    {
        this.isHovering = false;
        this.HideTooltip();
    }

    private void ShowTooltip(IReadOnlyList<string> lines, Point position)
    {
        this.TooltipStack.Children.Clear();
        foreach (var line in lines)
        {
            this.TooltipStack.Children.Add(new TextBlock
            {
                Text = line,
                Foreground = System.Windows.Media.Brushes.White,
                FontSize = 11,
            });
        }

        this.TooltipBorder.Margin = new Thickness(position.X + 12, position.Y + 12, 0, 0);
        this.TooltipBorder.Visibility = Visibility.Visible;
    }

    private void HideTooltip()
    {
        this.TooltipBorder.Visibility = Visibility.Collapsed;
    }

    private void OnPlotMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (this.controller is null)
        {
            return;
        }

        var plot = (WpfPlot)sender;
        var pixel = plot.GetPlotPixelPosition(e);
        var column = (int)Math.Round(plot.Plot.GetCoordinates(pixel).X);

        var menu = new ContextMenu();
        foreach (var node in Flatten(this.controller.MainNode))
        {
            if (!node.IsVisible || node.Timeline is null)
            {
                continue;
            }

            var point = node.Timeline.EffectivePoints().FirstOrDefault(p => this.controller.BucketIndex.TryGetColumn(p.BucketKey) == column);
            if (point is null)
            {
                continue;
            }

            var filePath = point.FilePath;
            var lineOrdinal = point.LineOrdinal;
            menu.Items.Add(ContextMenuHelper.CreateMenuItem(
                $"Go to entry in List view ({node.DisplayName})",
                () => this.NavigateToListRequested?.Invoke(filePath, lineOrdinal)));
        }

        if (menu.Items.Count > 0)
        {
            plot.ContextMenu = menu;
            menu.IsOpen = true;
        }
    }
}
