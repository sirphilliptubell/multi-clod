using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace MultiClod.App.SessionLog.Rendering;

/// <summary>
/// Maps a TranscriptRowCategory to its accent brush - see the approved plan's "UI Specs" table.
/// ToolCall's failure-red variant is handled separately, via a DataTrigger on IsError in
/// TranscriptCategoryStyles.xaml's ToolCallRowViewModel template, since that depends on row state
/// beyond just the category.
/// </summary>
public sealed class CategoryToBrushConverter : IValueConverter
{
    private static readonly Brush UserBrush = FrozenBrush("#3A96DD");
    private static readonly Brush AssistantBrush = FrozenBrush("#DA7756");
    private static readonly Brush ThinkingBrush = FrozenBrush("#9B8AC4");
    private static readonly Brush ToolCallBrush = FrozenBrush("#D08B2C");
    private static readonly Brush SystemMetaBrush = FrozenBrush("#8A8A8A");
    private static readonly Brush UnrecognizedBrush = FrozenBrush("#D9A93A");

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is TranscriptRowCategory category ? BrushFor(category) : SystemMetaBrush;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    private static Brush BrushFor(TranscriptRowCategory category) => category switch
    {
        TranscriptRowCategory.User => UserBrush,
        TranscriptRowCategory.Assistant => AssistantBrush,
        TranscriptRowCategory.Thinking => ThinkingBrush,
        TranscriptRowCategory.ToolCall => ToolCallBrush,
        TranscriptRowCategory.SystemMeta => SystemMetaBrush,
        TranscriptRowCategory.Unrecognized => UnrecognizedBrush,
        _ => SystemMetaBrush,
    };

    private static Brush FrozenBrush(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }
}
