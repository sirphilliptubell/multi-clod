using MultiClod.App.Context;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace MultiClod.App.Tests;

public sealed class ContextImportParserTests
{
    [Test]
    public async Task FindImports_PlainToken_IsFound()
    {
        var results = ContextImportParser.FindImports("See @README for an overview.");

        await Assert.That(results).Count().IsEqualTo(1);
        await Assert.That(results[0]).IsEqualTo("README");
    }

    [Test]
    public async Task FindImports_MultipleTokens_PreservesDocumentOrder()
    {
        var results = ContextImportParser.FindImports("First @one.md then @two.md and @three.md");

        await Assert.That(results).Count().IsEqualTo(3);
        await Assert.That(results[0]).IsEqualTo("one.md");
        await Assert.That(results[1]).IsEqualTo("two.md");
        await Assert.That(results[2]).IsEqualTo("three.md");
    }

    [Test]
    public async Task FindImports_TrailingPunctuation_IsTrimmed()
    {
        var results = ContextImportParser.FindImports("See (@foo.md).");

        await Assert.That(results).Count().IsEqualTo(1);
        await Assert.That(results[0]).IsEqualTo("foo.md");
    }

    [Test]
    public async Task FindImports_InsideInlineCodeSpan_IsSkipped()
    {
        var results = ContextImportParser.FindImports("Wrap it in backticks: `@README` stays literal.");

        await Assert.That(results).IsEmpty();
    }

    [Test]
    public async Task FindImports_InsideFencedCodeBlock_IsSkipped()
    {
        var text = "Before\n```\n@inside-fence.md\n```\n@after-fence.md";

        var results = ContextImportParser.FindImports(text);

        await Assert.That(results).Count().IsEqualTo(1);
        await Assert.That(results[0]).IsEqualTo("after-fence.md");
    }

    [Test]
    public async Task FindImports_InsideTildeFence_IsSkipped()
    {
        var text = "~~~\n@inside-tilde-fence.md\n~~~\n@after.md";

        var results = ContextImportParser.FindImports(text);

        await Assert.That(results).Count().IsEqualTo(1);
        await Assert.That(results[0]).IsEqualTo("after.md");
    }

    [Test]
    public async Task FindImports_NoTokens_ReturnsEmpty()
    {
        var results = ContextImportParser.FindImports("Nothing to import here.");

        await Assert.That(results).IsEmpty();
    }
}
