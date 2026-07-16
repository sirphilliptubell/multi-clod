using System.Text.Json;

namespace MultiClod.App.SessionLog.Rendering;

/// <summary>
/// A tool_use block merged with its later-arriving tool_result (matched by tool_use_id) into one
/// stable row. Starts pending; ApplyToolResult mutates fields and raises property-changed without
/// ever touching IsExpanded/IsAdditionalPropertiesExpanded, so an already-expanded row doesn't
/// collapse out from under the user the instant its result shows up. Failure is read from the
/// tool_result content block's "is_error" field (Anthropic Messages API's documented field on
/// tool_result blocks) - absent/false means success.
/// </summary>
public sealed class ToolCallRowViewModel : TranscriptRowViewModel
{
    private static readonly IReadOnlySet<string> ToolUseConsumedPaths =
        new HashSet<string>(CommonEntryFields.BaseConsumedPaths) { "message.content", "message.role" };

    private static readonly IReadOnlySet<string> ToolResultConsumedPaths =
        new HashSet<string>(CommonEntryFields.BaseConsumedPaths) { "message.content", "message.role", "toolUseResult" };

    private readonly JsonElement toolUseLineRoot;
    private readonly JsonElement? toolInput;
    private readonly string inputPreview;
    private JsonElement? toolResultLineRoot;
    private JsonElement? toolResultContent;

    public ToolCallRowViewModel(JsonElement toolUseLineRoot, string toolName, JsonElement? toolInput, string inputPreview)
        : base(TranscriptRowCategory.ToolCall, TranscriptTimestamp.TryRead(toolUseLineRoot))
    {
        this.toolUseLineRoot = toolUseLineRoot;
        this.ToolName = toolName;
        this.toolInput = toolInput;
        this.inputPreview = inputPreview;
    }

    public string ToolName { get; }

    public bool IsPending { get; private set; } = true;

    public bool IsError { get; private set; }

    // Per the plan, the summary never shows result content - just a resolved/failed marker -
    // ExpandedBodyText is where the full input/result becomes visible.
    public void ApplyToolResult(JsonElement toolResultLineRoot, JsonElement? toolResultContent, bool isError)
    {
        this.toolResultLineRoot = toolResultLineRoot;
        this.toolResultContent = toolResultContent;
        this.IsPending = false;
        this.IsError = isError;
        this.RaisePropertyChanged(nameof(this.IsPending));
        this.RaisePropertyChanged(nameof(this.IsError));
        this.RaiseContentChanged();
    }

    public override string SummaryText
    {
        get
        {
            var body = $"{this.ToolName}({this.inputPreview})";
            if (!this.IsPending)
            {
                body += this.IsError ? "  [failed]" : "  [ok]";
            }

            return TranscriptSummary.WithTimestamp(this.Timestamp, body);
        }
    }

    public override string ExpandedBodyText
    {
        get
        {
            var inputText = this.toolInput is { } input ? TranscriptJsonFormatting.Format(input) : "(none)";
            var body = $"Tool: {this.ToolName}\n\nInput:\n{inputText}";

            if (this.IsPending)
            {
                return body + "\n\n(waiting for result...)";
            }

            var resultLabel = this.IsError ? "Error" : "Result";
            var resultText = this.toolResultContent switch
            {
                { ValueKind: JsonValueKind.String } content => content.GetString() ?? string.Empty,
                { } content => TranscriptJsonFormatting.Format(content),
                null => "(no content)",
            };

            return $"{body}\n\n{resultLabel}:\n{resultText}";
        }
    }

    public override string AdditionalPropertiesJson
    {
        get
        {
            var toolUseLeftover = JsonLeftoverComputer.ComputeLeftoverJson(this.toolUseLineRoot, ToolUseConsumedPaths);
            if (this.toolResultLineRoot is not { } resultRoot)
            {
                return toolUseLeftover;
            }

            var toolResultLeftover = JsonLeftoverComputer.ComputeLeftoverJson(resultRoot, ToolResultConsumedPaths);
            return $"// tool_use:\n{toolUseLeftover}\n\n// tool_result:\n{toolResultLeftover}";
        }
    }

    public override string CopyableJson
    {
        get
        {
            var toolUseJson = TranscriptJsonFormatting.Format(this.toolUseLineRoot);
            if (this.toolResultLineRoot is not { } resultRoot)
            {
                return toolUseJson;
            }

            return $"// tool_use line:\n{toolUseJson}\n\n// tool_result line:\n{TranscriptJsonFormatting.Format(resultRoot)}";
        }
    }
}
