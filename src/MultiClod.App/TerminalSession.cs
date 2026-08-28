using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using MultiClod.App.Diagnostics;
using MultiClod.Terminal.Abstractions;

namespace MultiClod.App;

/// <summary>
/// A live host's bindable status, owned by a <see cref="SessionNodeViewModel"/> while that node
/// is running. Identity/naming live on the tree node instead - this class only exists for as long
/// as the process does, and is discarded (never reused) when the session is stopped.
/// </summary>
public sealed class TerminalSession : INotifyPropertyChanged
{
    private static readonly Brush StartingBrush = Brushes.Goldenrod;
    private static readonly Brush RunningBrush = Brushes.LimeGreen;
    private static readonly Brush FaultedBrush = Brushes.OrangeRed;

    // NotStarted and Exited both render as a hollow outline instead of a flat fill (see
    // MainWindow.xaml's StatusDot IsHollow trigger) - this is the stroke color for that outline.
    private static readonly Brush HollowBrush = Brushes.LimeGreen;

    // Hook-emitted markers ride the same OSC 2 (window title) channel real Claude-set titles use -
    // see ConPtyConnection.ScanForTitleSequences - distinguished only by this prefix, which a real
    // conversation title would never start with. See claude-session-signal.ps1.
    //
    // Packs both the activity kind and Claude's live session_id into ONE title
    // ("MULTICLOD:<sessionId>|<kind>[:promptId]") rather than two independent OSC sequences -
    // live testing showed Claude Code only forwards the LAST title-setting escape sequence from a
    // hook's terminalSequence when more than one is present, silently dropping the rest. A single
    // combined sequence sidesteps that entirely.
    private const string CombinedSentinelPrefix = "MULTICLOD:";

    private SessionState state = SessionState.Starting;
    private string statusText = "Starting";
    private Brush statusBrush = StartingBrush;
    private bool isHollow;
    private string? detectedTitle;
    private Guid? observedClaudeSessionId;

    // Owns every fact behind the Activity glyph, and the priority rules that reduce them to one
    // value - see SessionActivityTracker for why those are kept apart from the displayed enum
    // rather than each hook assigning to it directly. Plain in-memory, same as before: a restart
    // kills the whole process tree (ConPtyConnection.Dispose), so starting from scratch is always
    // correct rather than something that needs to survive a relaunch.
    private readonly SessionActivityTracker tracker = new();

