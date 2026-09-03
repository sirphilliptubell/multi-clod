using MultiClod.App.OutputStyles;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace MultiClod.App.Tests;

public sealed class OutputStyleFrontmatterYamlTests
{
    [Test]
    public async Task TryParse_NoFrontmatter_ReturnsFalseAndWholeTextAsBody()
    {
        var ok = OutputStyleFrontmatterYaml.TryParse("Just body text, no frontmatter.", out var frontmatter, out var rawBlock, out var body);

        await Assert.That(ok).IsFalse();
        await Assert.That(frontmatter).IsNull();
        await Assert.That(rawBlock).IsNull();
        await Assert.That(body).IsEqualTo("Just body text, no frontmatter.");
    }

    [Test]
    public async Task TryParse_NameDescriptionKeepCodingInstructions_ParsesAll()
    {
        var ok = OutputStyleFrontmatterYaml.TryParse(
            "---\nname: Diagrams first\ndescription: Lead every explanation with a diagram\nkeep-coding-instructions: true\n---\nBody text",
            out var frontmatter, out _, out var body);

        await Assert.That(ok).IsTrue();
        await Assert.That(frontmatter!.Name).IsEqualTo("Diagrams first");
        await Assert.That(frontmatter.Description).IsEqualTo("Lead every explanation with a diagram");
        await Assert.That(frontmatter.KeepCodingInstructions).IsTrue();
        await Assert.That(body).IsEqualTo("Body text");
    }

    [Test]
    public async Task TryParse_KeepCodingInstructionsOmitted_DefaultsToFalse()
    {
        var ok = OutputStyleFrontmatterYaml.TryParse("---\nname: Concise\n---\nBody", out var frontmatter, out _, out _);

        await Assert.That(ok).IsTrue();
        await Assert.That(frontmatter!.KeepCodingInstructions).IsFalse();
    }

    [Test]
    public async Task Serialize_EditingExistingField_PreservesOriginalKeyOrder()
    {
        OutputStyleFrontmatterYaml.TryParse("---\nname: Original\ndescription: First\n---\nBody", out var frontmatter, out _, out var body);

        frontmatter!.SetDescription("Changed");
        var result = OutputStyleFrontmatterYaml.Serialize(frontmatter, body);

        var frontmatterBlock = result[..result.LastIndexOf("---", StringComparison.Ordinal)];
        var nameIndex = frontmatterBlock.IndexOf("name:", StringComparison.Ordinal);
        var descriptionIndex = frontmatterBlock.IndexOf("description:", StringComparison.Ordinal);

        await Assert.That(nameIndex).IsLessThan(descriptionIndex);
        await Assert.That(frontmatterBlock).Contains("Changed");
    }

    [Test]
    public async Task Serialize_NewlyAddedKey_AppendedAtEnd()
    {
        OutputStyleFrontmatterYaml.TryParse("---\nname: Original\n---\nBody", out var frontmatter, out _, out var body);

        frontmatter!.SetDescription("Newly added");
        var result = OutputStyleFrontmatterYaml.Serialize(frontmatter, body);

        var nameIndex = result.IndexOf("name:", StringComparison.Ordinal);
        var descriptionIndex = result.IndexOf("description:", StringComparison.Ordinal);

        await Assert.That(nameIndex).IsLessThan(descriptionIndex);
    }

    // force-for-plugin has no dedicated accessor on OutputStyleFrontmatter - it's a plugin-authoring
    // field this app never needs to curate (this app only ever edits personal/project output
    // styles, never a plugin's) - but it must still round-trip untouched, same as any unrecognized
    // key, so a Save through the editor never silently drops it.
    [Test]
    public async Task Serialize_RoundTrip_ForceForPluginSurvivesUnchanged()
    {
        OutputStyleFrontmatterYaml.TryParse("---\nname: Original\nforce-for-plugin: true\n---\nBody", out var frontmatter, out _, out var body);

        frontmatter!.SetDescription("Added");
        var result = OutputStyleFrontmatterYaml.Serialize(frontmatter, body);

        await Assert.That(result).Contains("force-for-plugin: true");
    }

    [Test]
    public async Task TryParse_DuplicateKeys_TreatedAsParseFailure()
    {
        var ok = OutputStyleFrontmatterYaml.TryParse("---\nname: First\nname: Second\n---\n", out var frontmatter, out var rawBlock, out _);

        await Assert.That(ok).IsFalse();
        await Assert.That(frontmatter).IsNull();
        await Assert.That(rawBlock).IsEqualTo("name: First\nname: Second");
    }

    [Test]
    public async Task TryParse_NonMappingRoot_ReturnsFalseButRawBlockPopulated()
    {
        var ok = OutputStyleFrontmatterYaml.TryParse("---\n- just\n- a\n- list\n---\nBody", out var frontmatter, out var rawBlock, out var body);

        await Assert.That(ok).IsFalse();
        await Assert.That(frontmatter).IsNull();
        await Assert.That(rawBlock).IsEqualTo("- just\n- a\n- list");
        await Assert.That(body).IsEqualTo("Body");
    }
}
