using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MultiClod.App.Git;

/// <summary>
/// Read-only git queries against a working folder, used by AddSessionDialog to populate its
/// branch select once "Create a git worktree" is checked. Every method treats "not a git
/// repository" (or git itself being unavailable) as an empty/failed result rather than throwing -
/// the dialog just leaves the branch list empty and lets GitWorktree's own attempt surface the
/// real error if the user tries to proceed anyway.
/// </summary>
internal static class GitRepository
{
    internal static bool TryGetRepoRoot(string folder, out string repoRoot)
    {
        repoRoot = string.Empty;

        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
        {
            return false;
        }

        var (exitCode, stdOut, _) = GitProcess.Run(folder, "rev-parse", "--show-toplevel");
        if (exitCode != 0)
        {
            return false;
        }

        // git always reports the toplevel with forward slashes, even on Windows.
        repoRoot = stdOut.Trim().Replace('/', '\\');
        return true;
    }

    internal static IReadOnlyList<string> GetLocalBranches(string repoRoot)
    {
        var (exitCode, stdOut, _) = GitProcess.Run(repoRoot, "branch", "--format=%(refname:short)");
        return exitCode == 0 ? SplitLines(stdOut) : [];
    }

    /// <summary>
    /// Excludes the "origin/HEAD" symbolic pointer itself - GetDefaultRemoteBranch resolves what it
    /// points at instead, so listing it separately would just offer a confusing duplicate entry
    /// alongside the real branch it aliases. Filtering has to go by the *full* ref name
    /// ("refs/remotes/origin/HEAD"): %(refname:short) collapses that particular ref down to just
    /// "origin" (the remote's own name, not "origin/HEAD"), so a short-name-only filter never
    /// matches it and "origin" leaks into the list looking like a real branch.
    /// </summary>
    internal static IReadOnlyList<string> GetRemoteBranches(string repoRoot)
    {
        var (exitCode, stdOut, _) = GitProcess.Run(repoRoot, "branch", "-r", "--format=%(refname)%09%(refname:short)");
        if (exitCode != 0)
        {
            return [];
        }

        var branches = new List<string>();
        foreach (var line in SplitLines(stdOut))
        {
            var parts = line.Split('\t', 2);
            if (parts.Length == 2 && !parts[0].EndsWith("/HEAD", StringComparison.Ordinal))
            {
                branches.Add(parts[1]);
            }
        }

        return branches;
    }

    /// <summary>
    /// The remote's default branch (e.g. "origin/main"), resolved from the local clone's own
    /// refs/remotes/origin/HEAD symbolic ref - no network access, so this can be stale if the
    /// remote's default branch changed since the repo was cloned/fetched. Null if unset (some
    /// clones never populate it) or there's no "origin" remote at all.
    /// </summary>
    internal static string? GetDefaultRemoteBranch(string repoRoot)
    {
        var (exitCode, stdOut, _) = GitProcess.Run(repoRoot, "symbolic-ref", "--short", "refs/remotes/origin/HEAD");
        return exitCode == 0 ? stdOut.Trim() : null;
    }

    private static IReadOnlyList<string> SplitLines(string output)
    {
        return output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}
