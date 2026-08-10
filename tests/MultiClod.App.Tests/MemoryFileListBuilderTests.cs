using MultiClod.App.SessionScope;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace MultiClod.App.Tests;

public sealed class MemoryFileListBuilderTests
{
    [Test]
    public async Task Build_DirectoryDoesNotExist_ReturnsEmpty()
    {
        var scratchDir = CreateScratchDirectory();
        try
        {
            var result = MemoryFileListBuilder.Build(Path.Combine(scratchDir, "memory"));

            await Assert.That(result).IsEmpty();
        }
        finally
        {
            DeleteScratchDirectory(scratchDir);
        }
    }

    [Test]
    public async Task Build_PinsMemoryMdFirstRegardlessOfNameCase_ThenAlphabetical()
    {
        var scratchDir = CreateScratchDirectory();
        try
        {
            File.WriteAllText(Path.Combine(scratchDir, "zzz.md"), string.Empty);
            File.WriteAllText(Path.Combine(scratchDir, "memory.md"), string.Empty);
            File.WriteAllText(Path.Combine(scratchDir, "aaa.md"), string.Empty);

            var result = MemoryFileListBuilder.Build(scratchDir);

            await Assert.That(result).Count().IsEqualTo(3);
            await Assert.That(result[0].Name).IsEqualTo("memory.md");
            await Assert.That(result[1].Name).IsEqualTo("aaa.md");
            await Assert.That(result[2].Name).IsEqualTo("zzz.md");
        }
        finally
        {
            DeleteScratchDirectory(scratchDir);
        }
    }

    [Test]
    public async Task Build_IgnoresNonMarkdownFiles()
    {
        var scratchDir = CreateScratchDirectory();
        try
        {
            File.WriteAllText(Path.Combine(scratchDir, "notes.md"), string.Empty);
            File.WriteAllText(Path.Combine(scratchDir, "data.json"), string.Empty);

            var result = MemoryFileListBuilder.Build(scratchDir);

            await Assert.That(result).Count().IsEqualTo(1);
            await Assert.That(result[0].Name).IsEqualTo("notes.md");
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
