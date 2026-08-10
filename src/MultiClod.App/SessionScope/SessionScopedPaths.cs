using System.IO;
using MultiClod.App.Context;
using MultiClod.App.Git;
using MultiClod.App.Validation;

namespace MultiClod.App.SessionScope;

/// <summary>
/// Resolves the two roots the session-scoped sub-panel needs from a session's working directory:
/// its Claude memory folder (keyed by cwd, mirroring ClaudeProjectPath's own CLI-compatible
/// encoding) and its git repo root (worktree-aware, so a session running inside a linked worktree
/// still resolves to that worktree's own root rather than the main repo's).
/// </summary>
internal static class SessionScopedPaths
{
    public static string GetMemoryDirectory(string workingDirectory) =>
        Path.Combine(ClaudeConfigDirectory.Root, "projects", ClaudeProjectPath.Encode(workingDirectory), "memory");

    public static bool TryGetRepoRoot(string workingDirectory, out string repoRoot) =>
        GitRepository.TryGetRepoRoot(workingDirectory, out repoRoot);
}
