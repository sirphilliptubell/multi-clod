using System.IO;
using System.Text.Json;

namespace MultiClod.App.Deeplink;

/// <summary>
/// Classifies the contents of an extracted deeplink zip: a top-level "&lt;guid&gt;.jsonl" is a main
/// session; a same-named sibling folder is its subagent container only if it has a nested
/// "subagents/agent-*.jsonl". Everything else (any other file, or a folder with no matching sibling
/// jsonl) is bucketed as an "other file" - mirrors Import/ClaudeSessionSearchService's
/// EnumerateCandidateFiles rules, adapted to a single flat extraction root instead of a
/// ~/.claude/projects-shaped tree (a deeplink zip has no encoded project-folder wrapper).
///
/// Runs in two passes over the top-level entries - first identifying every main session (and its
/// consumed subagents folder), then bucketing whatever's left as "other" - rather than one pass, so
/// classification never depends on Directory.GetFileSystemEntries' (unspecified) enumeration order.
/// </summary>
internal static class ImportZipClassifier
{
    public static async Task<ClassifiedImportContents> ClassifyAsync(string extractionRoot, CancellationToken cancellationToken)
    {
        var topLevelEntries = Directory.GetFileSystemEntries(extractionRoot);
        var sessions = new List<DeeplinkImportedSession>();
        var consumedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in topLevelEntries)
        {
            if (!File.Exists(entry) || !entry.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var stem = Path.GetFileNameWithoutExtension(entry);
            if (!Guid.TryParse(stem, out var sessionId))
            {
                continue;
            }

            var sessionDir = Path.Combine(extractionRoot, stem);
            var subagentDir = Path.Combine(sessionDir, "subagents");
            var subagentFiles = Directory.Exists(subagentDir)
                ? Directory.GetFiles(subagentDir, "agent-*.jsonl")
                : Array.Empty<string>();

            consumedPaths.Add(entry);
            if (subagentFiles.Length > 0)
            {
                consumedPaths.Add(sessionDir);
            }

            var (cwd, aiTitle) = await ExtractCwdAndLastAiTitleAsync(entry, cancellationToken);
            sessions.Add(new DeeplinkImportedSession(sessionId, entry, cwd, aiTitle, subagentFiles));
        }

        var otherFiles = new List<string>();
        foreach (var entry in topLevelEntries)
        {
            if (consumedPaths.Contains(entry))
            {
                continue;
            }

            if (File.Exists(entry))
            {
                otherFiles.Add(entry);
            }
            else if (Directory.Exists(entry))
            {
                otherFiles.AddRange(Directory.GetFiles(entry, "*", SearchOption.AllDirectories));
            }
        }

        return new ClassifiedImportContents(sessions, otherFiles);
    }

    /// <summary>
    /// Streams a main session file once - the first "cwd" wins (fixed once set), and every
    /// "ai-title" line overwrites so the LAST one (by scan order) is what's returned, since Claude
    /// Code re-emits an updated title through a conversation. Mirrors
    /// ClaudeSessionSearchService.ExtractCwdAndLastAiTitleAsync.
    /// </summary>
    private static async Task<(string? Cwd, string? AiTitle)> ExtractCwdAndLastAiTitleAsync(string mainSessionFilePath, CancellationToken cancellationToken)
    {
        string? cwd = null;
        string? aiTitle = null;

        try
        {
            using var reader = new StreamReader(mainSessionFilePath);
            while (await reader.ReadLineAsync(cancellationToken) is { } line)
            {
                if (cwd is null && line.Contains("\"cwd\"", StringComparison.Ordinal))
                {
                    cwd = TryGetStringProperty(line, "cwd") ?? cwd;
                }

                if (line.Contains("\"ai-title\"", StringComparison.Ordinal))
                {
                    aiTitle = TryGetStringProperty(line, "aiTitle") ?? aiTitle;
                }
            }
        }
        catch (IOException)
        {
        }

        return (cwd, aiTitle);
    }

    private static string? TryGetStringProperty(string json, string propertyName)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
        }
        catch (JsonException)
        {
            // Partial/malformed line - skip it rather than failing the whole scan.
            return null;
        }
    }
}
