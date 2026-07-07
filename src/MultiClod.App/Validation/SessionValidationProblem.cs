using System.IO;

namespace MultiClod.App.Validation;

public enum SessionValidationProblem
{
    None,
    WorkingDirectoryMissing,
    ClaudeDataMissing,
}

internal static class SessionValidator
{
    public static SessionValidationProblem Validate(SessionNodeViewModel node)
    {
        if (!Directory.Exists(node.WorkingDirectory))
        {
            return SessionValidationProblem.WorkingDirectoryMissing;
        }

        // Only meaningful once we've actually launched with --session-id at least once - a
        // never-started node has no claude-side data yet, and that's expected, not a problem.
        if (node.HasBeenStarted && !File.Exists(ClaudeProjectPath.GetSessionFilePath(node.WorkingDirectory, node.ClaudeSessionId)))
        {
            return SessionValidationProblem.ClaudeDataMissing;
        }

        return SessionValidationProblem.None;
    }
}
