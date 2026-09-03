namespace MultiClod.Terminal.Abstractions;

/// <summary>
/// The effective environment a session's child process actually receives: this app's own process
/// environment, minus the VS Code / editor injected vars below. Shared (rather than duplicated in
/// ProcessFactory) so anything else that needs "what claude would actually see" - e.g. the
/// Environment Variables session menu - can't drift out of sync with what ProcessFactory really
/// strips when building the child process's environment block.
/// </summary>
public static class EnvironmentSnapshot
{
    // Set by VS Code on any process it launches/debugs (F5, not just its integrated terminal),
    // regardless of the "console" setting in launch.json - "externalTerminal" only changes where
    // stdio is connected, not what env vars the debuggee inherits. Confirmed by hand: a session
    // launched this way runs claude interactively (real responses, real MCP servers) but silently
    // never persists a transcript or registers in ~/.claude/sessions - the same launch outside VS
    // Code works fine. TERM_PROGRAM=vscode is the most likely single trigger (a common convention
    // CLIs use to detect "running inside an editor" and special-case behavior), but since none of
    // these are needed by claude and stripping them can't break anything, the whole family gets
    // scrubbed rather than betting on isolating the exact one.
    private static readonly HashSet<string> EditorInjectedVariablesToStrip = new(StringComparer.OrdinalIgnoreCase)
    {
        "TERM_PROGRAM",
        "TERM_PROGRAM_VERSION",
        "VSCODE_PID",
        "VSCODE_CWD",
        "VSCODE_NLS_CONFIG",
        "VSCODE_IPC_HOOK",
        "VSCODE_IPC_HOOK_CLI",
        "VSCODE_INJECTION",
        "VSCODE_IPC_HOOK_EXTHOST",
        "VSCODE_IPC_HOOK_CLI_EXTHOST",
        "VSCODE_GIT_ASKPASS_NODE",
        "VSCODE_GIT_ASKPASS_EXTRA_ARGS",
        "VSCODE_GIT_ASKPASS_MAIN",
        "VSCODE_GIT_IPC_HANDLE",
        "VSCODE_INSPECTOR_OPTIONS",
        "ELECTRON_RUN_AS_NODE",
    };

    public static IReadOnlyDictionary<string, string> GetEffective()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            var key = (string)entry.Key;
            if (EditorInjectedVariablesToStrip.Contains(key))
            {
                continue;
            }

            result[key] = (string?)entry.Value ?? string.Empty;
        }

        // Windows fixes a process's own environment block (the loop above) at process-creation
        // time - it's never updated from the registry afterward, so a long-running MultiClod.App
        // instance would otherwise keep launching new sessions with whatever System/User env vars
        // were current when *it* started, even after the user edits them (System Properties, or
        // setx) without restarting the app. EnvironmentVariableTarget.Machine/User read straight
        // from the registry on every call, so overlaying them here (User last, matching Windows'
        // own precedence when it builds a fresh process's environment block) picks up edits made
        // to persisted env vars at launch time instead of a stale copy from whenever this process
        // started. Only covers persisted (System/User) vars, not ephemeral ones set in a parent
        // shell that isn't itself reread - the same limitation a freshly-launched process would
        // have.
        OverlayTarget(result, EnvironmentVariableTarget.Machine);
        OverlayTarget(result, EnvironmentVariableTarget.User);

        return result;
    }

    private static void OverlayTarget(Dictionary<string, string> result, EnvironmentVariableTarget target)
    {
        foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables(target))
        {
            var key = (string)entry.Key;
            if (EditorInjectedVariablesToStrip.Contains(key))
            {
                continue;
            }

            var value = (string?)entry.Value ?? string.Empty;

            // PATH is special-cased: Windows itself builds a fresh process's PATH by concatenating
            // Machine then User Path (System Properties' own combined view), not by one replacing
            // the other. Overlaying User after Machine here with a flat assignment would otherwise
            // silently drop every Machine-only PATH entry (e.g. C:\Program Files\dotnet\, which is
            // Machine-scoped while global dotnet tool shims live under the User-scoped
            // %USERPROFILE%\.dotnet\tools) - breaking anything a hook shells out to that isn't also
            // on the User PATH.
            if (string.Equals(key, "Path", StringComparison.OrdinalIgnoreCase))
            {
                result[key] = result.TryGetValue(key, out var existing) && existing.Length > 0
                    ? $"{existing};{value}"
                    : value;
            }
            else
            {
                result[key] = value;
            }
        }
    }
}
