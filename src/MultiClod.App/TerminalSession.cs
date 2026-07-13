using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
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

    // Hook-emitted activity markers ride the same OSC 2 (window title) channel real Claude-set
    // titles use - see ConPtyConnection.ScanForTitleSequences - distinguished only by this prefix,
    // which a real conversation title would never start with. See claude-session-signal.ps1.
    private const string ActivitySentinelPrefix = "MULTICLOD_ACTIVITY:";

    // A second, independent OSC 2 sequence emitted alongside the activity marker, carrying Claude
    // Code's own live session_id - see claude-session-signal.ps1 and ObservedClaudeSessionId.
    private const string SessionIdSentinelPrefix = "MULTICLOD_SESSION:";

    private SessionState state = SessionState.Starting;
    private string statusText = "Starting";
    private Brush statusBrush = StartingBrush;
    private bool isHollow;
    private string? detectedTitle;
    private SessionActivity activity = SessionActivity.Idle;
    private Guid? observedClaudeSessionId;

    // The prompt_id that set the current NeedsInput sticky (from the agent_needs_input
    // Notification - Claude asked a question), or null if there is none / it came from
    // permission_prompt (a transient mid-turn block) instead - see OnHostTitleChanged. Keyed on
    // prompt_id rather than a plain bool so a Stop is only suppressed when it belongs to the exact
    // turn that's latched: two independently-spawned hook subprocesses (this one and the
    // UserPromptSubmit one for whatever turn follows) aren't guaranteed to land in the PTY stream
    // in firing order, so a bool cleared by "Working" could still be stale when a later Stop reads
    // it, leaving the icon stuck on NeedsInput through an actually-completed later turn.
    private string? stickyNeedsInputPromptId;

    // Count of Task tool calls (PreToolUse "TaskStart") not yet matched by a SubagentStop
    // ("TaskEnd"). Any Task call still outstanding when Stop fires is treated as a background
    // agent - see ClaudeSessionHooksInstaller. Plain in-memory field, same as activity: a restart
    // kills the whole process tree (ConPtyConnection.Dispose), so starting back at 0 is always
    // correct rather than something that needs to survive a relaunch.
    private int pendingBackgroundTasks;

    // Set when a Stop arrives while pendingBackgroundTasks > 0, so the delayed Done transition
    // (once the counter drains) knows to fire - see OnHostTitleChanged's "TaskEnd"/"Stop" cases.
    private bool awaitingBackgroundCompletion;

    public TerminalSession(string workingDirectory, ISessionHost host)
    {
        this.WorkingDirectory = workingDirectory;
        this.Host = host;
        this.Host.StateChanged += this.OnHostStateChanged;
        this.Host.TitleChanged += this.OnHostTitleChanged;
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
    public SessionActivity Activity
    {
        get => this.activity;
        private set => this.SetField(ref this.activity, value);
    }

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
    // interrupts Working, since that reflects Claude actually being mid-turn right now.
    public void ClearLatchedActivity()
    {
        if (this.Activity is SessionActivity.NeedsInput or SessionActivity.Done)
        {
            this.Activity = SessionActivity.Idle;
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
        if (title.StartsWith(SessionIdSentinelPrefix, StringComparison.Ordinal))
        {
            if (Guid.TryParse(title[SessionIdSentinelPrefix.Length..], out var sessionId))
            {
                this.ObservedClaudeSessionId = sessionId;
            }

            return;
        }

        if (!title.StartsWith(ActivitySentinelPrefix, StringComparison.Ordinal))
        {
            this.DetectedTitle = title;
            return;
        }

        // "Kind" and "Kind:promptId" - see claude-session-signal.ps1. Only NeedsInputSticky and
        // Stop carry a promptId; a real Claude Code prompt_id is a UUID, so it never contains
        // ':' itself.
        var marker = title[ActivitySentinelPrefix.Length..];
        var colonIndex = marker.IndexOf(':');
        var kind = colonIndex < 0 ? marker : marker[..colonIndex];
        var promptId = colonIndex < 0 ? null : marker[(colonIndex + 1)..];

        switch (kind)
        {
            case "Working":
                this.awaitingBackgroundCompletion = false;
                this.Activity = SessionActivity.Working;
                break;
            case "NeedsInputSticky":
                this.stickyNeedsInputPromptId = promptId;
                this.Activity = SessionActivity.NeedsInput;
                break;
            case "NeedsInputTransient":
                this.Activity = SessionActivity.NeedsInput;
                break;
            case "TaskStart":
                this.pendingBackgroundTasks++;
                break;
            case "TaskEnd":
                this.pendingBackgroundTasks = Math.Max(0, this.pendingBackgroundTasks - 1);
                if (this.pendingBackgroundTasks == 0 && this.awaitingBackgroundCompletion)
                {
                    this.awaitingBackgroundCompletion = false;
                    this.Activity = SessionActivity.Done;
                }

                break;
            case "Stop":
                // A sticky "Claude asked a question" is never silently overwritten by the
                // Stop hook that always fires at turn-end for that same turn (matched by
                // promptId); a transient permission-prompt block, or a Stop for any later
                // turn (a different/absent promptId - e.g. once the user has replied), is
                // free to move on to Done - unless a Task tool call from this turn is still
                // outstanding, in which case Done is deferred to the "TaskEnd" case above.
                if (this.stickyNeedsInputPromptId is null || this.stickyNeedsInputPromptId != promptId)
                {
                    if (this.pendingBackgroundTasks > 0)
                    {
                        this.awaitingBackgroundCompletion = true;
                    }
                    else
                    {
                        this.Activity = SessionActivity.Done;
                    }
                }

                break;
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
                this.stickyNeedsInputPromptId = null;
                this.pendingBackgroundTasks = 0;
                this.awaitingBackgroundCompletion = false;
                this.Activity = SessionActivity.Idle;
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
