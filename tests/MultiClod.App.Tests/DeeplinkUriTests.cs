using MultiClod.App.Deeplink;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace MultiClod.App.Tests;

public sealed class DeeplinkUriTests
{
    [Test]
    public async Task TryParse_ValidHttpsSource_ExtractsDecodedUrl()
    {
        var raw = "multi-clod://open-session-log?url=" + Uri.EscapeDataString("https://example.com/session.zip");

        var ok = DeeplinkUri.TryParse(raw, out var source);

        await Assert.That(ok).IsTrue();
        await Assert.That(source).IsEqualTo("https://example.com/session.zip");
    }

    [Test]
    public async Task TryParse_ValidLocalPathSource_ExtractsDecodedPath()
    {
        var raw = "multi-clod://open-session-log?url=" + Uri.EscapeDataString(@"C:\temp\session.zip");

        var ok = DeeplinkUri.TryParse(raw, out var source);

        await Assert.That(ok).IsTrue();
        await Assert.That(source).IsEqualTo(@"C:\temp\session.zip");
    }

    [Test]
    public async Task TryParse_WrongScheme_ReturnsFalse()
    {
        var ok = DeeplinkUri.TryParse("https://open-session-log?url=x", out _);

        await Assert.That(ok).IsFalse();
    }

    [Test]
    public async Task TryParse_WrongHost_ReturnsFalse()
    {
        var ok = DeeplinkUri.TryParse("multi-clod://do-something-else?url=x", out _);

        await Assert.That(ok).IsFalse();
    }

    [Test]
    public async Task TryParse_MissingUrlParam_ReturnsFalse()
    {
        var ok = DeeplinkUri.TryParse("multi-clod://open-session-log?foo=bar", out _);

        await Assert.That(ok).IsFalse();
    }

    [Test]
    public async Task TryParse_EmptyUrlParam_ReturnsFalse()
    {
        var ok = DeeplinkUri.TryParse("multi-clod://open-session-log?url=", out _);

        await Assert.That(ok).IsFalse();
    }

    [Test]
    public async Task TryParse_NotAUri_ReturnsFalse()
    {
        var ok = DeeplinkUri.TryParse("not a uri at all", out _);

        await Assert.That(ok).IsFalse();
    }
}
