using MultiClod.App.Skills;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace MultiClod.App.Tests;

public sealed class SkillFrontmatterYamlTests
{
    [Test]
    public async Task TryParse_NoFrontmatter_ReturnsFalseAndWholeTextAsBody()
    {
        var ok = SkillFrontmatterYaml.TryParse("# Just a heading\nNo frontmatter here.", out var frontmatter, out var rawBlock, out var body);

        await Assert.That(ok).IsFalse();
        await Assert.That(frontmatter).IsNull();
        await Assert.That(rawBlock).IsNull();
        await Assert.That(body).IsEqualTo("# Just a heading\nNo frontmatter here.");
    }

    [Test]
    public async Task TryParse_EmptyBlock_ReturnsEmptyMapping()
    {
        var ok = SkillFrontmatterYaml.TryParse("---\n---\n# Body", out var frontmatter, out _, out var body);

        await Assert.That(ok).IsTrue();
        await Assert.That(frontmatter!.Name).IsNull();
        await Assert.That(frontmatter.Mapping.Children).IsEmpty();
        await Assert.That(body).IsEqualTo("# Body");
    }

    [Test]
    public async Task TryParse_NonMappingRoot_ReturnsFalseButRawBlockPopulated()
    {
        var ok = SkillFrontmatterYaml.TryParse("---\n- just\n- a\n- list\n---\nBody", out var frontmatter, out var rawBlock, out var body);

        await Assert.That(ok).IsFalse();
        await Assert.That(frontmatter).IsNull();
        await Assert.That(rawBlock).IsEqualTo("- just\n- a\n- list");
        await Assert.That(body).IsEqualTo("Body");
    }

    [Test]
    public async Task TryParse_InvalidYaml_ReturnsFalseButRawBlockPopulated()
    {
        var ok = SkillFrontmatterYaml.TryParse("---\nname: [unterminated\n---\nBody", out var frontmatter, out var rawBlock, out _);

        await Assert.That(ok).IsFalse();
        await Assert.That(frontmatter).IsNull();
        await Assert.That(rawBlock).IsEqualTo("name: [unterminated");
    }

    [Test]
    public async Task TryParse_UnterminatedFrontmatter_TreatsRestAsBlockWithEmptyBody()
    {
        var ok = SkillFrontmatterYaml.TryParse("---\nname: Orphan\n# never closed", out var frontmatter, out _, out var body);

        await Assert.That(ok).IsTrue();
        await Assert.That(frontmatter!.Name).IsEqualTo("Orphan");
        await Assert.That(body).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task TryParse_NameAndDescription_ParsesBoth()
    {
        var ok = SkillFrontmatterYaml.TryParse("---\nname: My Skill\ndescription: Does a thing\n---\n# Body\n", out var frontmatter, out _, out _);

        await Assert.That(ok).IsTrue();
        await Assert.That(frontmatter!.Name).IsEqualTo("My Skill");
        await Assert.That(frontmatter.Description).IsEqualTo("Does a thing");
    }

    [Test]
    public async Task TryParse_DuplicateKeys_TreatedAsParseFailure()
    {
        // YamlDotNet's YamlStream.Load throws on a duplicate mapping key (caught as a YamlException)
        // rather than silently keeping the last value - this editor treats that the same as any
        // other invalid YAML: a parse failure with the raw block preserved for the caller to show.
        var ok = SkillFrontmatterYaml.TryParse("---\nname: First\nname: Second\n---\n", out var frontmatter, out var rawBlock, out _);

        await Assert.That(ok).IsFalse();
        await Assert.That(frontmatter).IsNull();
        await Assert.That(rawBlock).IsEqualTo("name: First\nname: Second");
    }

    [Test]
    public async Task TryParse_ListValuedAllowedTools_ParsesAsList()
    {
        var ok = SkillFrontmatterYaml.TryParse("---\nallowed-tools:\n  - Read\n  - Grep\n---\n", out var frontmatter, out _, out _);

        await Assert.That(ok).IsTrue();
        await Assert.That(frontmatter!.AllowedTools).IsEquivalentTo(["Read", "Grep"]);
    }

    [Test]
    public async Task Serialize_EditingExistingField_PreservesOriginalKeyOrder()
    {
        SkillFrontmatterYaml.TryParse("---\nname: Original\ndescription: First\nmodel: sonnet\n---\nBody", out var frontmatter, out _, out var body);

        frontmatter!.SetDescription("Changed");
        var result = SkillFrontmatterYaml.Serialize(frontmatter, body);

        var frontmatterBlock = result[..result.LastIndexOf("---", StringComparison.Ordinal)];
        var nameIndex = frontmatterBlock.IndexOf("name:", StringComparison.Ordinal);
        var descriptionIndex = frontmatterBlock.IndexOf("description:", StringComparison.Ordinal);
        var modelIndex = frontmatterBlock.IndexOf("model:", StringComparison.Ordinal);

        await Assert.That(nameIndex).IsLessThan(descriptionIndex);
        await Assert.That(descriptionIndex).IsLessThan(modelIndex);
        await Assert.That(frontmatterBlock).Contains("Changed");
    }

    [Test]
    public async Task Serialize_NewlyAddedKey_AppendedAtEnd()
    {
        SkillFrontmatterYaml.TryParse("---\nname: Original\n---\nBody", out var frontmatter, out _, out var body);

        frontmatter!.SetDescription("Newly added");
        var result = SkillFrontmatterYaml.Serialize(frontmatter, body);

        var nameIndex = result.IndexOf("name:", StringComparison.Ordinal);
        var descriptionIndex = result.IndexOf("description:", StringComparison.Ordinal);

        await Assert.That(nameIndex).IsLessThan(descriptionIndex);
    }

    [Test]
    public async Task Serialize_AllowedTools_FourOrFewer_WritesFlatString()
    {
        var frontmatter = SkillFrontmatter.CreateEmpty();
        frontmatter.SetAllowedTools(["Read", "Grep", "Edit", "Write"]);

        var result = SkillFrontmatterYaml.Serialize(frontmatter, string.Empty);

        await Assert.That(result).Contains("allowed-tools: Read Grep Edit Write");
    }

    [Test]
    public async Task Serialize_AllowedTools_FiveOrMore_WritesYamlList()
    {
        var frontmatter = SkillFrontmatter.CreateEmpty();
        frontmatter.SetAllowedTools(["Read", "Grep", "Edit", "Write", "Bash"]);

        var result = SkillFrontmatterYaml.Serialize(frontmatter, string.Empty);

        await Assert.That(result).Contains("allowed-tools:\n");
        await Assert.That(result).Contains("- Read");
        await Assert.That(result).Contains("- Bash");
    }

    [Test]
    public async Task Serialize_RoundTrip_UnrecognizedKeysSurvive()
    {
        SkillFrontmatterYaml.TryParse("---\nname: Original\ncustom-field: keep-me\n---\nBody", out var frontmatter, out _, out var body);

        frontmatter!.SetDescription("Added");
        var result = SkillFrontmatterYaml.Serialize(frontmatter, body);

        await Assert.That(result).Contains("custom-field: keep-me");
    }
}
