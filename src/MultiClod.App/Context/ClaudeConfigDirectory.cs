using System.IO;

namespace MultiClod.App.Context;

/// <summary>
/// Where the claude CLI stores its user-level config, including CLAUDE.md - ~/.claude by default,
/// overridable via the CLAUDE_CONFIG_DIR environment variable (see
/// https://code.claude.com/docs/en/claude-directory.md). Unlike Skills.ClaudeSkillsDirectory, this
/// honors the override since it's the real resolution rule the claude CLI itself follows for this
/// specific file.
/// </summary>
internal static class ClaudeConfigDirectory
{
    public static string Root { get; } = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR") is { Length: > 0 } overridden
        ? overridden
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude");

    public static string ClaudeMdPath { get; } = Path.Combine(Root, "CLAUDE.md");
}
