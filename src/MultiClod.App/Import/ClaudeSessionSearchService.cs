using System.Collections.Concurrent;
using System.IO;
using System.Text.Json;

namespace MultiClod.App.Import;

/// <summary>
/// Scans a ~/.claude/projects-shaped directory tree for session transcripts whose content
/// contains every word of a free-text query. Streams files line-by-line rather than loading them
/// whole (transcripts can be multi-MB) and runs across candidate files with bounded parallelism.
/// The projects root is always a constructor parameter, never hardcoded, so tests can point this
/// at a synthetic fixture tree instead of the real ~/.claude/projects.
/// </summary>
internal sealed class ClaudeSessionSearchService
{
    private readonly string projectsRootDirectory;

    public ClaudeSessionSearchService(string projectsRootDirectory)
    {
        this.projectsRootDirectory = projectsRootDirectory;
    }

    public async Task<IReadOnlyList<ClaudeSessionSearchResult>> SearchAsync(
        string searchText,
        CancellationToken cancellationToken,
        IProgress<(int FilesScanned, int TotalFiles)>? progress = null)
    {
        var words = searchText
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (words.Length == 0 || !Directory.Exists(this.projectsRootDirectory))
        {
            return Array.Empty<ClaudeSessionSearchResult>();
        }

        var candidates = EnumerateCandidateFiles(this.projectsRootDirectory);
        var matches = new ConcurrentBag<CandidateFile>();
        var scanned = 0;

        await Parallel.ForEachAsync(
            candidates,
            new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount, CancellationToken = cancellationToken },
            async (candidate, ct) =>
            {
                if (await FileContainsAllWordsAsync(candidate.FilePath, words, ct))
                {
                    matches.Add(candidate);
                }

                progress?.Report((Interlocked.Increment(ref scanned), candidates.Count));
            });

        // Extraction only runs for files that actually matched, so it stays cheap in aggregate
        // even though it re-reads each parent main file in full (to find the LAST ai-title).
        var extractionCache = new Dictionary<string, (string? Cwd, string? AiTitle)>();
        var results = new List<ClaudeSessionSearchResult>();

        foreach (var match in matches)
        {
            if (!extractionCache.TryGetValue(match.ParentFilePath, out var extracted))
            {
                extracted = await ExtractCwdAndLastAiTitleAsync(match.ParentFilePath, cancellationToken);
                extractionCache[match.ParentFilePath] = extracted;
            }

            if (extracted.Cwd is null)
            {
                // No cwd anywhere in the parent transcript means it's corrupt/truncated - there's
                // nothing usable to import it with.
                continue;
            }

            var summary = extracted.AiTitle;
            if (summary is null && match.MetaJsonPath is not null)
            {
                summary = await TryReadMetaDescriptionAsync(match.MetaJsonPath, cancellationToken);
            }

            var lastModified = TryGetLastWriteTime(match.FilePath);
            results.Add(new ClaudeSessionSearchResult(match.ProjectDirectoryName, match.DisplayPath, match.ParentSessionId, extracted.Cwd, summary, lastModified));
        }

