using MultiClod.App.Deeplink;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace MultiClod.App.Tests;

public sealed class DeeplinkSourceKeyTests
{
    [Test]
    public async Task Normalize_HttpsUrl_ReturnsCanonicalUriString()
    {
        var key = DeeplinkSourceKey.Normalize("https://example.com/session.zip");

        await Assert.That(key).IsEqualTo("https://example.com/session.zip");
    }

    [Test]
    public async Task Normalize_LocalPath_IsCaseInsensitive()
    {
        var lower = DeeplinkSourceKey.Normalize(@"c:\temp\session.zip");
        var upper = DeeplinkSourceKey.Normalize(@"C:\TEMP\SESSION.ZIP");

        await Assert.That(lower).IsEqualTo(upper);
    }

    [Test]
    public async Task Normalize_DifferentPaths_ProduceDifferentKeys()
    {
        var first = DeeplinkSourceKey.Normalize(@"C:\temp\a.zip");
        var second = DeeplinkSourceKey.Normalize(@"C:\temp\b.zip");

        await Assert.That(first).IsNotEqualTo(second);
    }

    [Test]
    public async Task Normalize_HttpAndHttpsOfSameHost_ProduceDifferentKeys()
    {
        var http = DeeplinkSourceKey.Normalize("http://example.com/session.zip");
        var https = DeeplinkSourceKey.Normalize("https://example.com/session.zip");

        await Assert.That(http).IsNotEqualTo(https);
    }
}
