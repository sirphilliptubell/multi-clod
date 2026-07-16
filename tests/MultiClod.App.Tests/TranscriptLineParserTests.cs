using MultiClod.App.SessionLog.Parsing;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace MultiClod.App.Tests;

public sealed class TranscriptLineParserTests
{
    [Test]
    public async Task Parse_ValidJsonWithType_ReturnsTypeValueAndValidRoot()
    {
        var parsed = TranscriptLineParser.Parse("{\"type\":\"user\",\"uuid\":\"abc\"}");

        await Assert.That(parsed.IsValidJson).IsTrue();
        await Assert.That(parsed.TypeValue).IsEqualTo("user");
        await Assert.That(parsed.Root.GetProperty("uuid").GetString()).IsEqualTo("abc");
    }

    [Test]
    public async Task Parse_ValidJsonWithoutType_ReturnsNullTypeValue()
    {
        var parsed = TranscriptLineParser.Parse("{\"foo\":\"bar\"}");

        await Assert.That(parsed.IsValidJson).IsTrue();
        await Assert.That(parsed.TypeValue).IsNull();
    }

    [Test]
    public async Task Parse_MalformedJson_ReturnsInvalid()
    {
        var parsed = TranscriptLineParser.Parse("{not json");

        await Assert.That(parsed.IsValidJson).IsFalse();
        await Assert.That(parsed.TypeValue).IsNull();
        await Assert.That(parsed.RawText).IsEqualTo("{not json");
    }

    [Test]
    public async Task Parse_RootSurvivesAfterGc_ClonedIndependentlyOfSourceDocument()
    {
        var parsed = TranscriptLineParser.Parse("{\"type\":\"assistant\",\"value\":42}");
        GC.Collect();
        GC.WaitForPendingFinalizers();

        await Assert.That(parsed.Root.GetProperty("value").GetInt32()).IsEqualTo(42);
    }
}
