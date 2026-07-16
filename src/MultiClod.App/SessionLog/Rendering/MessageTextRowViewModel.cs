using System.Text.Json;

namespace MultiClod.App.SessionLog.Rendering;

/// <summary>
/// A "text" or "image" content block from a user/assistant entry. User and Assistant share this
/// same row type - only Category (and therefore styling) differs, since both are just "a message
/// with some text" per the approved plan's categorization. An "image" block renders as a fixed
/// placeholder ("[image attached]") with the full block still reachable via Source - no thumbnail
/// decoding for v1.
/// </summary>
public sealed class MessageTextRowViewModel : TranscriptRowViewModel
{
    private readonly JsonElement lineRoot;
    private readonly string previewText;

    public MessageTextRowViewModel(TranscriptRowCategory category, JsonElement lineRoot, string previewText)
        : base(category, TranscriptTimestamp.TryRead(lineRoot))
    {
        this.lineRoot = lineRoot;
        this.previewText = previewText;
    }

    public override string SummaryText => TranscriptSummary.WithTimestamp(this.Timestamp, TextPreview.Truncate(this.previewText));

    public override string ExpandedBodyText => this.previewText;

    public override string CopyableJson => TranscriptJsonFormatting.Format(this.lineRoot);
}
