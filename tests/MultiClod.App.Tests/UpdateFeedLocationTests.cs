using MultiClod.App.Updates;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace MultiClod.App.Tests;

public sealed class UpdateFeedLocationTests
{
    [Test]
    public async Task Resolve_BakedInSet_ReturnsBakedIn()
    {
        var result = UpdateFeedLocation.Resolve(bakedInValue: "https://github.com/example/repo");

        await Assert.That(result).IsEqualTo("https://github.com/example/repo");
    }

    [Test]
    public async Task Resolve_BakedInNull_ReturnsNull()
    {
        var result = UpdateFeedLocation.Resolve(bakedInValue: null);

        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task Resolve_BakedInEmpty_ReturnsNull()
    {
        var result = UpdateFeedLocation.Resolve(bakedInValue: string.Empty);

        await Assert.That(result).IsNull();
    }
}
