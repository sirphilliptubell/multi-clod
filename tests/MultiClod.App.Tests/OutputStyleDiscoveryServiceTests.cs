using MultiClod.App.OutputStyles;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace MultiClod.App.Tests;

public sealed class OutputStyleDiscoveryServiceTests
{
    [Test]
    public async Task ScanPersonalOutputStyles_RootDoesNotExist_ReturnsEmpty()
    {
        var scratchDir = CreateScratchDirectory();
        try
        {
            var missingRoot = Path.Combine(scratchDir, "does-not-exist");

            var results = new OutputStyleDiscoveryService(missingRoot).ScanPersonalOutputStyles();

            await Assert.That(results).IsEmpty();
        }
        finally
        {
            DeleteScratchDirectory(scratchDir);
        }
    }

    [Test]
    public async Task ScanPersonalOutputStyles_ParsesFrontmatter_SortedByName()
    {
        var scratchDir = CreateScratchDirectory();
        try
        {
            WriteOutputStyle(scratchDir, "zzz-style.md", "---\nname: Zebra\ndescription: Last alphabetically\n---\n# Body");
            WriteOutputStyle(scratchDir, "aaa-style.md", "---\nname: Alpha\ndescription: First alphabetically\n---\n# Body");

            var results = new OutputStyleDiscoveryService(scratchDir).ScanPersonalOutputStyles();

            await Assert.That(results).Count().IsEqualTo(2);
            await Assert.That(results[0].Name).IsEqualTo("Alpha");
            await Assert.That(results[0].Description).IsEqualTo("First alphabetically");
            await Assert.That(results[1].Name).IsEqualTo("Zebra");
        }
        finally
        {
            DeleteScratchDirectory(scratchDir);
        }
    }

    [Test]
    public async Task ScanPersonalOutputStyles_NoFrontmatter_FallsBackToFileName()
    {
        var scratchDir = CreateScratchDirectory();
        try
        {
            WriteOutputStyle(scratchDir, "my-style.md", "# Just a heading, no frontmatter");

            var results = new OutputStyleDiscoveryService(scratchDir).ScanPersonalOutputStyles();

            await Assert.That(results).Count().IsEqualTo(1);
            await Assert.That(results[0].Name).IsEqualTo("my-style");
            await Assert.That(results[0].Description).IsNull();
        }
        finally
        {
            DeleteScratchDirectory(scratchDir);
        }
    }

    [Test]
    public async Task ScanPersonalOutputStyles_NonMdFile_IsSkipped()
    {
        var scratchDir = CreateScratchDirectory();
        try
        {
            File.WriteAllText(Path.Combine(scratchDir, "not-a-style.txt"), "irrelevant");
            WriteOutputStyle(scratchDir, "real-style.md", "---\nname: Real\n---\n");

            var results = new OutputStyleDiscoveryService(scratchDir).ScanPersonalOutputStyles();

            await Assert.That(results).Count().IsEqualTo(1);
            await Assert.That(results[0].Name).IsEqualTo("Real");
        }
        finally
        {
            DeleteScratchDirectory(scratchDir);
        }
    }

    private static void WriteOutputStyle(string root, string fileName, string content)
    {
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, fileName), content);
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
