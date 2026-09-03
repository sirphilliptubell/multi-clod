using MultiClod.App.Frontmatter;

namespace MultiClod.App.Skills;

/// <summary>
/// Thin SKILL.md-specific wrapper around the generic Frontmatter.FrontmatterYaml split/parse/
/// serialize engine, constructing SkillFrontmatter from the parsed mapping. Replaces the old
/// hand-rolled line-scanner (SkillFrontmatterParser) so the Skills list and the Skill editor
/// (Skills\SkillEditor) never disagree about what a file's frontmatter says.
/// </summary>
internal static class SkillFrontmatterYaml
{
    public static bool TryParse(string rawText, out SkillFrontmatter? frontmatter, out string? rawFrontmatterBlock, out string body) =>
        FrontmatterYaml.TryParse(rawText, m => new SkillFrontmatter(m), out frontmatter, out rawFrontmatterBlock, out body);

    /// <summary>
    /// Parses just the inner YAML text of a frontmatter block (no surrounding `---` markers) - used
    /// by SkillEditorView's own frontmatter textarea, which holds exactly that.
    /// </summary>
    public static bool TryParseBlock(string yamlBlock, out SkillFrontmatter? frontmatter) =>
        FrontmatterYaml.TryParseBlock(yamlBlock, m => new SkillFrontmatter(m), out frontmatter);

    public static string Serialize(SkillFrontmatter frontmatter, string body) =>
        FrontmatterYaml.Serialize(frontmatter, body);

    /// <summary>
    /// Just the mapping's emitted YAML text, with no surrounding `---` markers and no trailing
    /// newline forced - used by SkillEditorView to refresh its frontmatter textarea after a
    /// left-panel field change, without also touching the body.
    /// </summary>
    public static string SerializeBlock(SkillFrontmatter frontmatter) =>
        FrontmatterYaml.SerializeBlock(frontmatter);
}
