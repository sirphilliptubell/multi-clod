using System.IO;
using MultiClod.App.Skills;

namespace MultiClod.App.OutputStyles;

/// <summary>
/// Scans personal output styles (~/.claude/output-styles/*.md only - no project-level output
/// styles here; the session-scoped sub-panel passes its own rootDirectoryOverride for that).
/// Mirrors Skills.SkillDiscoveryService, but output styles are flat *.md files directly under the
/// root rather than name/SKILL.md subfolders. Has no WPF dependency, same as its sibling, so
/// MultiClod.App.Tests can point it at a scratch directory instead of the real
/// ~/.claude/output-styles.
/// </summary>
internal sealed class OutputStyleDiscoveryService
{
    // Internal rather than private - SessionScope.SessionPanelAvailability reuses this for its own
    // repo-scoped ".claude/output-styles" presence check, mirroring SkillDiscoveryService.SkillFileName.
    internal const string OutputStyleFileExtension = ".md";

    private readonly string rootDirectory;

    public OutputStyleDiscoveryService(string? rootDirectoryOverride = null)
    {
        this.rootDirectory = rootDirectoryOverride ?? ClaudeOutputStylesDirectory.Root;
    }

    public IReadOnlyList<OutputStyleInfo> ScanPersonalOutputStyles()
    {
        if (!Directory.Exists(this.rootDirectory))
        {
            return Array.Empty<OutputStyleInfo>();
        }

        var results = new List<OutputStyleInfo>();
        foreach (var file in Directory.EnumerateFiles(this.rootDirectory, "*" + OutputStyleFileExtension))
        {
            SkillFrontmatterYaml.TryParse(File.ReadAllText(file), out var frontmatter, out _, out _);
            results.Add(new OutputStyleInfo(frontmatter?.Name ?? Path.GetFileNameWithoutExtension(file), frontmatter?.Description, file));
        }

        results.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        return results;
    }
}
