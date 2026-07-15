using System.IO;
using System.Text.Json;
using MultiClod.App.Diagnostics;

namespace MultiClod.App.Persistence;

/// <summary>
/// Writes a Claude Code settings-overlay file (claude-session-hooks.json under
/// <see cref="MultiClodDataDirectory"/>) wiring UserPromptSubmit/Stop/Notification/PreToolUse/
/// SubagentStop hooks to claude-session-signal.ps1, so LaunchSession can pass it via
/// `claude --settings &lt;path&gt;`. Confined to sessions multi-clod itself launches - never touches
/// the user's own ~/.claude/settings.json - see the plan's "Hook scope" decision.
/// </summary>
public sealed class ClaudeSessionHooksInstaller
{
    // No naming policy here (unlike SessionStore/WindowLayoutStore's shared camelCase options) -
    // the outer hook-event keys (UserPromptSubmit, Stop, Notification) are Claude Code's own
    // PascalCase schema, not ours to reshape; a camelCase policy would rewrite them and silently
    // break hook registration.
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string dataDirectory;

    public ClaudeSessionHooksInstaller(string? dataDirectoryOverride = null)
    {
        this.dataDirectory = dataDirectoryOverride ?? MultiClodDataDirectory.Root;
    }

    // Null until EnsureInstalled() succeeds - LaunchSession skips the --settings flag entirely
    // when this is null, so a failed write just silently forgoes the activity-icon feature rather
    // than blocking a launch.
    public string? SettingsFilePath { get; private set; }

    public void EnsureInstalled()
    {
        var scriptPath = Path.Combine(AppContext.BaseDirectory, "claude-session-signal.ps1");
        if (!File.Exists(scriptPath))
        {
            // Shipped as Content in MultiClod.App.csproj - missing means a broken/partial deploy,
            // not something worth crashing over.
            return;
        }

        var settingsPath = Path.Combine(this.dataDirectory, "claude-session-hooks.json");

        string Command(string marker)
        {
            var command = $"powershell -NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\" {marker}";

            // Debug-only: have claude-session-signal.ps1 log each hook firing's raw payload/parsed
            // session_id, to diagnose why the /clear session-id sync sometimes doesn't correct
            // drift - see that script's $DebugLogPath param and DebugLog.AppendHookDebugLogArg's
            // own remarks. A no-op in Release, so production installs never write this file.
            DebugLog.AppendHookDebugLogArg(ref command, this.dataDirectory);
            return command;
        }

        object CommandHook(string marker) => new { hooks = new[] { new { type = "command", command = Command(marker) } } };

        object MatcherHook(string matcher, string marker) => new
        {
            matcher,
            hooks = new[] { new { type = "command", command = Command(marker) } },
        };

        var settings = new
        {
            hooks = new
            {
                UserPromptSubmit = new[] { CommandHook("Working") },
                Stop = new[] { CommandHook("Stop") },
                Notification = new[]
                {
                    MatcherHook("agent_needs_input", "NeedsInputSticky"),
                    MatcherHook("permission_prompt", "NeedsInputTransient"),
                },
                // Task tool calls still outstanding when Stop fires are effectively background
                // agents from this app's perspective (a blocking Task call would already have
                // returned before Stop fires) - see TerminalSession's pendingBackgroundTasks.
                PreToolUse = new[] { MatcherHook("Task", "TaskStart") },
                SubagentStop = new[] { CommandHook("TaskEnd") },
            },
        };

        try
        {
            Directory.CreateDirectory(this.dataDirectory);
            File.WriteAllText(settingsPath, JsonSerializer.Serialize(settings, JsonOptions));
            this.SettingsFilePath = settingsPath;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best-effort, matching WindowLayoutStore/SessionStore - a locked file or full disk
            // shouldn't block the app from starting; it just means no activity icons this run.
            this.SettingsFilePath = null;
        }
    }
}
