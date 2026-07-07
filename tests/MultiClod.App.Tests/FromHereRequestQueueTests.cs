using MultiClod.App.FromHere;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace MultiClod.App.Tests;

public sealed class FromHereRequestQueueTests
{
    [Test]
    public async Task Post_BeforeAttach_BuffersAndDrainsInOrderOnAttach()
    {
        var queue = new FromHereRequestQueue();
        queue.Post(@"C:\first");
        queue.Post(@"C:\second");

        var received = new List<string?>();
        queue.Attach(received.Add);

        await Assert.That(received).Count().IsEqualTo(2);
        await Assert.That(received[0]).IsEqualTo(@"C:\first");
        await Assert.That(received[1]).IsEqualTo(@"C:\second");
    }

    [Test]
    public async Task Post_AfterAttach_RoutesImmediately()
    {
        var queue = new FromHereRequestQueue();
        var received = new List<string?>();
        queue.Attach(received.Add);

        queue.Post(@"C:\later");

        await Assert.That(received).Count().IsEqualTo(1);
        await Assert.That(received[0]).IsEqualTo(@"C:\later");
    }

    [Test]
    public async Task Post_NullDirectory_PassesThrough()
    {
        var queue = new FromHereRequestQueue();
        var received = new List<string?>();
        queue.Attach(received.Add);

        queue.Post(null);

        await Assert.That(received).Count().IsEqualTo(1);
        await Assert.That(received[0]).IsNull();
    }

    [Test]
    public async Task Post_AfterDetach_RebuffersUntilReattached()
    {
        var queue = new FromHereRequestQueue();
        var received = new List<string?>();
        queue.Attach(received.Add);

        queue.Detach();
        queue.Post(@"C:\while-detached");

        await Assert.That(received).IsEmpty();

        var receivedAfterReattach = new List<string?>();
        queue.Attach(receivedAfterReattach.Add);

        await Assert.That(receivedAfterReattach).Count().IsEqualTo(1);
        await Assert.That(receivedAfterReattach[0]).IsEqualTo(@"C:\while-detached");
    }
}
