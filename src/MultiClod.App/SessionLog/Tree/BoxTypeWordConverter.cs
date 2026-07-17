using System.Globalization;
using System.Windows.Data;
using MultiClod.App.SessionLog.Rendering;

namespace MultiClod.App.SessionLog.Tree;

/// <summary>
/// Maps a BoxNode to its short type word for the Tree view's minimal box label (glyph + type word +
/// cost, no summary text - see TreeBoxStyles.xaml and decision D2 in
/// specs/session-log-tree-view.md). ToolCall shows its ToolName instead of a generic word, since
/// "tool" alone tells you nothing useful at this zoom level.
/// </summary>
public sealed class BoxTypeWordConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is BoxNode box ? Word(box) : string.Empty;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    private static string Word(BoxNode box)
    {
        if (box.Kind == BoxKind.SubagentReturn)
        {
            return "↩ result";
        }

        return box.RowVm.Category switch
        {
            TranscriptRowCategory.User => "user",
            TranscriptRowCategory.Assistant => "assistant",
            TranscriptRowCategory.Thinking => "thinking",
            TranscriptRowCategory.ToolCall => box.RowVm is ToolCallRowViewModel toolCall ? toolCall.ToolName : "tool",
            TranscriptRowCategory.SystemMeta => "system",
            TranscriptRowCategory.Unrecognized => "?",
            _ => string.Empty,
        };
    }
}
