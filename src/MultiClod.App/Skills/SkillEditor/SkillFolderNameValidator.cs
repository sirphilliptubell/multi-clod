using System.IO;
using System.Text;

namespace MultiClod.App.Skills.SkillEditor;

/// <summary>
/// A new skill's folder name becomes its `/skill-name` command (see
/// https://code.claude.com/docs/en/skills.md), so it's kept to lowercase letters, digits, and
/// hyphens as-you-type rather than allowing anything filesystem-legal.
/// </summary>
internal static class SkillFolderNameValidator
{
    public static string Sanitize(string input)
    {
        var builder = new StringBuilder(input.Length);
        foreach (var c in input.ToLowerInvariant())
        {
            if (c is >= 'a' and <= 'z' or >= '0' and <= '9' or '-')
            {
                builder.Append(c);
            }
        }

        return builder.ToString();
    }

    /// <summary>
    /// Validates an already-sanitized name against a target skills root. Blocking rather than
    /// guessing, same as the frontmatter YAML textarea's own error handling: an empty or
    /// already-taken name shows an inline error and blocks Save rather than picking a fallback.
    /// </summary>
    public static bool TryValidate(string sanitizedName, string skillsRoot, out string? error)
    {
        if (string.IsNullOrEmpty(sanitizedName))
        {
            error = "Enter a skill folder name.";
            return false;
        }

        if (Directory.Exists(Path.Combine(skillsRoot, sanitizedName)))
        {
            error = $"A skill named '{sanitizedName}' already exists.";
            return false;
        }

        error = null;
        return true;
    }
}
