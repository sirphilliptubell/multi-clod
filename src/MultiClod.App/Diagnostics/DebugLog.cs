using System.Diagnostics;
using System.IO;
using MultiClod.App.Persistence;

namespace MultiClod.App.Diagnostics;

/// <summary>
/// Ad hoc, file-based logging for chasing runtime bugs that are awkward to catch under a debugger
/// (hook subprocess timing, PTY escape-sequence parsing, etc.) - add a scoped method per area as
/// new investigations come up (see <see cref="LogTerminal"/>). Every method is
/// [Conditional("DEBUG")], so Release builds strip every call site entirely (arguments included) -
/// callers never need their own #if DEBUG, and there's zero runtime cost outside Debug.
/// </summary>
public static class DebugLog
{
    // Computed once per process rather than per call, so every log file a single run produces -
    // whether written directly by this class or by an external process we hand this stamp to (see
    // ClaudeSessionHooksInstaller passing it to claude-session-signal.ps1) - shares one filename
    // suffix. That makes it trivial to find every log from one investigation session.
    public static readonly string RunTimestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");

    private static readonly object WriteLock = new();

    /// <summary>
    /// Traces raw title callbacks TerminalSession.ApplyTitle receives from the PTY output stream -
    /// first added to diagnose why the /clear session-id sync doesn't always correct drift.
    /// </summary>
    [Conditional("DEBUG")]
    public static void LogTerminal(string message) => Write("terminal", message);

    /// <summary>
    /// Appends a "-DebugLogPath &lt;path&gt;" argument (so claude-session-signal.ps1 logs its own
    /// hook payloads) to a hook command line being built - see ClaudeSessionHooksInstaller. A ref
    /// parameter rather than a return value so this can still be [Conditional("DEBUG")]: Release
    /// builds strip the call entirely, leaving commandLine untouched, the same as the old
    /// #if DEBUG/#else construct it replaces.
    /// </summary>
    [Conditional("DEBUG")]
    public static void AppendHookDebugLogArg(ref string commandLine, string dataDirectory) =>
        commandLine += $" -DebugLogPath \"{Path.Combine(dataDirectory, $"debug-hooks-{RunTimestamp}.log")}\"";

    private static void Write(string channel, string message)
    {
        var path = Path.Combine(MultiClodDataDirectory.Root, $"debug-{channel}-{RunTimestamp}.log");
        var line = $"[{DateTimeOffset.Now:O}] {message}{Environment.NewLine}";

        lock (WriteLock)
        {
            try
            {
                File.AppendAllText(path, line);
            }
            catch (IOException)
            {
                // Best-effort - a locked/missing log file must never break the feature being
                // traced.
            }
        }
    }
}
