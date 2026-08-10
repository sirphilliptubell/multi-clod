using MultiClod.App.Context;
using MultiClod.App.SessionScope;
using MultiClod.App.Validation;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace MultiClod.App.Tests;

public sealed class SessionScopedPathsTests
{
    [Test]
    public async Task GetMemoryDirectory_MatchesClaudeProjectPathEncodingUnderClaudeConfigRoot()
    {
        var workingDirectory = Path.Combine(Path.GetTempPath(), "MultiClod.App.Tests", Guid.NewGuid().ToString());
        var expected = Path.Combine(ClaudeConfigDirectory.Root, "projects", ClaudeProjectPath.Encode(workingDirectory), "memory");

        var actual = SessionScopedPaths.GetMemoryDirectory(workingDirectory);

        await Assert.That(actual).IsEqualTo(expected);
    }

    [Test]
    public async Task TryGetRepoRoot_NotARepo_ReturnsFalse()
    {
        var scratchDir = CreateScratchDirectory();
        try
        {
            var result = SessionScopedPaths.TryGetRepoRoot(scratchDir, out var repoRoot);

            await Assert.That(result).IsFalse();
            await Assert.That(repoRoot).IsEqualTo(string.Empty);
        }
        finally
        {
            DeleteScratchDirectory(scratchDir);
        }
    }

    private static string CreateScratchDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "MultiClod.App.Tests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteScratchDirectory(string path)
    {
        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
