using System;
using System.IO;

namespace MultiClod.App.Persistence;

/// <summary>
/// Append-only session-diagnostics.log under MultiClodDataDirectory - written whenever a session
/// faults (see MainWindow.LaunchSession), so the exit code and whatever the process printed right
/// before dying survive past the terminal pane scrolling past it or the session/window closing.
/// Best-effort, matching every other store in this folder - a failed write shouldn't turn "a
/// session faulted" into a second, unrelated failure.
/// </summary>
internal static class SessionDiagnosticsLog
{
    private static readonly object WriteLock = new();

    internal static void LogFault(string sessionName, string workingDirectory, string commandLine, int? exitCode, string outputTail)
    {
        var entry =
            $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] FAULTED session='{sessionName}'{Environment.NewLine}" +
            $"  workingDirectory: {workingDirectory}{Environment.NewLine}" +
            $"  commandLine: {commandLine}{Environment.NewLine}" +
            $"  exitCode: {(exitCode is { } code ? code.ToString() : "(unknown)")}{Environment.NewLine}" +
            $"  output tail:{Environment.NewLine}" +
            $"{IndentLines(outputTail)}{Environment.NewLine}{Environment.NewLine}";

        try
        {
            var path = Path.Combine(MultiClodDataDirectory.Root, "session-diagnostics.log");
            Directory.CreateDirectory(MultiClodDataDirectory.Root);

            lock (WriteLock)
            {
                File.AppendAllText(path, entry);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static string IndentLines(string text)
    {
        if (text.Length == 0)
        {
            return "    (none)";
        }

        var lines = text.ReplaceLineEndings("\n").Split('\n');
        return string.Join(Environment.NewLine, Array.ConvertAll(lines, line => "    " + line));
    }
}
