using System.IO;

namespace MultiClod.App.Skills;

/// <summary>
/// Scans personal skills (~/.claude/skills/*/SKILL.md only - no project-level or plugin skills).
/// Has no WPF dependency, mirroring Persistence.SessionStore, so MultiClod.App.Tests can point it
/// at a scratch directory instead of the real ~/.claude/skills.
/// </summary>
internal sealed class SkillDiscoveryService
{
    private const string SkillFileName = "SKILL.md";

    private readonly string rootDirectory;

    public SkillDiscoveryService(string? rootDirectoryOverride = null)
    {
        this.rootDirectory = rootDirectoryOverride ?? ClaudeSkillsDirectory.Root;
    }

    public IReadOnlyList<SkillInfo> ScanPersonalSkills()
    {
        if (!Directory.Exists(this.rootDirectory))
        {
            return Array.Empty<SkillInfo>();
        }

        var results = new List<SkillInfo>();
        foreach (var skillDirectory in Directory.EnumerateDirectories(this.rootDirectory))
        {
            var skillFile = Path.Combine(skillDirectory, SkillFileName);
            if (!File.Exists(skillFile))
            {
                continue;
            }

            var (name, description) = SkillFrontmatterParser.Parse(File.ReadAllText(skillFile));
            results.Add(new SkillInfo(name ?? Path.GetFileName(skillDirectory), description, skillFile));
        }

        results.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        return results;
    }
}
