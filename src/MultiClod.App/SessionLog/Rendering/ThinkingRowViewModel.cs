using System.Text.Json;

namespace MultiClod.App.SessionLog.Rendering;

/// <summary>
/// A "thinking" content block from an assistant entry - always its own row, rendered
/// de-emphasized (italic/muted) via TranscriptCategoryStyles.xaml, distinct from the assistant's
/// visible text.
/// </summary>
public sealed class ThinkingRowViewModel : TranscriptRowViewModel
{
    private readonly JsonElement lineRoot;
    private readonly string previewText;

    public ThinkingRowViewModel(JsonElement lineRoot, string previewText)
        : base(TranscriptRowCategory.Thinking, TranscriptTimestamp.TryRead(lineRoot))
    {
        this.lineRoot = lineRoot;
        this.previewText = previewText;
    }

    public override string SummaryText => TranscriptSummary.WithTimestamp(this.Timestamp, TextPreview.Truncate(this.previewText));

    public override string ExpandedBodyText => this.previewText;

    public override string CopyableJson => TranscriptJsonFormatting.Format(this.lineRoot);
}
