using System.IO;
using System.Text.Json;

namespace MultiClod.App.SessionLog.Tree;

/// <summary>
/// Reads a subagent's agent-&lt;id&gt;.meta.json sidecar. Path is derived the same way
/// Import\ClaudeSessionSearchService does it: swap the ".jsonl" extension for ".meta.json" (NOT
/// simply appending - Path.ChangeExtension first removes ".jsonl", leaving "agent-&lt;id&gt;", so
/// this concatenation produces "agent-&lt;id&gt;.meta.json", not "agent-&lt;id&gt;.jsonl.meta.json").
/// Returns null for a missing/unparseable file or one missing "toolUseId" - TreeGraphBuilder treats
/// that as an orphan agent (rendered, unlinked, no connector) rather than failing the whole build.
/// </summary>
internal static class AgentMetaReader
{
    public static AgentMeta? TryRead(string agentJsonlPath)
    {
        var metaPath = Path.ChangeExtension(agentJsonlPath, null) + ".meta.json";

        if (!File.Exists(metaPath))
        {
            return null;
        }

        try
        {
            using var stream = File.OpenRead(metaPath);
            using var document = JsonDocument.Parse(stream);
            var root = document.RootElement;

            var toolUseId = GetString(root, "toolUseId");
            if (toolUseId is null)
            {
                return null;
            }

            return new AgentMeta(
                AgentType: GetString(root, "agentType") ?? string.Empty,
                Description: GetString(root, "description") ?? string.Empty,
                ToolUseId: toolUseId,
                ParentAgentId: GetString(root, "parentAgentId"),
                SpawnDepth: root.TryGetProperty("spawnDepth", out var depth) && depth.ValueKind == JsonValueKind.Number ? depth.GetInt32() : 1);
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            return null;
        }
    }

    private static string? GetString(JsonElement element, string propertyName) =>
        element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(propertyName, out var value)
            && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
