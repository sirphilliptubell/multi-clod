using System;
using System.IO;

namespace MultiClod.App.Git;

/// <summary>
/// Creates a new worktree, and the new branch it's checked out on, for AddSessionDialog's
/// "Create a git worktree" option.
/// </summary>
internal static class GitWorktree
{
    /// <summary>
    /// Creates a new local branch named <paramref name="worktreeName"/> off <paramref name="baseBranch"/>
    /// (e.g. "origin/main"), checked out into a fresh worktree at a sibling folder of the repo -
    /// repoRoot "C:\git\multi-clod" with worktreeName "fix-login" becomes
    /// "C:\git\multi-clod.worktrees\fix-login", keeping every worktree out of the main repo's own
    /// working tree entirely. On failure, <paramref name="worktreePath"/> is empty and
    /// <paramref name="error"/> is git's own message (or ours, if the parent folder couldn't be
    /// created) - shown as-is in AddSessionDialog rather than re-interpreted, since git's own
    /// wording (e.g. "branch already exists") is already the clearest explanation available.
    /// </summary>
    internal static bool TryCreate(string repoRoot, string worktreeName, string baseBranch, out string worktreePath, out string error)
    {
        var trimmedRoot = repoRoot.TrimEnd('\\', '/');
        var worktreesRoot = trimmedRoot + ".worktrees";
        worktreePath = Path.Combine(worktreesRoot, worktreeName);

        try
        {
            Directory.CreateDirectory(worktreesRoot);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            error = $"Could not create '{worktreesRoot}': {ex.Message}";
            worktreePath = string.Empty;
            return false;
        }

        var (exitCode, _, stdErr) = GitProcess.Run(repoRoot, "worktree", "add", "-b", worktreeName, worktreePath, baseBranch);
        if (exitCode != 0)
        {
            error = string.IsNullOrWhiteSpace(stdErr) ? "git worktree add failed." : stdErr.Trim();
            worktreePath = string.Empty;
            return false;
        }

        error = string.Empty;
        return true;
    }
}
