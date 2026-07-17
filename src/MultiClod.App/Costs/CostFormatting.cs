namespace MultiClod.App.Costs;

/// <summary>
/// Shared "$X.XX" / "&lt;$0.01" formatting for a single known dollar amount - used both by
/// SessionCostAggregator (aggregate badges) and by a transcript row's own per-line cost, so the
/// "don't show a misleading $0.00 for a legitimately non-zero amount" rule stays consistent
/// everywhere a cost is displayed.
/// </summary>
internal static class CostFormatting
{
    public static string FormatKnownAmount(decimal amountUsd) =>
        amountUsd > 0m && Math.Round(amountUsd, 2) == 0m ? "<$0.01" : $"${amountUsd:F2}";
}
