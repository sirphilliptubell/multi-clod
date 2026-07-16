using System.Text.Json;

namespace MultiClod.App.SessionLog.Rendering;

/// <summary>
/// Reserved for actual parse failures only - a line that isn't valid JSON, or is valid JSON with
/// no "type" field at all. NOT used for valid-but-unfamiliar event types, which render as
/// SystemMetaRowViewModel instead so a future Claude Code version's new event types don't look
/// like something is broken.
/// </summary>
public sealed class UnrecognizedRowViewModel : TranscriptRowViewModel
{
    private readonly string rawText;
    private readonly JsonElement? validJsonWithNoType;

    public UnrecognizedRowViewModel(string rawText, JsonElement? validJsonWithNoType)
        : base(TranscriptRowCategory.Unrecognized, validJsonWithNoType is { } root ? TranscriptTimestamp.TryRead(root) : null)
    {
        this.rawText = rawText;
        this.validJsonWithNoType = validJsonWithNoType;
    }

    public override string SummaryText => TranscriptSummary.WithTimestamp(this.Timestamp, TextPreview.Truncate(this.rawText));

    public override string ExpandedBodyText => this.rawText;

    public override string CopyableJson => this.validJsonWithNoType is { } root
        ? TranscriptJsonFormatting.Format(root)
        : this.rawText;
}
