using MultiClod.App.Costs;

namespace MultiClod.App.SessionLog.Rendering;

/// <summary>
/// Tri-state cost for one transcript row's own JSONL line - None (the line carried no
/// message.usage at all, e.g. a user/tool_result line), Known (priced successfully), or Unknown
/// (the line's model wasn't found in ClaudeModelPricing for its timestamp). Unlike an aggregate
/// SessionCostSummary, there's no partial sum possible for a single line - it's either priced or
/// it isn't.
/// </summary>
internal enum LineCostKind
{
    None,
    Known,
    Unknown,
}

internal readonly struct LineCostDisplay
{
    public static readonly LineCostDisplay None = new(LineCostKind.None, null);
    public static readonly LineCostDisplay Unknown = new(LineCostKind.Unknown, null);

    private LineCostDisplay(LineCostKind kind, decimal? amountUsd)
    {
        this.Kind = kind;
        this.AmountUsd = amountUsd;
    }

    public LineCostKind Kind { get; }

    public decimal? AmountUsd { get; }

    public static LineCostDisplay KnownAmount(decimal amountUsd) => new(LineCostKind.Known, amountUsd);

    // "$X.XX" / "<$0.01" / literal "$?.??" / null (no cost shown at all for this row).
    public string? ToDisplayText() => this.Kind switch
    {
        LineCostKind.Known => CostFormatting.FormatKnownAmount(this.AmountUsd!.Value),
        LineCostKind.Unknown => "$?.??",
        _ => null,
    };
}
