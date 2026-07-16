using System.Text.Json;

namespace MultiClod.App.SessionLog.Rendering;

/// <summary>
/// Catch-all row for every valid transcript line whose top-level type isn't "user"/"assistant" -
/// mode, permission-mode, file-history-snapshot, attachment subtypes, etc. Hidden by default
/// behind the viewer's "Show all events" toggle; deliberately generic so a brand-new event type
/// introduced by a future Claude Code version still renders legibly without a code change here.
/// </summary>
public sealed class SystemMetaRowViewModel : TranscriptRowViewModel
{
    private readonly JsonElement lineRoot;
    private readonly string typeValue;

    public SystemMetaRowViewModel(JsonElement lineRoot, string typeValue)
        : base(TranscriptRowCategory.SystemMeta, TranscriptTimestamp.TryRead(lineRoot))
    {
        this.lineRoot = lineRoot;
        this.typeValue = typeValue;
    }

    public override string SummaryText => TranscriptSummary.WithTimestamp(this.Timestamp, this.BuildBody());

    public override string ExpandedBodyText => this.BuildBody();

    public override string CopyableJson => TranscriptJsonFormatting.Format(this.lineRoot);

    private string BuildBody()
    {
        var subtype = this.lineRoot.ValueKind == JsonValueKind.Object
            && this.lineRoot.TryGetProperty("subtype", out var subtypeValue)
            && subtypeValue.ValueKind == JsonValueKind.String
            ? subtypeValue.GetString()
            : null;

        return subtype is { Length: > 0 } ? $"{this.typeValue} ({subtype})" : this.typeValue;
    }
}
