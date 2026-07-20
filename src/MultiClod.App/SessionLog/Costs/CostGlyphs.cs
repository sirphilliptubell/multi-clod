using ScottPlot;

namespace MultiClod.App.SessionLog.Costs;

/// <summary>
/// Unicode glyph stand-ins for the exact 10 MarkerShape values CostModelShapeAssigner ever assigns
/// - a legend key only needs to be a recognizable small symbol, not a pixel-match of the plotted
/// marker, so no custom Path geometry is needed.
/// </summary>
internal static class CostGlyphs
{
    private static readonly Dictionary<MarkerShape, string> Glyphs = new()
    {
        [MarkerShape.FilledCircle] = "●",       // Haiku
        [MarkerShape.FilledTriangleUp] = "▲",   // Sonnet
        [MarkerShape.FilledSquare] = "■",       // Opus
        [MarkerShape.Asterisk] = "✳",           // Fable/Mythos
        [MarkerShape.OpenDiamond] = "◇",         // overflow 1
        [MarkerShape.Cross] = "✚",               // overflow 2
        [MarkerShape.HashTag] = "#",                   // overflow 3
        [MarkerShape.VerticalBar] = "│",         // overflow 4
        [MarkerShape.TriUp] = "△",               // overflow 5
        [MarkerShape.OpenCircleWithDot] = "◎",   // overflow 6
    };

    public static string For(MarkerShape shape) => Glyphs.GetValueOrDefault(shape, "?");
}
