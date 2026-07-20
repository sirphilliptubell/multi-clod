namespace MultiClod.App.SessionLog.Costs;

/// <summary>
/// One read-only row in the Costs view's top-left model legend: a Unicode glyph standing in for
/// the point marker shape (see CostModelShapeAssigner/CostGlyphs), the model slug, and its cost
/// text - "$X.XX" when the visible total equals the grand total for that model, else
/// "$visible / $grandTotal".
/// </summary>
internal sealed record CostLegendEntry(string Glyph, string ModelSlug, string CostText);
