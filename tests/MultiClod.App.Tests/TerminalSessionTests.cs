using MultiClod.Terminal.Abstractions;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace MultiClod.App.Tests;

public sealed class TerminalSessionTests
{
    [Test]
    public async Task ApplyTitle_SessionIdOnly_SetsObservedClaudeSessionIdOnly()
    {
        var session = new TerminalSession(Path.GetTempPath(), new FakeSessionHost());
        var claudeSessionId = Guid.NewGuid();

        session.ApplyTitle($"MULTICLOD:{claudeSessionId}|");

        await Assert.That(session.ObservedClaudeSessionId).IsEqualTo(claudeSessionId);
        await Assert.That(session.DetectedTitle).IsNull();
        await Assert.That(session.Activity).IsEqualTo(SessionActivity.Idle);
    }

    [Test]
    public async Task ApplyTitle_ActivityOnly_StillUpdatesActivityNotSessionId()
    {
        var session = new TerminalSession(Path.GetTempPath(), new FakeSessionHost());

        session.ApplyTitle("MULTICLOD:|Working");

        await Assert.That(session.Activity).IsEqualTo(SessionActivity.Working);
        await Assert.That(session.ObservedClaudeSessionId).IsNull();
        await Assert.That(session.DetectedTitle).IsNull();
    }

