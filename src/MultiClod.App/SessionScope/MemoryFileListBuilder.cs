using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MultiClod.App.SessionScope;

/// <summary>
/// Lists the .md files directly under a session's memory folder, with MEMORY.md pinned first (the
/// always-loaded index file) and everything else alphabetical. Non-recursive - subfolders under
/// memory/, if any, aren't surfaced here. Mirrors Skills.SkillDiscoveryService's shape: a plain
/// static scan with no WPF dependency, so tests can point it at a scratch directory.
/// </summary>
internal static class MemoryFileListBuilder
{
    private const string PinnedFileName = "MEMORY.md";

    public static IReadOnlyList<MemoryFileNodeViewModel> Build(string memoryDirectory)
    {
        if (!Directory.Exists(memoryDirectory))
        {
            return Array.Empty<MemoryFileNodeViewModel>();
        }

        return Directory.GetFiles(memoryDirectory, "*.md")
            .OrderBy(f => string.Equals(Path.GetFileName(f), PinnedFileName, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
            .Select(f => new MemoryFileNodeViewModel(f))
            .ToList();
    }
}
