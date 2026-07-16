using System;
using System.IO;
using System.Text.RegularExpressions;

namespace MultiClod.App.Validation;

/// <summary>
/// Mirrors how the claude CLI derives its per-project storage path under ~/.claude/projects/, so
/// SessionValidator can check whether a --resume'd session's transcript still exists without
/// spawning claude itself.
/// </summary>
internal static class ClaudeProjectPath
{
    // Verified against this machine's real ~/.claude/projects/ - it's not just \, /, : that get
    // replaced. E.g. C:\_Gits-GS-Github\multi-claude -> C---Gits-GS-Github-multi-claude (the '_'
    // is gone too). Any character outside [A-Za-z0-9-]
    // becomes '-'; only hyphens already in the path survive untouched.
    private static readonly Regex NonAlphanumericOrHyphen = new("[^A-Za-z0-9-]", RegexOptions.Compiled);

    /// <summary>
    /// Uppercases the drive letter, then replaces every character that isn't a letter, digit, or
    /// hyphen with '-'. Example: C:\_Gits-GS-Github\multi-claude -> C---Gits-GS-Github-multi-claude.
    /// </summary>
    public static string Encode(string workingDirectory)
    {
        var normalized = Path.GetFullPath(workingDirectory);
        var chars = normalized.ToCharArray();
        if (chars.Length >= 2 && chars[1] == ':')
        {
            chars[0] = char.ToUpperInvariant(chars[0]);
        }

        return NonAlphanumericOrHyphen.Replace(new string(chars), "-");
    }

    public static string GetSessionFilePath(string workingDirectory, Guid claudeSessionId)
    {
        var claudeHome = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var projectDir = Path.Combine(claudeHome, ".claude", "projects", Encode(workingDirectory));
        return Path.Combine(projectDir, $"{claudeSessionId}.jsonl");
    }
}
