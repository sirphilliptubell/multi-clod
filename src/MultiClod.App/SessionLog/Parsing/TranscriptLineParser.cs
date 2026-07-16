using System.Text.Json;

namespace MultiClod.App.SessionLog.Parsing;

/// <summary>
/// Parses one JSONL transcript line at a time. Never throws - a line that isn't valid JSON, or is
/// valid JSON with no "type" property, comes back with IsValidJson/TypeValue reflecting that so the
/// caller can render an Unrecognized row instead of aborting the whole read. Transcripts are
/// actively appended to by a separate `claude` process, so a torn/partial line read mid-write is
/// expected, not exceptional.
/// </summary>
public static class TranscriptLineParser
{
    public static ParsedLine Parse(string rawLine)
    {
        try
        {
            using var document = JsonDocument.Parse(rawLine);
            var root = document.RootElement.Clone();
            var typeValue = root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty("type", out var typeProperty)
                && typeProperty.ValueKind == JsonValueKind.String
                ? typeProperty.GetString()
                : null;

            return new ParsedLine(IsValidJson: true, root, typeValue, rawLine);
        }
        catch (JsonException)
        {
            return new ParsedLine(IsValidJson: false, default, TypeValue: null, rawLine);
        }
    }
}
