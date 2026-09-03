using System.IO;

namespace MultiClod.App.OutputStyles.OutputStyleEditor;

/// <summary>
/// The loaded/editable state behind OutputStyleEditorView: an output-style file's frontmatter (as
/// an ordered OutputStyleFrontmatter mapping) plus its body text, and whether this is a brand-new,
/// not-yet-saved output style (<see cref="IsNew"/>) or an existing one loaded from disk. Unlike
/// Skills\SkillEditor\SkillDocument (one subfolder per skill), output styles are flat files
/// directly under their root, so <see cref="DirectoryPath"/> never changes after construction -
/// only <see cref="FileNameWithoutExtension"/> is unset (empty) for a new document until the first
/// <see cref="Save"/> is given a chosen file name.
/// </summary>
internal sealed class OutputStyleDocument
{
    private OutputStyleDocument(string directoryPath, string fileNameWithoutExtension, bool isNew, OutputStyleFrontmatter frontmatter, string body, string? rawFrontmatterBlock)
    {
        this.DirectoryPath = directoryPath;
        this.FileNameWithoutExtension = fileNameWithoutExtension;
        this.IsNew = isNew;
        this.Frontmatter = frontmatter;
        this.Body = body;
        this.RawFrontmatterBlock = rawFrontmatterBlock;
    }

    public string DirectoryPath { get; }

    public string FileNameWithoutExtension { get; private set; }

    public bool IsNew { get; private set; }

    public OutputStyleFrontmatter Frontmatter { get; set; }

    public string Body { get; set; }

    /// <summary>
    /// Populated only when the file's frontmatter failed to parse as a YAML mapping (invalid YAML,
    /// or a non-mapping root) - OutputStyleEditorView falls back to a raw-text-only mode showing
    /// this verbatim rather than any value derived from <see cref="Frontmatter"/> (which is just an
    /// empty mapping in that case).
    /// </summary>
    public string? RawFrontmatterBlock { get; }

    public string FilePath => Path.Combine(this.DirectoryPath, this.FileNameWithoutExtension + OutputStyleDiscoveryService.OutputStyleFileExtension);

    public static OutputStyleDocument Load(string filePath)
    {
        var rawText = File.ReadAllText(filePath);
        var parsed = OutputStyleFrontmatterYaml.TryParse(rawText, out var frontmatter, out var rawBlock, out var body);
        return new OutputStyleDocument(
            Path.GetDirectoryName(filePath)!,
            Path.GetFileNameWithoutExtension(filePath),
            isNew: false,
            frontmatter: parsed ? frontmatter! : OutputStyleFrontmatter.CreateEmpty(),
            body: body,
            rawFrontmatterBlock: parsed ? null : rawBlock);
    }

    public static OutputStyleDocument CreateNew(string outputStylesRoot) =>
        new(outputStylesRoot, fileNameWithoutExtension: string.Empty, isNew: true, OutputStyleFrontmatter.CreateEmpty(), body: string.Empty, rawFrontmatterBlock: null);

    /// <summary>
    /// Writes frontmatter+body to disk. <paramref name="newFileName"/> is required (and becomes
    /// <see cref="FileNameWithoutExtension"/>) the first time a new output style is saved; the
    /// caller (OutputStyleEditorView) is responsible for validating it first via
    /// OutputStyleFileNameValidator - this method assumes it's already valid, unique, and sanitized.
    /// </summary>
    public void Save(string? newFileName = null) =>
        this.WriteToDisk(OutputStyleFrontmatterYaml.Serialize(this.Frontmatter, this.Body), newFileName);

    /// <summary>
    /// Used only in the raw-text-only fallback mode (<see cref="RawFrontmatterBlock"/> is
    /// non-null): writes the two textareas' current text verbatim, bypassing
    /// <see cref="Frontmatter"/>/structural serialization entirely, since there's nothing
    /// structurally valid to re-emit from.
    /// </summary>
    public void SaveRaw(string frontmatterBlockText, string bodyText, string? newFileName = null) =>
        this.WriteToDisk($"---\n{frontmatterBlockText}\n---\n{bodyText}", newFileName);

    private void WriteToDisk(string fileContent, string? newFileName)
    {
        if (this.IsNew)
        {
            this.FileNameWithoutExtension = newFileName!;
        }

        Directory.CreateDirectory(this.DirectoryPath);
        File.WriteAllText(this.FilePath, fileContent);
        this.IsNew = false;
    }
}