    [Test]
    public async Task ApplyTitle_CombinedMarker_SetsBothSessionIdAndActivityFromOneTitle()
    {
        // The whole point of packing both into one OSC sequence (see claude-session-signal.ps1's
        // remarks) - Claude Code only reliably forwards one title-setting sequence per hook
        // response, so both pieces of state must land from a single ApplyTitle call.
        var session = new TerminalSession(Path.GetTempPath(), new FakeSessionHost());
        var claudeSessionId = Guid.NewGuid();

        session.ApplyTitle($"MULTICLOD:{claudeSessionId}|Working");

        await Assert.That(session.ObservedClaudeSessionId).IsEqualTo(claudeSessionId);
        await Assert.That(session.Activity).IsEqualTo(SessionActivity.Working);
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
    public async Task ApplyTitle_SessionIdThenNewOne_UpdatesToLatest()
    {
        // Mirrors what happens across two hook firings after /clear swaps Claude onto a new
        // transcript mid-session: the second, differing marker should win.
        var session = new TerminalSession(Path.GetTempPath(), new FakeSessionHost());
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();

        session.ApplyTitle($"MULTICLOD:{firstId}|");
        session.ApplyTitle($"MULTICLOD:{secondId}|");

        await Assert.That(session.ObservedClaudeSessionId).IsEqualTo(secondId);
    }

    [Test]
    public async Task ApplyTitle_StopWithNoPendingTask_SetsDoneImmediately()
    {
        // Regression check for the common case - no background Task call was ever started, so
        // Stop should behave exactly as before this change.
        var session = new TerminalSession(Path.GetTempPath(), new FakeSessionHost());

        session.ApplyTitle("MULTICLOD:|Working");
        session.ApplyTitle("MULTICLOD:|Stop");

        await Assert.That(session.Activity).IsEqualTo(SessionActivity.Done);
    }

    [Test]
    public async Task ApplyTitle_StopWithBackgroundTaskRunning_DefersDoneUntilItFinishes()
    {
        var session = new TerminalSession(Path.GetTempPath(), new FakeSessionHost());

        session.ApplyTitle("MULTICLOD:|Working");
        session.ApplyTitle("MULTICLOD:|Stop::1");

        await Assert.That(session.Activity).IsEqualTo(SessionActivity.Working);

        session.ApplyTitle("MULTICLOD:|BackgroundSync::0");

        await Assert.That(session.Activity).IsEqualTo(SessionActivity.Done);
    }

    [Test]
    public async Task BackgroundTaskCount_TracksReportedValueAndRaisesPropertyChanged()
    {
        // The count backing MainWindow.xaml's spinner badge - see
        // SessionNodeViewModel.BackgroundTaskBadgeText. Must move independently of Activity (2 -> 1
        // outstanding agents is not an Activity change) and still notify.
        var session = new TerminalSession(Path.GetTempPath(), new FakeSessionHost());
        var raisedProperties = new List<string?>();
        session.PropertyChanged += (_, e) => raisedProperties.Add(e.PropertyName);

        session.ApplyTitle("MULTICLOD:|Working");
        session.ApplyTitle("MULTICLOD:|Stop::2");

        await Assert.That(session.BackgroundTaskCount).IsEqualTo(2);
        await Assert.That(raisedProperties).Contains(nameof(TerminalSession.BackgroundTaskCount));

        raisedProperties.Clear();
        session.ApplyTitle("MULTICLOD:|BackgroundSync::1");

        await Assert.That(session.BackgroundTaskCount).IsEqualTo(1);
        await Assert.That(session.Activity).IsEqualTo(SessionActivity.Working);
        await Assert.That(raisedProperties).Contains(nameof(TerminalSession.BackgroundTaskCount));
        await Assert.That(raisedProperties).DoesNotContain(nameof(TerminalSession.Activity));
    }

    [Test]
    public async Task ApplyTitle_StopWithNoBackgroundTasks_SetsDoneImmediately()
    {
        // A turn whose subagents all finished before it ended reports a count of zero, and must
        // behave exactly like a turn that never spawned one.
        var session = new TerminalSession(Path.GetTempPath(), new FakeSessionHost());

        session.ApplyTitle("MULTICLOD:|Working");
        session.ApplyTitle("MULTICLOD:|Stop::0");

        await Assert.That(session.Activity).IsEqualTo(SessionActivity.Done);
    }

    [Test]
    public async Task ApplyTitle_MultipleBackgroundTasks_WaitsForAllToFinish()
    {
        var session = new TerminalSession(Path.GetTempPath(), new FakeSessionHost());

        session.ApplyTitle("MULTICLOD:|Working");
        session.ApplyTitle("MULTICLOD:|Stop::2");
        session.ApplyTitle("MULTICLOD:|BackgroundSync::1");

        await Assert.That(session.Activity).IsEqualTo(SessionActivity.Working);

        session.ApplyTitle("MULTICLOD:|BackgroundSync::0");

        await Assert.That(session.Activity).IsEqualTo(SessionActivity.Done);
    }

    [Test]
    public async Task ApplyTitle_BackgroundCountIsLevelNotDelta_SoALostSignalSelfCorrects()
    {
        // The whole reason the count is re-read from Claude Code's background_tasks list on every
        // firing rather than accumulated: a hook subprocess that dies without ever reporting (a
        // known Windows issue) must not leave the session wedged on a spinner forever. The next
        // report that does land is trusted outright.
        var session = new TerminalSession(Path.GetTempPath(), new FakeSessionHost());

        session.ApplyTitle("MULTICLOD:|Working");
        session.ApplyTitle("MULTICLOD:|Stop::3");

        await Assert.That(session.Activity).IsEqualTo(SessionActivity.Working);

        // Nothing reported 2 or 1 - they were lost. A single accurate report still settles it.
        session.ApplyTitle("MULTICLOD:|BackgroundSync::0");

        await Assert.That(session.Activity).IsEqualTo(SessionActivity.Done);
    }

    [Test]
    public async Task ApplyTitle_StopWithoutBackgroundCount_KeepsLastKnownCount()
    {
        // A Claude Code build that doesn't report background_tasks at all (or a hook whose stdin
        // JSON failed to parse) leaves the field off entirely - which must not be read as zero and
        // clear a spinner for work that is still running.
        var session = new TerminalSession(Path.GetTempPath(), new FakeSessionHost());

        session.ApplyTitle("MULTICLOD:|Working");
        session.ApplyTitle("MULTICLOD:|Stop::2");
        session.ApplyTitle("MULTICLOD:|Working");
        session.ApplyTitle("MULTICLOD:|Stop");

        await Assert.That(session.Activity).IsEqualTo(SessionActivity.Working);
    }

    [Test]
    public async Task ApplyTitle_StickyNeedsInputWithBackgroundTask_StaysNeedsInput()
    {
        // A question Claude actually asked the user outranks background work - it's the one state
        // where the session is blocked on them rather than merely still busy.
        var session = new TerminalSession(Path.GetTempPath(), new FakeSessionHost());
        var promptId = Guid.NewGuid().ToString();

        session.ApplyTitle("MULTICLOD:|Working");
        session.ApplyTitle($"MULTICLOD:|NeedsInputSticky:{promptId}");
        session.ApplyTitle($"MULTICLOD:|Stop:{promptId}:1");

        await Assert.That(session.Activity).IsEqualTo(SessionActivity.NeedsInput);

        session.ApplyTitle("MULTICLOD:|BackgroundSync::0");

        await Assert.That(session.Activity).IsEqualTo(SessionActivity.NeedsInput);
    }

    [Test]
    public async Task ApplyTitle_PermissionPromptAfterTurnEnded_DoesNotClobberBackgroundSpinner()
    {
        // Regression for the reported bug: a background agent's own permission prompt arrives after
        // the foreground turn already ended, and used to latch the icon on NeedsInput for the whole
        // (here, minutes-long) run of the agent, then jump straight to Done. Verified against a real
        // captured hook sequence - Working, Stop(1 background task), permission_prompt, then nothing
        // at all until the agent finished.
        var session = new TerminalSession(Path.GetTempPath(), new FakeSessionHost());
        var promptId = Guid.NewGuid().ToString();

        session.ApplyTitle($"MULTICLOD:|Working:{promptId}");
        session.ApplyTitle($"MULTICLOD:|Stop:{promptId}:1");
        session.ApplyTitle($"MULTICLOD:|NeedsInputTransient:{promptId}");

        await Assert.That(session.Activity).IsEqualTo(SessionActivity.Working);

        session.ApplyTitle("MULTICLOD:|BackgroundSync::0");

        await Assert.That(session.Activity).IsEqualTo(SessionActivity.Done);
    }

    [Test]
    public async Task ApplyTitle_PermissionPromptDuringTurn_StillShowsNeedsInput()
    {
        // The counterpart to the above: a prompt raised while the foreground turn is genuinely
        // in flight is a real "go look at this session" signal and must still show, since that
        // turn's own Stop is guaranteed to clear it.
        var session = new TerminalSession(Path.GetTempPath(), new FakeSessionHost());
        var promptId = Guid.NewGuid().ToString();

        session.ApplyTitle($"MULTICLOD:|Working:{promptId}");
        session.ApplyTitle($"MULTICLOD:|NeedsInputTransient:{promptId}");

        await Assert.That(session.Activity).IsEqualTo(SessionActivity.NeedsInput);

        session.ApplyTitle($"MULTICLOD:|Stop:{promptId}:0");

        await Assert.That(session.Activity).IsEqualTo(SessionActivity.Done);
    }

    [Test]
    public async Task ApplyTitle_StaleStopFromOlderTurn_DoesNotClobberNewerWorking()
    {
        // Regression for a stuck/late Claude Code hook subprocess (observed on Windows - see
        // TerminalSession.currentTurnPromptId's remarks): a Stop straggling in long after its own
        // turn ended, once the user has already started a new one, must not overwrite the new
        // turn's Working back to Done.
        var session = new TerminalSession(Path.GetTempPath(), new FakeSessionHost());
        var oldPromptId = Guid.NewGuid().ToString();
        var newPromptId = Guid.NewGuid().ToString();

        session.ApplyTitle($"MULTICLOD:|Working:{oldPromptId}");
        session.ApplyTitle($"MULTICLOD:|Working:{newPromptId}");
        session.ApplyTitle($"MULTICLOD:|Stop:{oldPromptId}");

        await Assert.That(session.Activity).IsEqualTo(SessionActivity.Working);

        session.ApplyTitle($"MULTICLOD:|Stop:{newPromptId}");

        await Assert.That(session.Activity).IsEqualTo(SessionActivity.Done);
    }

    [Test]
    public async Task ApplyTitle_StopWithNoPromptId_StillCompletesCurrentTurn()
    {
        // A Stop whose stdin JSON failed to parse (promptId null) - or from a Claude Code version
        // older than the one that added prompt_id - degrades to the old trust-it-unconditionally
        // behavior rather than being treated as stale.
        var session = new TerminalSession(Path.GetTempPath(), new FakeSessionHost());
        var promptId = Guid.NewGuid().ToString();

        session.ApplyTitle($"MULTICLOD:|Working:{promptId}");
        session.ApplyTitle("MULTICLOD:|Stop");

        await Assert.That(session.Activity).IsEqualTo(SessionActivity.Done);
    }

    [Test]
    public async Task HandleInterruptDetected_WhileWorking_SetsInterrupted()
    {
        var session = new TerminalSession(Path.GetTempPath(), new FakeSessionHost());

        session.ApplyTitle("MULTICLOD:|Working");
        session.HandleInterruptDetected();

        await Assert.That(session.Activity).IsEqualTo(SessionActivity.Interrupted);
    }

    [Test]
    public async Task HandleInterruptDetected_WhileNeedsInput_IsNoOp()
    {
        var session = new TerminalSession(Path.GetTempPath(), new FakeSessionHost());

        session.ApplyTitle("MULTICLOD:|Working");
        session.ApplyTitle("MULTICLOD:|NeedsInputSticky:prompt");

        session.HandleInterruptDetected();

        await Assert.That(session.Activity).IsEqualTo(SessionActivity.NeedsInput);
    }

    [Test]
    public async Task HandleInterruptDetected_WhileDone_IsNoOp()
    {
        var session = new TerminalSession(Path.GetTempPath(), new FakeSessionHost());

        session.ApplyTitle("MULTICLOD:|Working");
        session.ApplyTitle("MULTICLOD:|Stop");

        session.HandleInterruptDetected();

        await Assert.That(session.Activity).IsEqualTo(SessionActivity.Done);
    }

    [Test]
    public async Task HandleInterruptDetected_WithPendingBackgroundTask_DoesNotLaterFlipToDone()
    {
        // An interrupted turn abandons whatever background agents it had outstanding - a later
        // report that they've drained shouldn't resurrect the old turn's deferred Done.
        var session = new TerminalSession(Path.GetTempPath(), new FakeSessionHost());

        session.ApplyTitle("MULTICLOD:|Working");
        session.ApplyTitle("MULTICLOD:|Stop::1");
        session.HandleInterruptDetected();

        await Assert.That(session.Activity).IsEqualTo(SessionActivity.Interrupted);

        session.ApplyTitle("MULTICLOD:|BackgroundSync::0");

        await Assert.That(session.Activity).IsEqualTo(SessionActivity.Interrupted);
    }

    [Test]
    public async Task ClearLatchedActivity_WhileInterrupted_ResetsToIdle()
    {
        var session = new TerminalSession(Path.GetTempPath(), new FakeSessionHost());

        session.ApplyTitle("MULTICLOD:|Working");
        session.HandleInterruptDetected();
        session.ClearLatchedActivity();

        await Assert.That(session.Activity).IsEqualTo(SessionActivity.Idle);
    }

    [Test]
    public async Task HandleApiErrorDetected_WhileWorking_SetsError()
    {
        var session = new TerminalSession(Path.GetTempPath(), new FakeSessionHost());

        session.ApplyTitle("MULTICLOD:|Working");
        session.HandleApiErrorDetected();

        await Assert.That(session.Activity).IsEqualTo(SessionActivity.Error);
    }

    [Test]
    public async Task HandleApiErrorDetected_WhileNeedsInput_StillSetsError()
    {
        // Unlike HandleInterruptDetected, this isn't gated on Activity == Working - a dropped
        // connection is a real "something broke" signal that should win regardless of what else
        // was going on.
        var session = new TerminalSession(Path.GetTempPath(), new FakeSessionHost());

        session.ApplyTitle("MULTICLOD:|Working");
        session.ApplyTitle("MULTICLOD:|NeedsInputSticky:prompt");
        session.HandleApiErrorDetected();

        await Assert.That(session.Activity).IsEqualTo(SessionActivity.Error);
    }

    [Test]
    public async Task HandleApiErrorDetected_WithPendingBackgroundTask_DoesNotLaterFlipToDone()
    {
        var session = new TerminalSession(Path.GetTempPath(), new FakeSessionHost());

        session.ApplyTitle("MULTICLOD:|Working");
        session.ApplyTitle("MULTICLOD:|Stop::1");
        session.HandleApiErrorDetected();

        await Assert.That(session.Activity).IsEqualTo(SessionActivity.Error);

        session.ApplyTitle("MULTICLOD:|BackgroundSync::0");

        await Assert.That(session.Activity).IsEqualTo(SessionActivity.Error);
    }

    [Test]
    public async Task ClearLatchedActivity_WhileError_ResetsToIdle()
    {
        var session = new TerminalSession(Path.GetTempPath(), new FakeSessionHost());

        session.ApplyTitle("MULTICLOD:|Working");
        session.HandleApiErrorDetected();
        session.ClearLatchedActivity();

        await Assert.That(session.Activity).IsEqualTo(SessionActivity.Idle);
    }

    [Test]
    public async Task ApplyTitle_NewTurnAfterApiError_ClearsError()
    {
        var session = new TerminalSession(Path.GetTempPath(), new FakeSessionHost());

        session.ApplyTitle("MULTICLOD:|Working");
        session.HandleApiErrorDetected();
        session.ApplyTitle("MULTICLOD:|Working");

        await Assert.That(session.Activity).IsEqualTo(SessionActivity.Working);
    }

    // Minimal ISessionHost stub - TerminalSession's constructor only subscribes to
    // StateChanged/TitleChanged and never touches Pane, so Pane deliberately throws if a test ever
    // starts relying on it unexpectedly.
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
