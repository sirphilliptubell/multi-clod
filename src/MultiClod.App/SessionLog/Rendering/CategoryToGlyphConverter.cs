using System.Globalization;
using System.Windows.Data;

namespace MultiClod.App.SessionLog.Rendering;

/// <summary>
/// Maps a TranscriptRowCategory to its Segoe Fluent Icons glyph - see the approved plan's "UI
/// Specs" table for the codepoint assigned to each category.
/// </summary>
public sealed class CategoryToGlyphConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is TranscriptRowCategory category ? Glyph(category) : string.Empty;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    private static string Glyph(TranscriptRowCategory category) => category switch
    {
        TranscriptRowCategory.User => "\uE77B",
        TranscriptRowCategory.Assistant => "\uE8BD",
        TranscriptRowCategory.Thinking => "\uE7E7",
        TranscriptRowCategory.ToolCall => "\uE90F",
        TranscriptRowCategory.SystemMeta => "\uE946",
        TranscriptRowCategory.Unrecognized => "\uE897",
        _ => string.Empty,
    };
}
