using System.IO;

namespace MultiClod.App.OutputStyles;

/// <summary>
/// Where the claude CLI stores personal (non-project) output styles - ~/.claude/output-styles.
/// Mirrors Skills.ClaudeSkillsDirectory - deliberately not CLAUDE_CONFIG_DIR-aware, same as that
/// sibling directory under the same ~/.claude root.
/// </summary>
internal static class ClaudeOutputStylesDirectory
{
    public static string Root { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "output-styles");
}
