using System.IO;

namespace MultiClod.App.Skills.SkillEditor;

/// <summary>
/// The loaded/editable state behind SkillEditorView: a SKILL.md's frontmatter (as an ordered
/// SkillFrontmatter mapping) plus its body text, and whether this is a brand-new, not-yet-saved
/// skill (<see cref="IsNew"/>) or an existing one loaded from disk. For a new skill,
/// <see cref="FolderPath"/> starts out as the *root* skills directory (personal or project) it
/// will be created under - see <see cref="CreateNew"/> - and only becomes the skill's own
/// directory once <see cref="Save"/> is given a chosen folder name.
/// </summary>
internal sealed class SkillDocument
{
    private SkillDocument(string folderPath, bool isNew, SkillFrontmatter frontmatter, string body, string? rawFrontmatterBlock)
    {
        this.FolderPath = folderPath;
        this.IsNew = isNew;
        this.Frontmatter = frontmatter;
        this.Body = body;
        this.RawFrontmatterBlock = rawFrontmatterBlock;
    }

    public string FolderPath { get; private set; }

    public bool IsNew { get; private set; }

    public SkillFrontmatter Frontmatter { get; set; }

    public string Body { get; set; }

    /// <summary>
    /// Populated only when the file's frontmatter failed to parse as a YAML mapping (invalid YAML,
    /// or a non-mapping root) - SkillEditorView falls back to a raw-text-only mode showing this
    /// verbatim rather than any value derived from <see cref="Frontmatter"/> (which is just an
    /// empty mapping in that case).
    /// </summary>
    public string? RawFrontmatterBlock { get; }

    public string FilePath => Path.Combine(this.FolderPath, SkillDiscoveryService.SkillFileName);

    public static SkillDocument Load(string filePath)
    {
        var rawText = File.ReadAllText(filePath);
        var parsed = SkillFrontmatterYaml.TryParse(rawText, out var frontmatter, out var rawBlock, out var body);
        return new SkillDocument(
            Path.GetDirectoryName(filePath)!,
            isNew: false,
            frontmatter: parsed ? frontmatter! : SkillFrontmatter.CreateEmpty(),
            body: body,
            rawFrontmatterBlock: parsed ? null : rawBlock);
    }

    public static SkillDocument CreateNew(string skillsRoot) =>
        new(skillsRoot, isNew: true, SkillFrontmatter.CreateEmpty(), body: string.Empty, rawFrontmatterBlock: null);

    /// <summary>
    /// Writes frontmatter+body to disk. <paramref name="newFolderName"/> is required (and moves
    /// <see cref="FolderPath"/> under the root it was created with) the first time a new skill is
    /// saved; the caller (SkillEditorView) is responsible for validating it first via
    /// SkillFolderNameValidator - this method assumes it's already a valid, unique, sanitized name.
    /// </summary>
    public void Save(string? newFolderName = null) =>
        this.WriteToDisk(SkillFrontmatterYaml.Serialize(this.Frontmatter, this.Body), newFolderName);

    /// <summary>
    /// Used only in the raw-text-only fallback mode (<see cref="RawFrontmatterBlock"/> is
    /// non-null): writes the two textareas' current text verbatim, bypassing
    /// <see cref="Frontmatter"/>/structural serialization entirely, since there's nothing
    /// structurally valid to re-emit from.
    /// </summary>
    public void SaveRaw(string frontmatterBlockText, string bodyText, string? newFolderName = null) =>
        this.WriteToDisk($"---\n{frontmatterBlockText}\n---\n{bodyText}", newFolderName);

    private void WriteToDisk(string fileContent, string? newFolderName)
    {
        if (this.IsNew)
        {
            this.FolderPath = Path.Combine(this.FolderPath, newFolderName!);
        }

        Directory.CreateDirectory(this.FolderPath);
        File.WriteAllText(this.FilePath, fileContent);
        this.IsNew = false;
    }
}