    // initialActivity seeds this session's glyph with its node's last-persisted settled state
    // (see SessionNodeViewModel.LastActivity) - LaunchSession relaunches every previously-started
    // session immediately on startup, so without this every node would flash back to a blank Idle
    // glyph the instant the app reopens, even though it's about to show the exact same live
    // Activity again once real hook signals resume.
    public TerminalSession(string workingDirectory, ISessionHost host, SessionActivity initialActivity = SessionActivity.Idle)
    {
        this.WorkingDirectory = workingDirectory;
        this.Host = host;
        this.tracker.Seed(initialActivity);
        this.Host.StateChanged += this.OnHostStateChanged;
        this.Host.TitleChanged += this.OnHostTitleChanged;
        this.Host.InterruptDetected += this.OnHostInterruptDetected;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string WorkingDirectory { get; }

    public ISessionHost Host { get; }

    public SessionState State
    {
        get => this.state;
        private set => this.SetField(ref this.state, value);
    }

    public string StatusText
    {
        get => this.statusText;
        private set => this.SetField(ref this.statusText, value);
    }

    public Brush StatusBrush
    {
        get => this.statusBrush;
        private set => this.SetField(ref this.statusBrush, value);
    }

    public bool IsHollow
    {
        get => this.isHollow;
        private set => this.SetField(ref this.isHollow, value);
    }

    // The terminal title Claude Code (or whatever's running) set via an OSC 0/2 escape sequence,
    // if any has been seen yet - see ConPtyConnection.ScanForTitleSequences.
    public string? DetectedTitle
    {
        get => this.detectedTitle;
        private set => this.SetField(ref this.detectedTitle, value);
    }

    // What the Claude Code process inside this session is doing right now, per its own hooks -
    // see OnHostTitleChanged. Only meaningful while State == Running; reset to Idle otherwise.
    // Derived rather than stored - see SessionActivityTracker.
    public SessionActivity Activity => this.tracker.Activity;

    // How many background agents are running right now - see SessionActivityTracker's own remarks
    // and SessionNodeViewModel.BackgroundTaskBadgeText for where this actually gets shown. Can
    // change independently of Activity (e.g. 2 -> 1 outstanding agents while still Working), which
    // is why MutateActivity diffs this separately rather than only the derived enum.
    public int BackgroundTaskCount => this.tracker.OutstandingBackgroundTasks;

    // Claude Code's own live session_id, as last reported by a hook firing - see OnHostTitleChanged.
    // Null until the first hook fires. MainWindow.LaunchSession compares this against the owning
    // node's persisted ClaudeSessionId and corrects/re-saves the node when they diverge (e.g. after
    // /clear inside the CLI swaps Claude onto a new transcript underneath us).
    public Guid? ObservedClaudeSessionId
    {
        get => this.observedClaudeSessionId;
        private set => this.SetField(ref this.observedClaudeSessionId, value);
    }

    // Clears a latched NeedsInput/Done back to Idle once the user looks at this session again -
    // called from SessionNodeViewModel when the tree selection lands on this session. Never
    // interrupts work that's genuinely still in flight - see SessionActivityTracker.MarkSeen.
    public void ClearLatchedActivity() => this.MutateActivity(t => t.MarkSeen());

    // Every Activity/BackgroundTaskCount change funnels through here so each derived value is
    // compared before and after (rather than each caller trying to work out whether its own signal
    // happened to move it) and PropertyChanged is raised exactly when one actually moved. The two
    // are diffed independently since the count can change (2 -> 1 outstanding agents) without
    // Activity itself moving off Working.
    private void MutateActivity(Action<SessionActivityTracker> change)
    {
        var activityBefore = this.tracker.Activity;
        var countBefore = this.tracker.OutstandingBackgroundTasks;
        change(this.tracker);

        if (this.tracker.Activity != activityBefore)
        {
            this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(this.Activity)));
        }

