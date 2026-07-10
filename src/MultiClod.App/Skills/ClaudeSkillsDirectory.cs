using System.IO;

namespace MultiClod.App.Skills;

/// <summary>
/// Where the claude CLI stores personal (non-project, non-plugin) skills - ~/.claude/skills.
/// Mirrors how Persistence.MultiClodDataDirectory and Validation.ClaudeProjectPath locate ~.
/// </summary>
internal static class ClaudeSkillsDirectory
{
    public static string Root { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "skills");
}
