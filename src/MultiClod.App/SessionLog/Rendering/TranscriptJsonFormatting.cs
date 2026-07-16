using System.Text.Json;

namespace MultiClod.App.SessionLog.Rendering;

/// <summary>
/// Shared JSON formatting for rows: full pretty-printed output for Additional Properties/copy, and
/// a truncated single-line form for a row's collapsed summary (e.g. a tool call's input preview) -
/// full fidelity always belongs in Additional Properties/CopyableJson, never the summary line.
/// </summary>
internal static class TranscriptJsonFormatting
{
    private static readonly JsonSerializerOptions IndentedOptions = new() { WriteIndented = true };

    public static string Format(JsonElement element) => JsonSerializer.Serialize(element, IndentedOptions);

    public static string FormatCompact(JsonElement element) => TextPreview.Truncate(element.GetRawText().Replace('\r', ' ').Replace('\n', ' '));
}