        if (this.tracker.OutstandingBackgroundTasks != countBefore)
        {
            this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(this.BackgroundTaskCount)));
        }
    }

    private void OnHostTitleChanged(object? sender, string title)
    {
        // Same cross-thread rationale as OnHostStateChanged below - ISessionHost.TitleChanged
        // fires from ConPtyConnection's output-pump thread.
        Application.Current.Dispatcher.BeginInvoke(() => this.ApplyTitle(title));
    }

    // Split out from OnHostTitleChanged so tests can drive the marker-parsing logic directly
    // without needing a live WPF Application/Dispatcher on the test thread.
    internal void ApplyTitle(string title)
    {
        DebugLog.LogTerminal($"ApplyTitle raw={title}");

        if (!title.StartsWith(CombinedSentinelPrefix, StringComparison.Ordinal))
        {
            this.DetectedTitle = title;
            return;
        }

        // "<sessionId>|<kind>[:promptId]" - see claude-session-signal.ps1. sessionId is empty when
        // the hook's own JSON parse failed; Guid.TryParse rejects that harmlessly, leaving
        // ObservedClaudeSessionId untouched rather than clearing a previously-good value.
        var combined = title[CombinedSentinelPrefix.Length..];
        var pipeIndex = combined.IndexOf('|');
        var sessionIdPart = pipeIndex < 0 ? string.Empty : combined[..pipeIndex];
        var marker = pipeIndex < 0 ? combined : combined[(pipeIndex + 1)..];

        if (Guid.TryParse(sessionIdPart, out var sessionId))
        {
            this.ObservedClaudeSessionId = sessionId;
        }

        // "Kind", "Kind:promptId" and "Kind:promptId:backgroundTaskCount" - see
        // claude-session-signal.ps1. Every marker carries a promptId whenever the hook's own stdin
        // JSON included one (Claude Code v2.1.196+ sends prompt_id on every hook event for a turn,
        // Working/Stop included - confirmed against real debug-hooks-*.log captures); older Claude
        // Code versions or a hook whose stdin JSON failed to parse just leave it empty. A real
        // Claude Code prompt_id is a UUID, so neither field can contain ':' itself.
        var fields = marker.Split(':');
        var kind = fields[0];
        var promptId = fields.Length > 1 && fields[1].Length > 0 ? fields[1] : null;

        // Absent (rather than zero) whenever Claude Code didn't report a background_tasks list at
        // all - the tracker then keeps whatever it last knew instead of assuming nothing is running.
        var backgroundTasks = fields.Length > 2 && int.TryParse(fields[2], out var parsed) ? parsed : (int?)null;

        switch (kind)
        {
            case "Working":
                this.MutateActivity(t => t.OnTurnStarted(promptId));
                break;
            case "NeedsInputSticky":
                this.MutateActivity(t => t.OnQuestionAsked(promptId));
                break;
            case "NeedsInputTransient":
                this.MutateActivity(t => t.OnPermissionPromptRaised());
                break;
            case "Stop":
                this.MutateActivity(t => t.OnTurnEnded(promptId, backgroundTasks));
                break;
            case "BackgroundSync":
                // SubagentStop, carrying how many background agents are still running now that this
                // one has finished. Nothing to do if the count didn't come through - see above.
                if (backgroundTasks is { } remaining)
                {
                    this.MutateActivity(t => t.OnBackgroundTasksReported(remaining));
                }

                break;
        }
    }

    private void OnHostInterruptDetected(object? sender, EventArgs e)
    {
        // Same cross-thread rationale as OnHostTitleChanged/OnHostStateChanged - raised from
        // ConPtyConnection's output-pump thread.
        Application.Current.Dispatcher.BeginInvoke(this.HandleInterruptDetected);
    }

    // Split out from OnHostInterruptDetected so tests can drive it directly, same rationale as
    // ApplyTitle/OnHostTitleChanged.
    internal void HandleInterruptDetected()
    {
        // Compensates for Claude Code's Stop hook never firing on a user-interrupted turn (e.g.
        // pressing Escape) - see IPtyConnection.InterruptDetected. Only meaningful while a turn
        // is genuinely in flight; a stray "Interrupted" the assistant happens to type into its own
        // prose while Idle/NeedsInput/Done/Interrupted is a harmless no-op here rather than
        // clobbering those.
        if (this.Activity == SessionActivity.Working)
        {
            this.MutateActivity(t => t.OnInterrupted());
        }
    }

    private void OnHostStateChanged(object? sender, SessionState state)
    {
        // ISessionHost.StateChanged fires from ConPtyConnection's output-pump thread, the wrapped
        // Process.Exited callback, or - during MainWindow.OnClosing - a Task.Run thread running
        // Host.Dispose() while the UI thread blocks on Task.WaitAll for that same Dispose() to
        // return. A blocking Dispatcher.Invoke here would deadlock in that last case: the UI
        // thread can't pump the dispatcher queue while it's parked in WaitAll, so the invoke would
        // never complete, Dispose() would never return, and WaitAll would never unblock. BeginInvoke
        // posts and returns immediately, so it can't deadlock; nothing here needs to observe the
        // property update actually landing before continuing.
        Application.Current.Dispatcher.BeginInvoke(() =>
        {
            (string text, Brush brush, bool hollow) = state switch
            {
                SessionState.NotStarted => ("Not started", HollowBrush, true),
                SessionState.Starting => ("Starting", StartingBrush, false),
                SessionState.Running => ("Running", RunningBrush, false),
                SessionState.Exited => ("Exited", HollowBrush, true),
                SessionState.Faulted => ("Faulted", FaultedBrush, false),
                _ => (state.ToString(), HollowBrush, true),
            };

            this.State = state;
            this.StatusText = text;
            this.StatusBrush = brush;
            this.IsHollow = hollow;

            // Activity only means something while the process is actually running - drop any
            // latched icon rather than have it linger over a dead/restarted session.
            if (state != SessionState.Running)
            {
                this.MutateActivity(t => t.Reset());
            }
        });
    }

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (Equals(field, value))
        {
            return;
        }

        field = value;
        this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
