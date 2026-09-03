using MultiClod.App.OutputStyles.OutputStyleEditor;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace MultiClod.App.Tests;

public sealed class OutputStyleFileNameValidatorTests
{
    [Test]
    public async Task Sanitize_StripsWindowsIllegalCharacters()
    {
        var result = OutputStyleFileNameValidator.Sanitize("a<b>c:d\"e/f\\g|h?i*j");

        await Assert.That(result).IsEqualTo("abcdefghij");
    }

    // Unlike SkillFolderNameValidator (kebab-case only, since a skill folder name becomes a
    // /skill-name command), an output style file name has no slash-command constraint - spaces and
    // mixed case survive untouched, matching the doc's own "Diagrams first" example style name.
    [Test]
    public async Task Sanitize_PreservesSpacesAndMixedCase()
    {
        var result = OutputStyleFileNameValidator.Sanitize("Diagrams first");

        await Assert.That(result).IsEqualTo("Diagrams first");
    }

    [Test]
    public async Task Sanitize_TrimsTrailingDotsAndSpaces()
    {
        var result = OutputStyleFileNameValidator.Sanitize("My Style. . ");

        await Assert.That(result).IsEqualTo("My Style");
    }

    [Test]
    public async Task TryValidate_EmptyName_ReturnsFalseWithError()
    {
        var ok = OutputStyleFileNameValidator.TryValidate(string.Empty, "irrelevant-root", out var error);

        await Assert.That(ok).IsFalse();
        await Assert.That(error).IsNotNull();
    }

    [Test]
    public async Task TryValidate_UniqueName_ReturnsTrue()
    {
        var scratchDir = CreateScratchDirectory();
        try
        {
            var ok = OutputStyleFileNameValidator.TryValidate("New Style", scratchDir, out var error);

            await Assert.That(ok).IsTrue();
            await Assert.That(error).IsNull();
        }
        finally
        {
            DeleteScratchDirectory(scratchDir);
        }
    }

    [Test]
    public async Task TryValidate_NameAlreadyUsed_ReturnsFalseWithError()
    {
        var scratchDir = CreateScratchDirectory();
        try
        {
            File.WriteAllText(Path.Combine(scratchDir, "Existing.md"), "---\nname: Existing\n---\n");

            var ok = OutputStyleFileNameValidator.TryValidate("Existing", scratchDir, out var error);

            await Assert.That(ok).IsFalse();
            await Assert.That(error).IsNotNull();
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
