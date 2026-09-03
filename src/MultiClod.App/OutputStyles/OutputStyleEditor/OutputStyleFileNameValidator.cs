using System.IO;
using System.Text;

namespace MultiClod.App.OutputStyles.OutputStyleEditor;

/// <summary>
/// A new output style's file name has no slash-command constraint the way a skill's folder name
/// does (Skills\SkillEditor\SkillFolderNameValidator forces lowercase/digits/hyphens because that
/// becomes a `/skill-name` command) - an output style is just displayed under whatever `name` the
/// frontmatter sets, or the bare file name if `name` is omitted (see
/// https://code.claude.com/docs/en/output-styles.md). So this only strips characters Windows
/// actually rejects in a file name, preserving case/spaces/underscores/etc. otherwise.
/// </summary>
internal static class OutputStyleFileNameValidator
{
    private static readonly char[] InvalidFileNameChars = ['<', '>', ':', '"', '/', '\\', '|', '?', '*'];

    public static string Sanitize(string input)
    {
        var builder = new StringBuilder(input.Length);
        foreach (var c in input)
        {
            if (!char.IsControl(c) && Array.IndexOf(InvalidFileNameChars, c) < 0)
            {
                builder.Append(c);
            }
        }

        // Windows trims trailing dots/spaces from a file name itself (silently, at the OS level) -
        // stripped here too so what's shown in the (still-editable) box matches what would actually
        // land on disk, rather than looking valid until Save reveals a different final name.
        return builder.ToString().TrimEnd('.', ' ');
    }

    /// <summary>
    /// Validates an already-sanitized name against a target output styles root. Blocking rather
    /// than guessing, same as the frontmatter YAML textarea's own error handling: an empty or
    /// already-taken name shows an inline error and blocks Save rather than picking a fallback.
    /// </summary>
    public static bool TryValidate(string sanitizedName, string outputStylesRoot, out string? error)
    {
        if (string.IsNullOrEmpty(sanitizedName))
        {
            error = "Enter a file name.";
            return false;
        }

        if (File.Exists(Path.Combine(outputStylesRoot, sanitizedName + OutputStyleDiscoveryService.OutputStyleFileExtension)))
        {
            error = $"An output style named '{sanitizedName}' already exists.";
            return false;
        }

        error = null;
        return true;
    }
}
