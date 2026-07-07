using MultiClod.App.Updates;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace MultiClod.App.Tests;

public sealed class UpdateFeedLocationTests
{
    [Test]
    public async Task Resolve_OverrideSet_ReturnsOverride()
    {
        var result = UpdateFeedLocation.Resolve(envOverride: @"\\override\share", bakedInValue: @"\\baked\share");

        await Assert.That(result).IsEqualTo(@"\\override\share");
    }

    [Test]
    public async Task Resolve_OnlyBakedInSet_ReturnsBakedIn()
    {
        var result = UpdateFeedLocation.Resolve(envOverride: null, bakedInValue: @"\\baked\share");

        await Assert.That(result).IsEqualTo(@"\\baked\share");
    }

    [Test]
    public async Task Resolve_OverrideEmpty_FallsBackToBakedIn()
    {
        var result = UpdateFeedLocation.Resolve(envOverride: string.Empty, bakedInValue: @"\\baked\share");

        await Assert.That(result).IsEqualTo(@"\\baked\share");
    }

    [Test]
    public async Task Resolve_NeitherSet_ReturnsNull()
    {
        var result = UpdateFeedLocation.Resolve(envOverride: null, bakedInValue: null);

        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task Resolve_BakedInEmpty_ReturnsNull()
    {
        var result = UpdateFeedLocation.Resolve(envOverride: null, bakedInValue: string.Empty);

        await Assert.That(result).IsNull();
    }
}
