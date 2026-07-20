using ScottPlot;

namespace MultiClod.App.SessionLog.Costs;

/// <summary>
/// Assigns each distinct model slug a marker shape for the lifetime of one Costs session: a fixed
/// family mapping by complexity tier (Haiku/Sonnet/Opus/Fable-Mythos), plus a small overflow list
/// (round-robin, first-seen order) for any other/unrecognized model. ScottPlot's MarkerShape enum
/// has no "star" shape - Asterisk is the closest visual stand-in for Fable/Mythos.
/// </summary>
internal sealed class CostModelShapeAssigner
{
    private static readonly MarkerShape[] OverflowShapes =
    [
        MarkerShape.OpenDiamond,
        MarkerShape.Cross,
        MarkerShape.HashTag,
        MarkerShape.VerticalBar,
        MarkerShape.TriUp,
        MarkerShape.OpenCircleWithDot,
    ];

    private readonly Dictionary<string, MarkerShape> assigned = new(StringComparer.OrdinalIgnoreCase);
    private int nextOverflowIndex;

    public MarkerShape ShapeFor(string modelSlug)
    {
        if (this.assigned.TryGetValue(modelSlug, out var existing))
        {
            return existing;
        }

        var shape = DetectFamily(modelSlug) ?? OverflowShapes[this.nextOverflowIndex++ % OverflowShapes.Length];
        this.assigned[modelSlug] = shape;
        return shape;
    }

    private static MarkerShape? DetectFamily(string slug)
    {
        if (slug.Contains("haiku", StringComparison.OrdinalIgnoreCase))
        {
            return MarkerShape.FilledCircle;
        }

        if (slug.Contains("sonnet", StringComparison.OrdinalIgnoreCase))
        {
            return MarkerShape.FilledTriangleUp;
        }

        if (slug.Contains("opus", StringComparison.OrdinalIgnoreCase))
        {
            return MarkerShape.FilledSquare;
        }

        if (slug.Contains("fable", StringComparison.OrdinalIgnoreCase) || slug.Contains("mythos", StringComparison.OrdinalIgnoreCase))
        {
            return MarkerShape.Asterisk;
        }

        return null;
    }
}
