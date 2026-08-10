using MultiClod.App.SessionScope;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace MultiClod.App.Tests;

public sealed class SessionPanelAvailabilityTests
{
    [Test]
    public async Task HasAnyMemoryFile_DirectoryDoesNotExist_ReturnsFalse()
    {
        var scratchDir = CreateScratchDirectory();
        try
        {
            var result = SessionPanelAvailability.HasAnyMemoryFile(Path.Combine(scratchDir, "memory"));

            await Assert.That(result).IsFalse();
        }
        finally
        {
            DeleteScratchDirectory(scratchDir);
        }
    }

    [Test]
    public async Task HasAnyMemoryFile_DirectoryExistsButEmpty_ReturnsFalse()
    {
        var scratchDir = CreateScratchDirectory();
        try
        {
            var result = SessionPanelAvailability.HasAnyMemoryFile(scratchDir);

            await Assert.That(result).IsFalse();
        }
        finally
        {
            DeleteScratchDirectory(scratchDir);
        }
    }

    [Test]
    public async Task HasAnyMemoryFile_ContainsMdFile_ReturnsTrue()
    {
        var scratchDir = CreateScratchDirectory();
        try
        {
            File.WriteAllText(Path.Combine(scratchDir, "MEMORY.md"), "# Memory");

            var result = SessionPanelAvailability.HasAnyMemoryFile(scratchDir);

            await Assert.That(result).IsTrue();
        }
        finally
        {
            DeleteScratchDirectory(scratchDir);
        }
    }

    [Test]
    public async Task HasContextOrSkills_NeitherClaudeMdNorSkills_ReturnsFalse()
    {
        var scratchDir = CreateScratchDirectory();
        try
        {
            var result = SessionPanelAvailability.HasContextOrSkills(scratchDir);

            await Assert.That(result).IsFalse();
        }
        finally
        {
            DeleteScratchDirectory(scratchDir);
        }
    }

    [Test]
    public async Task HasContextOrSkills_HasClaudeMd_ReturnsTrue()
    {
        var scratchDir = CreateScratchDirectory();
        try
        {
            File.WriteAllText(Path.Combine(scratchDir, "CLAUDE.md"), "# Repo instructions");

            var result = SessionPanelAvailability.HasContextOrSkills(scratchDir);

            await Assert.That(result).IsTrue();
        }
        finally
        {
            DeleteScratchDirectory(scratchDir);
        }
    }

    [Test]
    public async Task HasContextOrSkills_HasSkillWithoutClaudeMd_ReturnsTrue()
    {
        var scratchDir = CreateScratchDirectory();
        try
        {
            var skillDir = Path.Combine(scratchDir, ".claude", "skills", "my-skill");
            Directory.CreateDirectory(skillDir);
            File.WriteAllText(Path.Combine(skillDir, "SKILL.md"), "---\nname: My Skill\n---\n");

            var result = SessionPanelAvailability.HasContextOrSkills(scratchDir);

            await Assert.That(result).IsTrue();
        }
        finally
        {
            DeleteScratchDirectory(scratchDir);
        }
    }

    [Test]
    public async Task HasContextOrSkills_SkillFolderWithoutSkillMd_ReturnsFalse()
    {
        var scratchDir = CreateScratchDirectory();
        try
        {
            Directory.CreateDirectory(Path.Combine(scratchDir, ".claude", "skills", "not-a-skill"));

            var result = SessionPanelAvailability.HasContextOrSkills(scratchDir);

            await Assert.That(result).IsFalse();
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
