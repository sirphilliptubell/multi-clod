namespace MultiClod.App.Skills;

/// <summary>
/// Pulls `name`/`description` out of a SKILL.md's leading `---`-delimited frontmatter block,
/// without a real YAML parser - matches ClaudeProjectPath's preference for simple string
/// handling over a new dependency for a single-purpose extraction. Kept free of any file I/O
/// so it's unit-testable against plain string literals (see SkillFrontmatterParserTests).
/// </summary>
internal static class SkillFrontmatterParser
{
    public static (string? Name, string? Description) Parse(string rawText)
    {
        var lines = rawText.Replace("\r\n", "\n").Split('\n');
        if (lines.Length == 0 || lines[0].Trim() != "---")
        {
            return (null, null);
        }

        string? name = null;
        string? description = null;

        for (var i = 1; i < lines.Length; i++)
        {
            var line = lines[i];
            if (line.Trim() == "---")
            {
                break;
            }

            if (TryParseKeyValue(line, "name", out var nameValue))
            {
                name = nameValue;
            }
            else if (TryParseKeyValue(line, "description", out var descriptionValue))
            {
                description = descriptionValue;
            }
        }

        return (name, description);
    }

    private static bool TryParseKeyValue(string line, string key, out string value)
    {
        value = string.Empty;

        var trimmed = line.TrimStart();
        var prefix = key + ":";
        if (!trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        value = Unquote(trimmed[prefix.Length..].Trim());
        return value.Length > 0;
    }

    private static string Unquote(string value)
    {
        if (value.Length >= 2 && ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\'')))
        {
            return value[1..^1];
        }

        return value;
    }
}
