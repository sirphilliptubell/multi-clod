using MultiClod.Terminal.Abstractions;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace MultiClod.App.Tests;

public sealed class TerminalSessionTests
{
    [Test]
    public async Task ApplyTitle_SessionIdMarker_SetsObservedClaudeSessionIdOnly()
    {
        var session = new TerminalSession(Path.GetTempPath(), new FakeSessionHost());
        var claudeSessionId = Guid.NewGuid();

        session.ApplyTitle($"MULTICLOD_SESSION:{claudeSessionId}");

        await Assert.That(session.ObservedClaudeSessionId).IsEqualTo(claudeSessionId);
        await Assert.That(session.DetectedTitle).IsNull();
        await Assert.That(session.Activity).IsEqualTo(SessionActivity.Idle);
    }

    [Test]
    public async Task ApplyTitle_ActivityMarker_StillUpdatesActivityNotSessionId()
    {
        var session = new TerminalSession(Path.GetTempPath(), new FakeSessionHost());

        session.ApplyTitle("MULTICLOD_ACTIVITY:Working");

        await Assert.That(session.Activity).IsEqualTo(SessionActivity.Working);
        await Assert.That(session.ObservedClaudeSessionId).IsNull();
        await Assert.That(session.DetectedTitle).IsNull();
    }

    [Test]
    public async Task ApplyTitle_RealTitle_StillUpdatesDetectedTitle()
    {
        var session = new TerminalSession(Path.GetTempPath(), new FakeSessionHost());

        session.ApplyTitle("some real conversation title");

        await Assert.That(session.DetectedTitle).IsEqualTo("some real conversation title");
        await Assert.That(session.ObservedClaudeSessionId).IsNull();
        await Assert.That(session.Activity).IsEqualTo(SessionActivity.Idle);
    }

    [Test]
    public async Task ApplyTitle_SessionIdMarkerThenNewOne_UpdatesToLatest()
    {
        // Mirrors what happens across two hook firings after /clear swaps Claude onto a new
        // transcript mid-session: the second, differing marker should win.
        var session = new TerminalSession(Path.GetTempPath(), new FakeSessionHost());
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();

        session.ApplyTitle($"MULTICLOD_SESSION:{firstId}");
        session.ApplyTitle($"MULTICLOD_SESSION:{secondId}");

        await Assert.That(session.ObservedClaudeSessionId).IsEqualTo(secondId);
    }

    // Minimal ISessionHost stub - TerminalSession's constructor only subscribes to
    // StateChanged/TitleChanged and never touches Pane, so Pane deliberately throws if a test ever
    // starts relying on it unexpectedly.
    private sealed class FakeSessionHost : ISessionHost
    {
        public ITerminalPane Pane => throw new NotSupportedException();

        public SessionState State => SessionState.NotStarted;

#pragma warning disable CS0067 // never raised - TerminalSession only needs to subscribe successfully
        public event EventHandler<SessionState>? StateChanged;

        public event EventHandler? CloseRequested;

        public event EventHandler<string>? TitleChanged;
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
