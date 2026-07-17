using System.Text.Json;
using MultiClod.App.Costs;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace MultiClod.App.Tests;

public sealed class ClaudeUsageReaderTests
{
    [Test]
    public async Task TryRead_LineWithCacheCreationBreakdown_UsesBreakdown()
    {
        const string json = """
            {"type":"assistant","timestamp":"2026-07-16T20:53:27.143Z",
             "message":{"model":"claude-sonnet-5",
               "usage":{"input_tokens":8940,"cache_creation_input_tokens":38545,"cache_read_input_tokens":10,
                 "output_tokens":387,
                 "cache_creation":{"ephemeral_1h_input_tokens":30000,"ephemeral_5m_input_tokens":8545}}}}
            """;

        var line = ClaudeUsageReader.TryRead(Parse(json));

        await Assert.That(line).IsNotNull();
        await Assert.That(line!.ModelSlug).IsEqualTo("claude-sonnet-5");
        await Assert.That(line.Usage.InputTokens).IsEqualTo(8940L);
        await Assert.That(line.Usage.OutputTokens).IsEqualTo(387L);
        await Assert.That(line.Usage.CacheReadInputTokens).IsEqualTo(10L);
        await Assert.That(line.Usage.CacheCreation1hInputTokens).IsEqualTo(30000L);
        await Assert.That(line.Usage.CacheCreation5mInputTokens).IsEqualTo(8545L);
        await Assert.That(line.Timestamp).IsEqualTo(DateTimeOffset.Parse("2026-07-16T20:53:27.143Z"));
    }

    [Test]
    public async Task TryRead_LineWithoutCacheCreationBreakdown_TreatsAllAsFiveMinuteWrite()
    {
        const string json = """
            {"type":"assistant","timestamp":"2026-07-16T20:53:27.143Z",
             "message":{"model":"claude-sonnet-5",
               "usage":{"input_tokens":2,"cache_creation_input_tokens":18701,"cache_read_input_tokens":29286,"output_tokens":158}}}
            """;

        var line = ClaudeUsageReader.TryRead(Parse(json));

        await Assert.That(line).IsNotNull();
        await Assert.That(line!.Usage.CacheCreation5mInputTokens).IsEqualTo(18701L);
        await Assert.That(line.Usage.CacheCreation1hInputTokens).IsEqualTo(0L);
    }

    [Test]
    public async Task TryRead_UserLineWithNoUsage_ReturnsNull()
    {
        const string json = """{"type":"user","message":{"content":"hello"}}""";

        var line = ClaudeUsageReader.TryRead(Parse(json));

        await Assert.That(line).IsNull();
    }

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement.Clone();
}
