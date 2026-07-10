using MultiClod.App.Skills;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace MultiClod.App.Tests;

public sealed class SkillDiscoveryServiceTests
{
    [Test]
    public async Task ScanPersonalSkills_RootDoesNotExist_ReturnsEmpty()
    {
        var scratchDir = CreateScratchDirectory();
        try
        {
            var missingRoot = Path.Combine(scratchDir, "does-not-exist");

            var results = new SkillDiscoveryService(missingRoot).ScanPersonalSkills();

            await Assert.That(results).IsEmpty();
        }
        finally
        {
            DeleteScratchDirectory(scratchDir);
        }
    }

    [Test]
    public async Task ScanPersonalSkills_ParsesFrontmatter_SortedByName()
    {
        var scratchDir = CreateScratchDirectory();
        try
        {
            WriteSkill(scratchDir, "zzz-skill", "---\nname: Zebra\ndescription: Last alphabetically\n---\n# Body");
            WriteSkill(scratchDir, "aaa-skill", "---\nname: Alpha\ndescription: First alphabetically\n---\n# Body");

            var results = new SkillDiscoveryService(scratchDir).ScanPersonalSkills();

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
    public async Task ScanPersonalSkills_NoFrontmatter_FallsBackToFolderName()
    {
        var scratchDir = CreateScratchDirectory();
        try
        {
            WriteSkill(scratchDir, "my-folder-name", "# Just a heading, no frontmatter");

            var results = new SkillDiscoveryService(scratchDir).ScanPersonalSkills();

            await Assert.That(results).Count().IsEqualTo(1);
            await Assert.That(results[0].Name).IsEqualTo("my-folder-name");
            await Assert.That(results[0].Description).IsNull();
        }
        finally
        {
            DeleteScratchDirectory(scratchDir);
        }
    }

    [Test]
    public async Task ScanPersonalSkills_DirectoryWithoutSkillMd_IsSkipped()
    {
        var scratchDir = CreateScratchDirectory();
        try
        {
            Directory.CreateDirectory(Path.Combine(scratchDir, "not-a-skill"));
            WriteSkill(scratchDir, "real-skill", "---\nname: Real\n---\n");

            var results = new SkillDiscoveryService(scratchDir).ScanPersonalSkills();

            await Assert.That(results).Count().IsEqualTo(1);
            await Assert.That(results[0].Name).IsEqualTo("Real");
        }
        finally
        {
            DeleteScratchDirectory(scratchDir);
        }
    }

    private static void WriteSkill(string root, string folderName, string content)
    {
        var dir = Path.Combine(root, folderName);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "SKILL.md"), content);
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
