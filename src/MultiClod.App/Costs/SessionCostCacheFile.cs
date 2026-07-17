using System.IO;
using System.Text.Json;

namespace MultiClod.App.Costs;

/// <summary>
/// Sidecar cache next to one JSONL log file ("&lt;name&gt;.jsonl" -> "&lt;name&gt;.mc.json"), so a
/// session's cost doesn't need re-deriving from byte 0 on every app launch. A ModelCostsUsd value
/// is null when that model was seen but had no matching ClaudeModelPricing rate at the time it was
/// read - see SessionCostFileProcessor for how that null gets resolved later (self-heal).
/// </summary>
internal sealed class SessionCostCacheFile
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public long LastReadOffset { get; set; }

    // Explicitly UTC (not local) so a comparison against FileInfo.LastWriteTimeUtc is never thrown
    // off by DST or the machine's local timezone.
    public DateTime SourceLastWriteUtc { get; set; }

    public Dictionary<string, decimal?> ModelCostsUsd { get; set; } = new();

    // "<id>.jsonl" -> "<id>.mc.json"; "agent-<id>.jsonl" -> "agent-<id>.mc.json" - same rule for
    // both the main log and a subagent log, no special-casing needed.
    public static string SidecarPathFor(string jsonlPath) =>
        Path.Combine(
            Path.GetDirectoryName(jsonlPath) ?? string.Empty,
            Path.GetFileNameWithoutExtension(jsonlPath) + ".mc.json");

    public static SessionCostCacheFile? TryLoad(string sidecarPath)
    {
        if (!File.Exists(sidecarPath))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<SessionCostCacheFile>(File.ReadAllText(sidecarPath), JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    public static void Save(string sidecarPath, SessionCostCacheFile data)
    {
        try
        {
            var directory = Path.GetDirectoryName(sidecarPath);
            if (directory is { Length: > 0 })
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(data, JsonOptions);
            var tmpPath = sidecarPath + ".tmp";
            File.WriteAllText(tmpPath, json);

            // Write-then-move, same defense-in-depth SessionStore uses for sessions.json - a crash
            // mid-write never leaves a half-written (unparseable) sidecar behind.
            File.Move(tmpPath, sidecarPath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best-effort persistence: a locked file or full disk shouldn't crash the monitor. The
            // next check (debounce/backstop) will simply retry from the last successfully-saved
            // offset.
        }
    }
}
