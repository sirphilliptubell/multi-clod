using System.IO;
using System.Linq;
using MultiClod.App.Skills;

namespace MultiClod.App.SessionScope;

/// <summary>
/// Whether the session-scoped sub-panel has anything to show for a given working directory -
/// drives both the grey-out state of its two icons and whether the panel auto-collapses/expands
/// when the foreground tab switches. Computed fresh on every foreground-tab change rather than
/// cached: it's a handful of cheap filesystem checks plus one `git rev-parse` for the repo root.
/// </summary>
internal readonly record struct SessionPanelAvailability(bool HasMemories, bool HasContextSkills)
{
    public static SessionPanelAvailability Compute(string workingDirectory)
    {
        var hasMemories = HasAnyMemoryFile(SessionScopedPaths.GetMemoryDirectory(workingDirectory));
        var hasContextSkills = SessionScopedPaths.TryGetRepoRoot(workingDirectory, out var repoRoot) && HasContextOrSkills(repoRoot);
        return new SessionPanelAvailability(hasMemories, hasContextSkills);
    }

    // Split out from Compute so tests can exercise the actual filesystem logic against a scratch
    // directory directly, without needing a real git repo or mutating CLAUDE_CONFIG_DIR (which
    // ClaudeConfigDirectory.Root - and therefore SessionScopedPaths.GetMemoryDirectory - can't
    // observe reliably anyway, since Root is a static field evaluated only once per process).
    internal static bool HasAnyMemoryFile(string memoryDirectory) =>
        Directory.Exists(memoryDirectory) && Directory.EnumerateFiles(memoryDirectory, "*.md").Any();

    internal static bool HasContextOrSkills(string repoRoot) =>
        File.Exists(Path.Combine(repoRoot, "CLAUDE.md")) || HasAnySkill(repoRoot);

    private static bool HasAnySkill(string repoRoot)
    {
        var skillsRoot = Path.Combine(repoRoot, ".claude", "skills");
        return Directory.Exists(skillsRoot)
            && Directory.EnumerateDirectories(skillsRoot).Any(dir => File.Exists(Path.Combine(dir, SkillDiscoveryService.SkillFileName)));
    }
}
