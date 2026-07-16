namespace MultiClod.App.SessionLog.Rendering;

/// <summary>
/// The 6 categories a row can render as - see TranscriptCategoryStyles.xaml for the glyph/accent
/// color assigned to each, and the plan's "UI Specs" table for why each was chosen.
/// </summary>
public enum TranscriptRowCategory
{
    User,
    Assistant,
    Thinking,
    ToolCall,
    SystemMeta,
    Unrecognized,
}
