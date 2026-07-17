namespace MultiClod.App.Skills;

/// <summary>
/// One discovered SKILL.md. Name/Description come from SkillFrontmatterParser and fall back to
/// the containing folder name / null when frontmatter is missing or malformed - see
/// SkillDiscoveryService.ScanPersonalSkills. The raw body is deliberately not carried on this
/// record; MarkdownEditorView re-reads FilePath on click instead, since skill files are small.
/// </summary>
internal sealed record SkillInfo(string Name, string? Description, string FilePath);