        return results;
    }

    private sealed record CandidateFile(
        string FilePath,
        string ProjectDirectoryName,
        Guid ParentSessionId,
        string ParentFilePath,
        string? MetaJsonPath,
        string DisplayPath);

    /// <summary>
    /// Enumerates every main session (top-level "&lt;guid&gt;.jsonl") and subagent
    /// ("&lt;guid&gt;/subagents/agent-*.jsonl") transcript under <paramref name="root"/>. A
    /// subfolder only counts as a session dir if its name parses as a guid AND a sibling
    /// "&lt;guid&gt;.jsonl" exists - this is what tells a real session dir apart from an unrelated
    /// folder like this app's own "memory" auto-memory folder. Built eagerly (not via yield) so
    /// enumeration failures on one folder (permissions, junctions) can be caught and skipped
    /// without aborting the whole scan.
    /// </summary>
    private static List<CandidateFile> EnumerateCandidateFiles(string root)
    {
        var candidates = new List<CandidateFile>();

        if (!TryGetEntries(root, Directory.GetDirectories, out var projectDirs))
        {
            return candidates;
        }

        foreach (var projectDir in projectDirs)
        {
            var projectDirectoryName = Path.GetFileName(projectDir);

            if (!TryGetEntries(projectDir, Directory.GetFileSystemEntries, out var entries))
            {
                continue;
            }

            foreach (var entry in entries)
            {
                if (Directory.Exists(entry))
                {
                    AddSubagentCandidates(candidates, projectDir, projectDirectoryName, entry);
                }
                else if (entry.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase))
                {
                    AddMainSessionCandidate(candidates, projectDirectoryName, entry);
                }
            }
        }

        return candidates;
    }

    private static void AddMainSessionCandidate(List<CandidateFile> candidates, string projectDirectoryName, string filePath)
    {
        var stem = Path.GetFileNameWithoutExtension(filePath);
        if (!Guid.TryParse(stem, out var sessionId))
        {
            return;
        }

        candidates.Add(new CandidateFile(filePath, projectDirectoryName, sessionId, filePath, MetaJsonPath: null, Path.GetFileName(filePath)));
    }

    private static void AddSubagentCandidates(List<CandidateFile> candidates, string projectDir, string projectDirectoryName, string sessionDir)
    {
        var dirName = Path.GetFileName(sessionDir);
        if (!Guid.TryParse(dirName, out var sessionId))
        {
            return;
        }

        var siblingJsonl = Path.Combine(projectDir, $"{dirName}.jsonl");
        if (!File.Exists(siblingJsonl))
        {
            return;
        }

        var subagentsDir = Path.Combine(sessionDir, "subagents");
        if (!TryGetEntries(subagentsDir, d => Directory.Exists(d) ? Directory.GetFiles(d, "agent-*.jsonl") : Array.Empty<string>(), out var subagentFiles))
        {
            return;
        }

        foreach (var subagentFile in subagentFiles)
        {
            var metaPath = Path.ChangeExtension(subagentFile, null) + ".meta.json";
            var displayPath = Path.Combine(dirName, "subagents", Path.GetFileName(subagentFile));
            candidates.Add(new CandidateFile(subagentFile, projectDirectoryName, sessionId, siblingJsonl, File.Exists(metaPath) ? metaPath : null, displayPath));
        }
    }

    private static DateTime TryGetLastWriteTime(string path)
    {
        try
        {
            return File.GetLastWriteTime(path);
        }
        catch (IOException)
        {
            return DateTime.MinValue;
        }
    }

    private static bool TryGetEntries(string path, Func<string, string[]> getEntries, out string[] entries)
    {
        try
        {
            entries = getEntries(path);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            entries = Array.Empty<string>();
            return false;
        }
    }

    private static async Task<bool> FileContainsAllWordsAsync(string path, string[] words, CancellationToken cancellationToken)
    {
        var found = new bool[words.Length];
        var remaining = words.Length;

        try
        {
            using var reader = new StreamReader(path);
            while (await reader.ReadLineAsync(cancellationToken) is { } line)
            {
                for (var i = 0; i < words.Length; i++)
                {
                    if (!found[i] && line.Contains(words[i], StringComparison.OrdinalIgnoreCase))
                    {
                        found[i] = true;
                        remaining--;
                    }
                }

                if (remaining == 0)
                {
                    return true;
                }
            }
        }
        catch (IOException)
        {
            // Locked/mid-write file - treat as no match rather than failing the whole search.
            return false;
        }

        return false;
    }

    /// <summary>
    /// Full streaming pass over a MAIN session file - the first "cwd" wins (fixed once set), and
    /// every "ai-title" line overwrites so the LAST one (by scan order) is what's returned, since
    /// Claude Code re-emits an updated title through a conversation.
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

    private static async Task<string?> TryReadMetaDescriptionAsync(string metaJsonPath, CancellationToken cancellationToken)
    {
        try
        {
            var json = await File.ReadAllTextAsync(metaJsonPath, cancellationToken);
            return TryGetStringProperty(json, "description");
        }
        catch (IOException)
        {
            return null;
        }
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
            // Partial/malformed line (e.g. a crash mid-write) - skip it rather than failing the scan.
            return null;
        }
    }
}
