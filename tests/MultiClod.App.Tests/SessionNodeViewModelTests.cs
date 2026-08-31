using MultiClod.Terminal.Abstractions;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace MultiClod.App.Tests;

public sealed class SessionNodeViewModelTests
{
    [Test]
    public async Task Activity_DormantNode_FallsBackToPersistedLastActivity()
    {
        // Mirrors what a stopped/never-relaunched node looks like right after SessionTreeController
        // rebuilds it from a saved SessionRecord.LastActivity - no TerminalSession attached yet.
        var node = new SessionNodeViewModel(
            Guid.NewGuid(), Guid.NewGuid(), "test", Path.GetTempPath(), hasBeenStarted: true,
            lastActivity: SessionActivity.NeedsInput);

        await Assert.That(node.Activity).IsEqualTo(SessionActivity.NeedsInput);
        await Assert.That(node.LastActivity).IsEqualTo(SessionActivity.NeedsInput);
    }

    [Test]
    public async Task AttachLiveSession_SettledActivity_MirrorsIntoLastActivity()
    {
        var node = new SessionNodeViewModel(Guid.NewGuid(), Guid.NewGuid(), "test", Path.GetTempPath(), hasBeenStarted: true);
        var session = new TerminalSession(Path.GetTempPath(), new FakeSessionHost());
        node.AttachLiveSession(session);

        session.ApplyTitle("MULTICLOD:|Working");

        // Working is transient (a live turn actually in flight) - never worth persisting, see
        // SessionRecord.LastActivity's remarks.
        await Assert.That(node.Activity).IsEqualTo(SessionActivity.Working);
        await Assert.That(node.LastActivity).IsEqualTo(SessionActivity.Idle);

        session.ApplyTitle("MULTICLOD:|Stop");

        await Assert.That(node.Activity).IsEqualTo(SessionActivity.Done);
        await Assert.That(node.LastActivity).IsEqualTo(SessionActivity.Done);
    }

    [Test]
    public async Task DetachLiveSession_FallsBackToLastMirroredActivity()
    {
        // What StopSession leaves behind - the node stays showing whatever glyph the session was
        // last settled into, rather than reverting to a blank Idle the moment it's stopped.
        var node = new SessionNodeViewModel(Guid.NewGuid(), Guid.NewGuid(), "test", Path.GetTempPath(), hasBeenStarted: true);
        var session = new TerminalSession(Path.GetTempPath(), new FakeSessionHost());
        node.AttachLiveSession(session);

        session.ApplyTitle("MULTICLOD:|Working");
        session.ApplyTitle("MULTICLOD:|Stop");
        node.DetachLiveSession();

        await Assert.That(node.Activity).IsEqualTo(SessionActivity.Done);
    }

    // Minimal ISessionHost stub - same shape as TerminalSessionTests' own, kept file-local since
    // it's only a few lines and neither test file needs the other's helpers.
    private sealed class FakeSessionHost : ISessionHost
    {
        public ITerminalPane Pane => throw new NotSupportedException();

        public SessionState State => SessionState.NotStarted;

        public int? LastExitCode => null;

        public string LastOutputTail => string.Empty;

#pragma warning disable CS0067 // never raised - TerminalSession only needs to subscribe successfully
        public event EventHandler<SessionState>? StateChanged;

        public event EventHandler<string>? TitleChanged;

        public event EventHandler? InterruptDetected;

        public event EventHandler? ApiErrorDetected;
#pragma warning restore CS0067

        public void Start(TerminalLaunchOptions options)
        {
        }

        public void Stop()
        {
        }

        public void Dispose()
        {
        }
    }
}
