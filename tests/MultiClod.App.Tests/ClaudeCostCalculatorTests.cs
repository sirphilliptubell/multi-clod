using MultiClod.App.Costs;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace MultiClod.App.Tests;

public sealed class ClaudeCostCalculatorTests
{
    [Test]
    public async Task TryGetRate_UnknownSlug_ReturnsNull()
    {
        var rate = ClaudeModelPricing.TryGetRate("not-a-real-model", DateTimeOffset.UtcNow);

        await Assert.That(rate).IsNull();
    }

    [Test]
    public async Task TryGetRate_KnownSlug_ReturnsRate()
    {
        var rate = ClaudeModelPricing.TryGetRate("claude-opus-4-8", DateTimeOffset.UtcNow);

        await Assert.That(rate).IsNotNull();
        await Assert.That(rate!.InputPerMillionUsd).IsEqualTo(5m);
        await Assert.That(rate.OutputPerMillionUsd).IsEqualTo(25m);
    }

    [Test]
    public async Task TryGetRate_SonnetFive_BeforeBoundary_ReturnsIntroPricing()
    {
        var justBefore = new DateTimeOffset(2026, 8, 31, 23, 59, 59, TimeSpan.Zero);

        var rate = ClaudeModelPricing.TryGetRate("claude-sonnet-5", justBefore);

        await Assert.That(rate).IsNotNull();
        await Assert.That(rate!.InputPerMillionUsd).IsEqualTo(2m);
        await Assert.That(rate.OutputPerMillionUsd).IsEqualTo(10m);
    }

    [Test]
    public async Task TryGetRate_SonnetFive_AtBoundary_ReturnsStandardPricing()
    {
        var atBoundary = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);

        var rate = ClaudeModelPricing.TryGetRate("claude-sonnet-5", atBoundary);

        await Assert.That(rate).IsNotNull();
        await Assert.That(rate!.InputPerMillionUsd).IsEqualTo(3m);
        await Assert.That(rate.OutputPerMillionUsd).IsEqualTo(15m);
    }

    [Test]
    public async Task HasAnyRateFor_KnownSlug_ReturnsTrue()
    {
        await Assert.That(ClaudeModelPricing.HasAnyRateFor("claude-haiku-4-5")).IsTrue();
    }

    [Test]
    public async Task HasAnyRateFor_UnknownSlug_ReturnsFalse()
    {
        await Assert.That(ClaudeModelPricing.HasAnyRateFor("not-a-real-model")).IsFalse();
    }

    [Test]
    public async Task TryComputeUsd_KnownModel_SumsAllFiveCategories()
    {
        // claude-opus-4-8: input $5, 5m-write $6.25, 1h-write $10, cache-read $0.50, output $25 (all per million tokens)
        var usage = new ClaudeUsage(
            InputTokens: 1_000_000,
            OutputTokens: 1_000_000,
            CacheReadInputTokens: 1_000_000,
            CacheCreation5mInputTokens: 1_000_000,
            CacheCreation1hInputTokens: 1_000_000);

        var cost = ClaudeCostCalculator.TryComputeUsd("claude-opus-4-8", usage, DateTimeOffset.UtcNow);

        await Assert.That(cost).IsEqualTo(5m + 25m + 0.5m + 6.25m + 10m);
    }

    [Test]
    public async Task TryComputeUsd_UnknownModel_ReturnsNull()
    {
        var usage = new ClaudeUsage(100, 100, 0, 0, 0);

        var cost = ClaudeCostCalculator.TryComputeUsd("not-a-real-model", usage, DateTimeOffset.UtcNow);

        await Assert.That(cost).IsNull();
    }

    [Test]
    public async Task TryComputeUsd_NoTimestamp_ReturnsNull()
    {
        var usage = new ClaudeUsage(100, 100, 0, 0, 0);

        var cost = ClaudeCostCalculator.TryComputeUsd("claude-opus-4-8", usage, timestamp: null);

        await Assert.That(cost).IsNull();
    }
}
