using System.IO;

namespace MultiClod.App.Context;

/// <summary>
/// Recursively resolves CLAUDE.md and its @imports into a ContextFileNodeViewModel tree, mirroring
/// the claude CLI's own resolution rules (https://code.claude.com/docs/en/memory.md): relative
/// import paths resolve against the *importing file's* directory (not the app's working directory
/// or the root CLAUDE.md's directory), recursion stops after 4 hops past the root, and a file that
/// reappears as its own ancestor is flagged as a cycle rather than expanded again - a "diamond"
/// import from two unrelated branches is not a cycle and expands independently in each place, since
/// this walks a fresh per-branch ancestor chain rather than a single global visited-set.
/// Has no WPF dependency beyond the view-model type itself, mirroring Skills.SkillDiscoveryService,
/// so tests can point it at a scratch directory instead of the real ~/.claude.
/// </summary>
internal static class ContextTreeBuilder
{
    // CLAUDE.md itself is hop 0 (not counted toward the cap); its direct imports are hop 1. A node
    // built at hop 4 is still resolved/shown normally, but its own @imports are never parsed into
    // children - giving a real 4-hop import chain and a max tree depth of 5 levels (hops 0-4).
    private const int MaxHop = 4;

    public static ContextFileNodeViewModel BuildRoot(string? claudeMdPathOverride = null)
    {
        var rootPath = claudeMdPathOverride ?? ClaudeConfigDirectory.ClaudeMdPath;
        return BuildNode(rootPath, rawImportText: null, ancestorPaths: Array.Empty<string>(), hop: 0);
    }

    private static ContextFileNodeViewModel BuildNode(string resolvedPath, string? rawImportText, IReadOnlyList<string> ancestorPaths, int hop)
    {
        if (!File.Exists(resolvedPath))
        {
            return new ContextFileNodeViewModel(resolvedPath, rawImportText, ContextFileState.Missing);
        }

        if (ancestorPaths.Any(ancestor => string.Equals(ancestor, resolvedPath, StringComparison.OrdinalIgnoreCase)))
        {
            return new ContextFileNodeViewModel(resolvedPath, rawImportText, ContextFileState.Cycle);
        }

        var node = new ContextFileNodeViewModel(resolvedPath, rawImportText, ContextFileState.Resolved);

        if (hop < MaxHop)
        {
            var containingDir = Path.GetDirectoryName(resolvedPath) ?? string.Empty;
            var childAncestors = ancestorPaths.Append(resolvedPath).ToList();

            foreach (var token in ReadImportTokens(resolvedPath))
            {
                var child = BuildChild(containingDir, token, childAncestors, hop + 1);
                child.Parent = node;
                node.Children.Add(child);
            }
        }

        return node;
    }

    private static ContextFileNodeViewModel BuildChild(string containingDir, string token, IReadOnlyList<string> childAncestors, int childHop)
    {
        try
        {
            var childPath = Path.IsPathRooted(token)
                ? Path.GetFullPath(token)
                : Path.GetFullPath(Path.Combine(containingDir, token));

            return BuildNode(childPath, token, childAncestors, childHop);
        }
        catch (ArgumentException)
        {
            // A token containing characters that make it an unresolvable path (rare - most content
            // just won't match a real file) - render it as not-found rather than aborting the
            // whole tree build over one bad @import.
            return new ContextFileNodeViewModel(token, token, ContextFileState.Missing);
        }
    }

    private static IReadOnlyList<string> ReadImportTokens(string resolvedPath)
    {
        try
        {
            return ContextImportParser.FindImports(File.ReadAllText(resolvedPath));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The file exists (we already checked) but couldn't be read right now - treat as "no
            // imports found" rather than propagating; the node itself still shows as Resolved since
            // the file does exist.
            return Array.Empty<string>();
        }
    }
}
