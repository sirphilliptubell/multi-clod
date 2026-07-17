using System.Windows;
using System.Windows.Controls;

namespace MultiClod.App.Costs;

/// <summary>
/// Reusable "$X.XX" / "&gt;$X.XX" / "&lt;$0.01" / "$?.??" display, shared by the session tree, tab
/// strip, Session Log window (Main Session header + subagent list), and every transcript row -
/// every one of those already exposes a fully-formatted nullable string (SessionNodeViewModel/
/// SessionLogSourceViewModel's CostBadgeText, TranscriptRowViewModel's LineCostText), so this
/// control just needs that string bound in; it derives "should this be visible" itself (empty/null
/// text, or the user's Settings toggle, either one hides it) instead of every caller also having to
/// bind a separate Has* boolean.
/// </summary>
public partial class CostBadge : UserControl
{
    public static readonly DependencyProperty CostTextProperty =
        DependencyProperty.Register(nameof(CostText), typeof(string), typeof(CostBadge), new PropertyMetadata(null));

    // Optional - a caller with nothing to break down (e.g. a transcript row, already a single
    // model) simply never binds this, and no tooltip appears at all.
    public static readonly DependencyProperty BreakdownTextProperty =
        DependencyProperty.Register(nameof(BreakdownText), typeof(string), typeof(CostBadge), new PropertyMetadata(null));

    public CostBadge()
    {
        this.InitializeComponent();
    }

    public string? CostText
    {
        get => (string?)this.GetValue(CostTextProperty);
        set => this.SetValue(CostTextProperty, value);
    }

    public string? BreakdownText
    {
        get => (string?)this.GetValue(BreakdownTextProperty);
        set => this.SetValue(BreakdownTextProperty, value);
    }
}
