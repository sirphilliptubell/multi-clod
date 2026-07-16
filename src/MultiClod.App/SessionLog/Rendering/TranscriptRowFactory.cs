using System.Text.Json;
using MultiClod.App.SessionLog.Parsing;

namespace MultiClod.App.SessionLog.Rendering;

/// <summary>
/// Turns each JSONL line from one source (main session or one subagent file) into 0+ rows. Owns
/// the pending tool_use lookup for that source, so tool_use/tool_result pairing never crosses
/// between two different open sources - switching sources in TranscriptViewerControl discards the
/// old factory entirely rather than reusing it.
/// </summary>
public sealed class TranscriptRowFactory
{
    private readonly Dictionary<string, ToolCallRowViewModel> pendingToolUses = new();

    /// <summary>
    /// Returns the new rows to append for this line - usually 0 or 1, occasionally more (an
    /// assistant turn with several content blocks). A tool_result that matches a pending tool_use
    /// mutates that row in place and contributes no new row of its own.
    /// </summary>
    public IReadOnlyList<TranscriptRowViewModel> ProcessLine(string rawLine)
    {
        var parsed = TranscriptLineParser.Parse(rawLine);
        if (!parsed.IsValidJson)
        {
            return [new UnrecognizedRowViewModel(rawLine, validJsonWithNoType: null)];
        }

        if (parsed.TypeValue is null)
        {
            return [new UnrecognizedRowViewModel(rawLine, parsed.Root)];
        }

        return parsed.TypeValue switch
        {
            "user" => this.ProcessMessageEntry(parsed.Root, TranscriptRowCategory.User),
            "assistant" => this.ProcessMessageEntry(parsed.Root, TranscriptRowCategory.Assistant),
            _ => [new SystemMetaRowViewModel(parsed.Root, parsed.TypeValue)],
        };
    }

    private IReadOnlyList<TranscriptRowViewModel> ProcessMessageEntry(JsonElement lineRoot, TranscriptRowCategory category)
    {
        if (!lineRoot.TryGetProperty("message", out var message) || !message.TryGetProperty("content", out var content))
        {
            return [];
        }

        if (content.ValueKind == JsonValueKind.String)
        {
            return [new MessageTextRowViewModel(category, lineRoot, content.GetString() ?? string.Empty)];
        }

        if (content.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var rows = new List<TranscriptRowViewModel>();
        foreach (var block in content.EnumerateArray())
        {
            var row = this.ProcessContentBlock(lineRoot, category, block);
            if (row is not null)
            {
                rows.Add(row);
            }
        }

        return rows;
    }

    // Returns null only for a tool_result block that successfully matched and mutated an existing
    // pending row in place - that's an update to an existing row, not a new one to append.
    private TranscriptRowViewModel? ProcessContentBlock(JsonElement lineRoot, TranscriptRowCategory category, JsonElement block)
    {
        var blockType = GetString(block, "type");

        return blockType switch
        {
            "text" => new MessageTextRowViewModel(category, lineRoot, GetString(block, "text")),
            "thinking" => new ThinkingRowViewModel(lineRoot, GetString(block, "thinking")),
            "image" => new MessageTextRowViewModel(category, lineRoot, "[image attached]"),
            "tool_use" => this.ProcessToolUse(lineRoot, block),
            "tool_result" => this.ProcessToolResult(lineRoot, block),
            _ => new MessageTextRowViewModel(category, lineRoot, $"[unknown content block: {(blockType.Length > 0 ? blockType : "?")}]"),
        };
    }

    private ToolCallRowViewModel ProcessToolUse(JsonElement lineRoot, JsonElement block)
    {
        var id = GetString(block, "id");
        var name = GetString(block, "name");
        JsonElement? inputElement = block.TryGetProperty("input", out var input) ? input : null;
        var inputPreview = inputElement is { } inputValue ? TranscriptJsonFormatting.FormatCompact(inputValue) : string.Empty;

        var row = new ToolCallRowViewModel(lineRoot, name, inputElement, inputPreview);
        if (id.Length > 0)
        {
            this.pendingToolUses[id] = row;
        }

        return row;
    }

    private TranscriptRowViewModel? ProcessToolResult(JsonElement lineRoot, JsonElement block)
    {
        var toolUseId = GetString(block, "tool_use_id");
        var isError = block.TryGetProperty("is_error", out var isErrorProperty) && isErrorProperty.ValueKind == JsonValueKind.True;
        JsonElement? contentElement = block.TryGetProperty("content", out var content) ? content : null;

        if (toolUseId.Length > 0 && this.pendingToolUses.Remove(toolUseId, out var pendingRow))
        {
            pendingRow.ApplyToolResult(lineRoot, contentElement, isError);
            return null;
        }

        // Orphaned result - e.g. its tool_use lives in a different file (a subagent transcript can
        // reference a tool_use recorded only in its parent's file). Render a degraded but visible
        // row rather than silently dropping the data.
        var orphan = new ToolCallRowViewModel(lineRoot, "(unknown tool)", toolInput: null, inputPreview: string.Empty);
        orphan.ApplyToolResult(lineRoot, contentElement, isError);
        return orphan;
    }

    private static string GetString(JsonElement element, string propertyName) =>
        element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(propertyName, out var value)
            && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;
}
