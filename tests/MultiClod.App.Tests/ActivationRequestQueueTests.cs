using MultiClod.App.Activation;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace MultiClod.App.Tests;

public sealed class ActivationRequestQueueTests
{
    [Test]
    public async Task Post_BeforeAttach_BuffersAndDrainsInOrderOnAttach()
    {
        var queue = new ActivationRequestQueue();
        queue.Post(ActivationRequest.FromHere(@"C:\first"));
        queue.Post(ActivationRequest.FromHere(@"C:\second"));

        var received = new List<ActivationRequest?>();
        queue.Attach(received.Add);

        await Assert.That(received).Count().IsEqualTo(2);
        await Assert.That(received[0]).IsEqualTo(ActivationRequest.FromHere(@"C:\first"));
        await Assert.That(received[1]).IsEqualTo(ActivationRequest.FromHere(@"C:\second"));
    }

    [Test]
    public async Task Post_AfterAttach_RoutesImmediately()
    {
        var queue = new ActivationRequestQueue();
        var received = new List<ActivationRequest?>();
        queue.Attach(received.Add);

        queue.Post(ActivationRequest.FromHere(@"C:\later"));

        await Assert.That(received).Count().IsEqualTo(1);
        await Assert.That(received[0]).IsEqualTo(ActivationRequest.FromHere(@"C:\later"));
    }

    [Test]
    public async Task Post_NullRequest_PassesThrough()
    {
        var queue = new ActivationRequestQueue();
        var received = new List<ActivationRequest?>();
        queue.Attach(received.Add);

        queue.Post(null);

        await Assert.That(received).Count().IsEqualTo(1);
        await Assert.That(received[0]).IsNull();
    }

    [Test]
    public async Task Post_AfterDetach_RebuffersUntilReattached()
    {
        var queue = new ActivationRequestQueue();
        var received = new List<ActivationRequest?>();
        queue.Attach(received.Add);

        queue.Detach();
        queue.Post(ActivationRequest.FromHere(@"C:\while-detached"));

        await Assert.That(received).IsEmpty();

        var receivedAfterReattach = new List<ActivationRequest?>();
        queue.Attach(receivedAfterReattach.Add);

        await Assert.That(receivedAfterReattach).Count().IsEqualTo(1);
        await Assert.That(receivedAfterReattach[0]).IsEqualTo(ActivationRequest.FromHere(@"C:\while-detached"));
    }

    [Test]
    public async Task Post_DeeplinkRequest_PassesThrough()
    {
        var queue = new ActivationRequestQueue();
        var received = new List<ActivationRequest?>();
        queue.Attach(received.Add);

        queue.Post(ActivationRequest.Deeplink("https://example.com/session.zip"));

        await Assert.That(received).Count().IsEqualTo(1);
        await Assert.That(received[0]).IsEqualTo(ActivationRequest.Deeplink("https://example.com/session.zip"));
    }
}
