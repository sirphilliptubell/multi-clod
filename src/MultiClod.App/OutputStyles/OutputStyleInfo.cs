namespace MultiClod.App.OutputStyles;

/// <summary>
/// One discovered output-style *.md file. Name/Description come from Skills.SkillFrontmatterParser
/// (same frontmatter shape as SKILL.md) and fall back to the file name (no extension) / null when
/// frontmatter is missing or malformed - see OutputStyleDiscoveryService.ScanPersonalOutputStyles.
/// The raw body is deliberately not carried on this record, mirroring Skills.SkillInfo - the
/// MarkdownEditorView re-reads FilePath on click instead, since output-style files are small.
/// </summary>
internal sealed record OutputStyleInfo(string Name, string? Description, string FilePath);
