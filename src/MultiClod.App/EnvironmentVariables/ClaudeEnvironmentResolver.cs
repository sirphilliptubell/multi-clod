using System.IO;
using System.Linq;
using System.Text.Json;
using MultiClod.App.Context;
using MultiClod.Terminal.Abstractions;

namespace MultiClod.App.EnvironmentVariables;

/// <summary>
/// Resolves "what we ultimately think claude would have resolved" for a session's environment:
/// the OS environment the child process actually receives (see
/// <see cref="EnvironmentSnapshot.GetEffective"/>, filtered to <see cref="ClaudeEnvVarNames"/>),
/// overlaid with the "env" block of each applicable settings file in Claude Code's real precedence
/// order (lowest to highest: user, project, project-local). Enterprise managed policy and CLI-arg
/// overrides are out of scope - see the plan's decision to skip them. Called once per
/// start/resume (MainWindow.LaunchSession) - never re-resolved just from opening the menu, so the
/// menu reflects what this session actually launched with, not the current on-disk state.
/// </summary>
internal static class ClaudeEnvironmentResolver
{
    // userSettingsPathOverride exists only so tests can pin the merge/precedence behavior without
    // depending on ClaudeConfigDirectory.Root, which is a static readonly resolved once per process
    // from CLAUDE_CONFIG_DIR - see ClaudeSessionHooksInstaller's dataDirectoryOverride for the same
    // pattern already used in this codebase.
    public static IReadOnlyList<ClaudeEnvironmentVariable> Resolve(string workingDirectory, string? userSettingsPathOverride = null)
    {
        var merged = new Dictionary<string, ClaudeEnvironmentVariable>(StringComparer.OrdinalIgnoreCase);

        foreach (var (key, value) in EnvironmentSnapshot.GetEffective())
        {
            if (ClaudeEnvVarNames.IsRelevant(key))
            {
                merged[key] = new ClaudeEnvironmentVariable(key, value, EnvironmentVariableSource.OsEnvironment);
            }
        }

        var userSettingsPath = userSettingsPathOverride ?? Path.Combine(ClaudeConfigDirectory.Root, "settings.json");
        Overlay(merged, userSettingsPath, EnvironmentVariableSource.UserSettings);
        Overlay(merged, Path.Combine(workingDirectory, ".claude", "settings.json"), EnvironmentVariableSource.ProjectSettings);
        Overlay(merged, Path.Combine(workingDirectory, ".claude", "settings.local.json"), EnvironmentVariableSource.ProjectLocalSettings);

        return merged.Values.OrderBy(v => v.Key, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static void Overlay(Dictionary<string, ClaudeEnvironmentVariable> merged, string settingsPath, EnvironmentVariableSource source)
    {
        foreach (var (key, value) in ReadEnvBlock(settingsPath))
        {
            merged[key] = new ClaudeEnvironmentVariable(key, value, source);
        }
    }

    // A settings file's "env" object can name literally anything (e.g. HTTP_PROXY), not just
    // Claude-prefixed vars - unlike the OS-environment scan above, nothing here is filtered by
    // ClaudeEnvVarNames.IsRelevant, since an explicit entry in a Claude settings file is Claude's
    // own resolved environment regardless of name.
    private static Dictionary<string, string> ReadEnvBlock(string settingsPath)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(settingsPath))
        {
            return result;
        }

        try
        {
            using var stream = File.OpenRead(settingsPath);
            using var document = JsonDocument.Parse(stream);
            if (!document.RootElement.TryGetProperty("env", out var envElement) || envElement.ValueKind != JsonValueKind.Object)
            {
                return result;
            }

            foreach (var property in envElement.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.String)
                {
                    result[property.Name] = property.Value.GetString() ?? string.Empty;
                }
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // Malformed/unreadable settings file - treated as absent rather than surfaced as an
            // error; this is a best-effort display, not a validator, and claude itself would be
            // the one to fail on a genuinely broken settings file.
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        return result;
    }
}
