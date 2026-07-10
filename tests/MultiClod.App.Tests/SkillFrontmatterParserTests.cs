using MultiClod.App.Skills;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace MultiClod.App.Tests;

public sealed class SkillFrontmatterParserTests
{
    [Test]
    public async Task Parse_NameAndDescriptionPresent_ReturnsBoth()
    {
        var (name, description) = SkillFrontmatterParser.Parse(
            "---\nname: My Skill\ndescription: Does a thing\n---\n# Body\n");

        await Assert.That(name).IsEqualTo("My Skill");
        await Assert.That(description).IsEqualTo("Does a thing");
    }

    [Test]
    public async Task Parse_NoFrontmatter_ReturnsNulls()
    {
        var (name, description) = SkillFrontmatterParser.Parse("# Just a heading\nNo frontmatter here.");

        await Assert.That(name).IsNull();
        await Assert.That(description).IsNull();
    }

    [Test]
    public async Task Parse_UnterminatedFrontmatter_ReturnsWhateverWasFoundBeforeEndOfFile()
    {
        var (name, description) = SkillFrontmatterParser.Parse("---\nname: Orphan\n# never closed");

        await Assert.That(name).IsEqualTo("Orphan");
        await Assert.That(description).IsNull();
    }

    [Test]
    public async Task Parse_QuotedValues_AreUnquoted()
    {
        var (name, description) = SkillFrontmatterParser.Parse(
            "---\nname: \"Quoted Name\"\ndescription: 'Single quoted'\n---\n");

        await Assert.That(name).IsEqualTo("Quoted Name");
        await Assert.That(description).IsEqualTo("Single quoted");
    }

    [Test]
    public async Task Parse_MissingDescription_ReturnsNullDescriptionOnly()
    {
        var (name, description) = SkillFrontmatterParser.Parse("---\nname: Solo\n---\n");

        await Assert.That(name).IsEqualTo("Solo");
        await Assert.That(description).IsNull();
    }

    [Test]
    public async Task Parse_KeyIsCaseInsensitive()
    {
        var (name, description) = SkillFrontmatterParser.Parse("---\nName: Cased\nDESCRIPTION: Also cased\n---\n");

        await Assert.That(name).IsEqualTo("Cased");
        await Assert.That(description).IsEqualTo("Also cased");
    }
}
