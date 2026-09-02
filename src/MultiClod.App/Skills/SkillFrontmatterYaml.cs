using System.IO;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace MultiClod.App.Skills;

/// <summary>
/// Splits a SKILL.md's leading `---`-delimited frontmatter block from its body and parses the
/// block with YamlDotNet's representation model (YamlMappingNode preserves key order, unlike a
/// plain dictionary) - replaces the old hand-rolled line-scanner (SkillFrontmatterParser) so the
/// Skills list and the Skill editor (Skills\SkillEditor) never disagree about what a file's
/// frontmatter says. Kept free of any file I/O so it's unit-testable against plain string
/// literals.
/// </summary>
internal static class SkillFrontmatterYaml
{
    /// <summary>
    /// True only when a `---`-delimited block was found AND it parsed as a YAML mapping.
    /// <paramref name="rawFrontmatterBlock"/> is populated whenever a `---`-delimited block was
    /// found at all (even one that failed to parse as a mapping - invalid YAML, or a scalar/list
    /// at the root instead of a mapping), so a caller can fall back to showing that text verbatim
    /// when <paramref name="frontmatter"/> comes back null despite a block being present.
    /// </summary>
    public static bool TryParse(string rawText, out SkillFrontmatter? frontmatter, out string? rawFrontmatterBlock, out string body)
    {
        var normalized = rawText.Replace("\r\n", "\n");
        var lines = normalized.Split('\n');
        if (lines.Length == 0 || lines[0].Trim() != "---")
        {
            frontmatter = null;
            rawFrontmatterBlock = null;
            body = normalized;
            return false;
        }

        var closingIndex = -1;
        for (var i = 1; i < lines.Length; i++)
        {
            if (lines[i].Trim() == "---")
            {
                closingIndex = i;
                break;
            }
        }

        string yamlBlock;
        if (closingIndex < 0)
        {
            // Unterminated block - matches the old parser's "whatever was found before EOF"
            // behavior: everything after the opening --- is the yaml block, body is empty.
            yamlBlock = string.Join('\n', lines.Skip(1));
            body = string.Empty;
        }
        else
        {
            yamlBlock = string.Join('\n', lines.Skip(1).Take(closingIndex - 1));
            body = string.Join('\n', lines.Skip(closingIndex + 1));
        }

        rawFrontmatterBlock = yamlBlock;
        return TryParseBlock(yamlBlock, out frontmatter);
    }

    /// <summary>
    /// Parses just the inner YAML text of a frontmatter block (no surrounding `---` markers) -
    /// used by SkillEditorView's own frontmatter textarea, which holds exactly that. True only when
    /// it parses as a YAML mapping; false for invalid YAML or a non-mapping root (a bare scalar or
    /// sequence), matching TryParse's own rules for the same two cases.
    /// </summary>
    public static bool TryParseBlock(string yamlBlock, out SkillFrontmatter? frontmatter)
    {
        YamlMappingNode? mapping;
        try
        {
            mapping = ParseMapping(yamlBlock);
        }
        catch (YamlException)
        {
            frontmatter = null;
            return false;
        }

        if (mapping is null)
        {
            frontmatter = null;
            return false;
        }

        frontmatter = new SkillFrontmatter(mapping);
        return true;
    }

    // Returns an empty mapping for a blank block (e.g. "---\n---\n" with nothing between the
    // markers) and null when the block parses but its root isn't a mapping at all (a bare scalar
    // or sequence) - both are distinguishable from a YamlException, which propagates to the
    // caller instead of being caught here.
    private static YamlMappingNode? ParseMapping(string yamlBlock)
    {
        if (string.IsNullOrWhiteSpace(yamlBlock))
        {
            return new YamlMappingNode();
        }

        var stream = new YamlStream();
        using var reader = new StringReader(yamlBlock);
        stream.Load(reader);

        if (stream.Documents.Count == 0)
        {
            return new YamlMappingNode();
        }

        return stream.Documents[0].RootNode as YamlMappingNode;
    }

    /// <summary>
    /// Re-emits `---\n&lt;mapping&gt;\n---\n&lt;body&gt;` - the mapping's own YamlDotNet emission
    /// preserves whatever key order and per-field scalar/sequence shape SkillFrontmatter's setters
    /// left it in (see that type for the order/shape rules).
    /// </summary>
    public static string Serialize(SkillFrontmatter frontmatter, string body)
    {
        var yamlBlock = EmitMapping(frontmatter.Mapping);
        var normalizedBody = body.Replace("\r\n", "\n");
        return $"---\n{yamlBlock}---\n{normalizedBody}";
    }

    /// <summary>
    /// Just the mapping's emitted YAML text, with no surrounding `---` markers and no trailing
    /// newline forced - used by SkillEditorView to refresh its frontmatter textarea after a
    /// left-panel field change, without also touching the body.
    /// </summary>
    public static string SerializeBlock(SkillFrontmatter frontmatter)
    {
        var text = EmitMapping(frontmatter.Mapping);
        return text.Length > 0 && text[^1] == '\n' ? text[..^1] : text;
    }

    private static string EmitMapping(YamlMappingNode mapping)
    {
        if (mapping.Children.Count == 0)
        {
            return string.Empty;
        }

        var document = new YamlDocument(mapping);
        var stream = new YamlStream(document);
        using var writer = new StringWriter();
        stream.Save(writer, assignAnchors: false);

        // YamlStream.Save emits "\r\n" line breaks regardless of the TextWriter, and always ends
        // with a "...\n" document-end marker - neither belongs inside a SKILL.md frontmatter
        // block, so normalize line endings first and trim the end marker back off.
        var text = writer.ToString().Replace("\r\n", "\n");
        var endMarkerIndex = text.LastIndexOf("...", StringComparison.Ordinal);
        return endMarkerIndex >= 0 ? text[..endMarkerIndex] : text;
    }
}
