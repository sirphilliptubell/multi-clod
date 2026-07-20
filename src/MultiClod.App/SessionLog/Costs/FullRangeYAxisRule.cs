using ScottPlot;

namespace MultiClod.App.SessionLog.Costs;

/// <summary>
/// Re-clamps one Y axis to [getMin(), getMax()] on every render pass - the documented ScottPlot
/// pattern for a "fixed scaling axis". Added once per plot in CostsView.Initialize so a graph's Y
/// range is always the full range across ALL series (visible or hidden); getMin/getMax read fields
/// CostsView updates from every incoming point regardless of that series' current visibility, so
/// toggling a series never rescales either graph's Y axis - only adds/removes its drawn line.
/// </summary>
internal sealed class FullRangeYAxisRule : IAxisRule
{
    private readonly IYAxis axis;
    private readonly Func<double> getMin;
    private readonly Func<double> getMax;

    public FullRangeYAxisRule(IYAxis axis, Func<double> getMin, Func<double> getMax)
    {
        this.axis = axis;
        this.getMin = getMin;
        this.getMax = getMax;
    }

    public void Apply(RenderPack rp, bool beforeLayout)
    {
        if (beforeLayout)
        {
            rp.Plot.Axes.SetLimitsY(this.getMin(), this.getMax(), this.axis);
        }
    }
}
