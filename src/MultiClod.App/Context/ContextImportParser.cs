using System.Text.RegularExpressions;

namespace MultiClod.App.Context;

/// <summary>
/// Finds @path import tokens in a CLAUDE.md-style file, mirroring the claude CLI's own import
/// syntax (https://code.claude.com/docs/en/memory.md): @path anywhere in the text, except inside
/// an inline code span or a fenced code block. No I/O - pure text in, tokens out - mirrors
/// Skills.SkillFrontmatterParser's style so it's independently testable.
/// </summary>
internal static class ContextImportParser
{
    private static readonly Regex FenceLine = new(@"^\s*(```|~~~)", RegexOptions.Compiled);
    private static readonly Regex CodeSpan = new(@"(`+)(.+?)\1", RegexOptions.Compiled);
    private static readonly Regex ImportToken = new(@"@\S+", RegexOptions.Compiled);
    private const string TrailingPunctuation = ".,;:!?)]}\"'";

    /// <summary>
    /// Returns the raw import paths (leading '@' stripped) in document order, skipping any token
    /// that falls inside a fenced code block or an inline code span on its line.
    /// </summary>
    public static IReadOnlyList<string> FindImports(string rawText)
    {
        var results = new List<string>();
        var insideFence = false;

        foreach (var line in rawText.Split('\n'))
        {
            if (FenceLine.IsMatch(line))
            {
                insideFence = !insideFence;
                continue;
            }

            if (insideFence)
            {
                continue;
            }

            var codeSpanRanges = new List<(int Start, int End)>();
            foreach (Match span in CodeSpan.Matches(line))
            {
                codeSpanRanges.Add((span.Index, span.Index + span.Length));
            }

            foreach (Match token in ImportToken.Matches(line))
            {
                if (codeSpanRanges.Any(r => token.Index >= r.Start && token.Index < r.End))
                {
                    continue;
                }

                var trimmed = token.Value.TrimEnd(TrailingPunctuation.ToCharArray());
                if (trimmed.Length > 1)
                {
                    results.Add(trimmed[1..]);
                }
            }
        }

        return results;
    }
}
