using System.Text;

namespace MultiClod.App.Extensions;

internal static class StringExtensions
{
    /// <summary>
    /// Lowercases and replaces every run of whitespace/punctuation with a single hyphen, so a
    /// free-form string (e.g. a session title) becomes safe to reuse as a git branch name and
    /// Windows folder name - e.g. "Do a Thing!" -> "do-a-thing". Leading hyphens are skipped
    /// rather than trimmed after the fact (a hyphen is only appended once the builder is
    /// non-empty), and trailing ones are trimmed below.
    /// </summary>
    internal static string Slugify(this string text)
    {
        var builder = new StringBuilder(text.Length);
        var lastWasHyphen = false;

        foreach (var ch in text.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch) || ch == '-' || ch == '_')
            {
                builder.Append(ch);
                lastWasHyphen = ch == '-';
            }
            else if (!lastWasHyphen && builder.Length > 0)
            {
                builder.Append('-');
                lastWasHyphen = true;
            }
        }

        while (builder.Length > 0 && builder[^1] == '-')
        {
            builder.Length--;
        }

        return builder.ToString();
    }
}
