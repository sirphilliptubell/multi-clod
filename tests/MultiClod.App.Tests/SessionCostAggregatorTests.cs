using MultiClod.App.Costs;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace MultiClod.App.Tests;

public sealed class SessionCostAggregatorTests
{
    [Test]
    public async Task FormatBadge_NoData_ReturnsNull()
    {
        await Assert.That(SessionCostAggregator.FormatBadge(SessionCostSummary.NoData)).IsNull();
    }

    [Test]
    public async Task FormatBadge_AllKnown_ReturnsPlainDollarAmount()
    {
        var summary = SessionCostSummary.Of(1.234m, hasUnknownContribution: false);

        await Assert.That(SessionCostAggregator.FormatBadge(summary)).IsEqualTo("$1.23");
    }

    [Test]
    public async Task FormatBadge_HasUnknownContribution_PrefixesGreaterThan()
    {
        var summary = SessionCostSummary.Of(2.5m, hasUnknownContribution: true);

        await Assert.That(SessionCostAggregator.FormatBadge(summary)).IsEqualTo(">$2.50");
    }

    [Test]
    public async Task FormatBadge_TinyKnownAmount_FloorsToLessThanOneCent()
    {
        var summary = SessionCostSummary.Of(0.0004m, hasUnknownContribution: false);

        await Assert.That(SessionCostAggregator.FormatBadge(summary)).IsEqualTo("<$0.01");
    }

    [Test]
    public async Task FormatBadge_TinyUnknownAmount_DoesNotDoubleUpFloorAndPrefix()
    {
        // With an unknown contribution, the ">" prefix already signals "at least this much" - a
        // known amount that rounds to zero should show "$0.00", not stack "<$0.01" under the ">".
        var summary = SessionCostSummary.Of(0.0004m, hasUnknownContribution: true);

        await Assert.That(SessionCostAggregator.FormatBadge(summary)).IsEqualTo(">$0.00");
    }

    [Test]
    public async Task FormatBadge_ExactlyZeroKnownAmount_ShowsPlainZero()
    {
        var summary = SessionCostSummary.Of(0m, hasUnknownContribution: false);

        await Assert.That(SessionCostAggregator.FormatBadge(summary)).IsEqualTo("$0.00");
    }

    [Test]
    public async Task Aggregate_SumsKnownCostsAcrossFiles_AndFlagsAnyUnknown()
    {
        var mainLog = new Dictionary<string, decimal?> { ["claude-opus-4-8"] = 1.00m };
        var subagent = new Dictionary<string, decimal?> { ["claude-haiku-4-5"] = 0.50m, ["some-new-model"] = null };

        var summary = SessionCostAggregator.Aggregate([mainLog, subagent]);

        await Assert.That(summary.HasAnyData).IsTrue();
        await Assert.That(summary.KnownTotalUsd).IsEqualTo(1.50m);
        await Assert.That(summary.HasUnknownContribution).IsTrue();
    }

    [Test]
    public async Task Aggregate_NoUnknownEntries_HasUnknownContributionIsFalse()
    {
        var mainLog = new Dictionary<string, decimal?> { ["claude-opus-4-8"] = 1.00m };

        var summary = SessionCostAggregator.Aggregate([mainLog]);

        await Assert.That(summary.HasUnknownContribution).IsFalse();
    }
}
