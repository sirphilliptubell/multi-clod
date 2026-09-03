using MultiClod.App.Frontmatter;

namespace MultiClod.App.OutputStyles;

/// <summary>
/// Thin output-style-specific wrapper around the generic Frontmatter.FrontmatterYaml split/parse/
/// serialize engine, constructing OutputStyleFrontmatter from the parsed mapping. Replaces
/// OutputStyleDiscoveryService's earlier stopgap use of Skills.SkillFrontmatterYaml (accepted only
/// because nothing output-style-specific existed yet).
/// </summary>
internal static class OutputStyleFrontmatterYaml
{
    public static bool TryParse(string rawText, out OutputStyleFrontmatter? frontmatter, out string? rawFrontmatterBlock, out string body) =>
        FrontmatterYaml.TryParse(rawText, m => new OutputStyleFrontmatter(m), out frontmatter, out rawFrontmatterBlock, out body);

    /// <summary>
    /// Parses just the inner YAML text of a frontmatter block (no surrounding `---` markers) - used
    /// by OutputStyleEditorView's own frontmatter textarea, which holds exactly that.
    /// </summary>
    public static bool TryParseBlock(string yamlBlock, out OutputStyleFrontmatter? frontmatter) =>
        FrontmatterYaml.TryParseBlock(yamlBlock, m => new OutputStyleFrontmatter(m), out frontmatter);

    public static string Serialize(OutputStyleFrontmatter frontmatter, string body) =>
        FrontmatterYaml.Serialize(frontmatter, body);

    /// <summary>
    /// Just the mapping's emitted YAML text, with no surrounding `---` markers and no trailing
    /// newline forced - used by OutputStyleEditorView to refresh its frontmatter textarea after a
    /// left-panel field change, without also touching the body.
    /// </summary>
    public static string SerializeBlock(OutputStyleFrontmatter frontmatter) =>
        FrontmatterYaml.SerializeBlock(frontmatter);
}
