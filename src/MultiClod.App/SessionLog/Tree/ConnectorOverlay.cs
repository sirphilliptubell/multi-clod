using System.Windows;
using System.Windows.Media;

namespace MultiClod.App.SessionLog.Tree;

/// <summary>
/// Draws every spawn/return connector for one TreeGraph snapshot in a single OnRender pass - far
/// cheaper than a Path-per-edge for a graph with many connectors, and connectors are never
/// interactive so no hit-testing is needed (IsHitTestVisible is false so a connector drawn near a
/// box never steals its click). Sits on top of the box ItemsControl inside the same (unscaled)
/// graph-pixel coordinate space - the containing Canvas's ScaleTransform handles zoom for both
/// layers identically. SetConnectors only needs to run on a graph rebuild; the geometry never
/// changes between rebuilds of the same snapshot.
/// </summary>
public sealed class ConnectorOverlay : FrameworkElement
{
    private const double ArrowHeadLength = 8;
    private const double ArrowHeadWidth = 6;

    private IReadOnlyList<TreeConnector> connectors = [];

    public ConnectorOverlay()
    {
        this.IsHitTestVisible = false;
    }

    public void SetConnectors(IReadOnlyList<TreeConnector> newConnectors)
    {
        this.connectors = newConnectors;
        this.InvalidateVisual();
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        foreach (var connector in this.connectors)
        {
            if (connector.Points.Count < 2)
            {
                continue;
            }

            var brush = BrushFor(connector.ColorHex);
            var pen = new Pen(brush, connector.Kind == TreeConnectorKind.Return ? 2.0 : 1.5);
            pen.Freeze();

            var geometry = new StreamGeometry();
            using (var context = geometry.Open())
            {
                context.BeginFigure(new Point(connector.Points[0].X, connector.Points[0].Y), isFilled: false, isClosed: false);
                for (var i = 1; i < connector.Points.Count; i++)
                {
                    context.LineTo(new Point(connector.Points[i].X, connector.Points[i].Y), isStroked: true, isSmoothJoin: false);
                }
            }

            geometry.Freeze();
            drawingContext.DrawGeometry(null, pen, geometry);

            if (connector.Kind == TreeConnectorKind.Return)
            {
                DrawArrowHead(drawingContext, brush, connector.Points[^2], connector.Points[^1]);
            }
        }
    }

    private static void DrawArrowHead(DrawingContext drawingContext, Brush brush, TreePoint from, TreePoint to)
    {
        var direction = new Vector(to.X - from.X, to.Y - from.Y);
        if (direction.Length < 0.001)
        {
            return;
        }

        direction.Normalize();
        var normal = new Vector(-direction.Y, direction.X);
        var tip = new Point(to.X, to.Y);
        var back = tip - direction * ArrowHeadLength;
        var left = back + normal * (ArrowHeadWidth / 2);
        var right = back - normal * (ArrowHeadWidth / 2);

        var arrow = new StreamGeometry();
        using (var context = arrow.Open())
        {
            context.BeginFigure(tip, isFilled: true, isClosed: true);
            context.LineTo(left, isStroked: true, isSmoothJoin: false);
            context.LineTo(right, isStroked: true, isSmoothJoin: false);
        }

        arrow.Freeze();
        drawingContext.DrawGeometry(brush, null, arrow);
    }

    private static Brush BrushFor(string colorHex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colorHex));
        brush.Freeze();
        return brush;
    }
}
