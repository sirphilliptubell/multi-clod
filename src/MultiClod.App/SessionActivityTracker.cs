namespace MultiClod.App;

/// <summary>
/// How the turn that most recently finished ended - see <see cref="SessionActivityTracker"/>.
/// </summary>
internal enum TurnOutcome
{
    None,
    Done,
    Interrupted,
}

/// <summary>
/// Derives a session's single displayed <see cref="SessionActivity"/> from the several
/// *independent, concurrent* facts Claude Code's hooks report about it.
///
/// The distinction matters because those facts genuinely overlap in time: a foreground turn can
/// finish while background agents are still running, and a permission prompt can be raised by
/// either. Earlier revisions stored the displayed enum directly and had each incoming hook assign
/// to it, which meant every signal could silently clobber an unrelated one - the class of bug that
/// produced a background agent showing "needs input" (then jumping straight to "done") for its
/// entire run, because a mid-flight permission-prompt Notification overwrote the deliberately-held
/// Working state. Keeping the facts separate and computing the glyph once, in <see cref="Activity"/>,
/// makes that structurally impossible rather than something each new signal has to remember to
/// guard against.
///
/// Deliberately plain C# with no WPF/dispatcher coupling, so the whole state machine is unit
/// testable directly (see TerminalSessionTests); <see cref="TerminalSession"/> owns one of these
/// and is responsible for raising change notifications.
/// </summary>
internal sealed class SessionActivityTracker
{
    // Sentinel promptId for a question restored from persistence (see Seed) - a real prompt_id is a
    // UUID, so this can never collide with one arriving from a hook.
    private const string SeededQuestionPromptId = "(seeded)";

    private bool turnInFlight;

    // The prompt_id of the turn currently in flight, when one was reported. Correlates a later Stop
    // back to the turn that started it: Claude Code hooks run as separate OS subprocesses
    // (powershell.exe here) and are known to occasionally hang or get suspended on Windows (see
    // anthropics/claude-code#77078), so a Stop can straggle in long after its own turn ended - well
    // after the user has sent a new message - and must not be allowed to end the *new* turn.
    // Null whenever no prompt_id was available (a hook whose stdin JSON failed to parse, or a
    // Claude Code older than the version that started sending prompt_id on every event), which
    // leaves the correlation checks below no-ops exactly as they were before it existed.
    private string? currentTurnPromptId;

    // Set while Claude has asked the user a question that hasn't been answered yet (the
    // agent_needs_input Notification). Latches deliberately: unlike a permission prompt this is a
    // real "this session is blocked on you" state, and it outranks background work - see Activity.
    private string? unansweredQuestionPromptId;

    // Set while a permission prompt was raised *by the foreground turn*. Only tracked for a turn
    // that's actually in flight: Claude Code reports that a prompt was raised but never that it was
    // resolved, so treating one as a latching state is only safe while something else (that turn's
    // own Stop) is guaranteed to clear it. A prompt arriving with no turn in flight belongs to a
    // background agent, whose prompts were observed to resolve on their own without any further
    // signal - latching on those is what wedged the icon before.
    private bool promptBlockingCurrentTurn;

    // Number of background agents still running, as last *reported* by Claude Code rather than
    // accumulated here - see OnBackgroundTasksReported. A level, not an edge count: Claude Code
    // includes the full background_tasks list on every Stop/SubagentStop payload, so this is
    // re-read from the source each time instead of being incremented and decremented (which drifts
    // permanently out of true the first time a hook subprocess dies without firing, and drift in
    // the "still running" direction wedges the session on a spinner that never clears).
    private int outstandingBackgroundTasks;

    /// <summary>
    /// How many background agents are running right now, for the tree's spinner badge (see
    /// SessionNodeViewModel.BackgroundTaskBadgeText) - the same value <see cref="Activity"/>
    /// already folds into Working, exposed on its own since "spinner" doesn't say "how many".
    /// </summary>
    public int OutstandingBackgroundTasks => this.outstandingBackgroundTasks;

    private TurnOutcome lastOutcome;

    // Whether the user has already looked at lastOutcome - see MarkSeen. Keeps a finished/
    // interrupted turn's glyph up until the session is next selected, rather than clearing it
    // immediately where nobody would ever see it.
    private bool outcomeSeen = true;

    // Set when the CLI's own raw output contained an "API Error: ..." line - see
    // TerminalSession.HandleApiErrorDetected. Unlike lastOutcome/outcomeSeen this isn't a settled
    // outcome reached once a turn ends normally; it can fire mid-turn (a dropped connection never
    // gets a matching Stop), so it's tracked as its own always-wins latch rather than folded into
    // that switch. Cleared the same way as the other latches - see MarkSeen - or by starting a new
    // turn, whichever the user does first.
    private bool hasApiError;

    /// <summary>
    /// The one glyph to show, derived fresh from every fact above. Ordering is the priority: an API
    /// error outranks everything else since it means something actually broke, things that are
    /// blocked on the *user* outrank things that are merely still running, and anything still
    /// running outranks the settled outcome of a turn that already ended.
    /// </summary>
    public SessionActivity Activity =>
        this.hasApiError ? SessionActivity.Error
        : this.unansweredQuestionPromptId is not null || this.promptBlockingCurrentTurn ? SessionActivity.NeedsInput
        : this.turnInFlight || this.outstandingBackgroundTasks > 0 ? SessionActivity.Working
        : this.outcomeSeen ? SessionActivity.Idle
        : this.lastOutcome switch
        {
            TurnOutcome.Done => SessionActivity.Done,
            TurnOutcome.Interrupted => SessionActivity.Interrupted,
            _ => SessionActivity.Idle,
        };

    /// <summary>
    /// Restores the glyph a dormant session was last left showing (see
    /// SessionNodeViewModel.LastActivity) so a relaunch-on-startup doesn't blink every node back to
    /// a blank Idle before real hook signals resume. Working is deliberately unrepresentable here:
    /// nothing is running yet that could ever clear it.
    /// </summary>
    public void Seed(SessionActivity activity)
    {
        switch (activity)
        {
            case SessionActivity.NeedsInput:
                this.unansweredQuestionPromptId = SeededQuestionPromptId;
                break;
            case SessionActivity.Done:
                this.lastOutcome = TurnOutcome.Done;
                this.outcomeSeen = false;
                break;
            case SessionActivity.Interrupted:
                this.lastOutcome = TurnOutcome.Interrupted;
                this.outcomeSeen = false;
                break;
            case SessionActivity.Error:
                this.hasApiError = true;
                break;
        }
    }

    /// <summary>UserPromptSubmit - the user sent a message, so any question they were being asked is answered.</summary>
    public void OnTurnStarted(string? promptId)
    {
        this.turnInFlight = true;
        this.currentTurnPromptId = promptId;
        this.unansweredQuestionPromptId = null;
        this.promptBlockingCurrentTurn = false;
        this.lastOutcome = TurnOutcome.None;
        this.outcomeSeen = true;
        this.hasApiError = false;
    }

    /// <summary>Notification/agent_needs_input - Claude asked the user something and is waiting.</summary>
    public void OnQuestionAsked(string? promptId) => this.unansweredQuestionPromptId = promptId ?? SeededQuestionPromptId;

    /// <summary>
    /// Notification/permission_prompt. Ignored unless a foreground turn is in flight - see
    /// <see cref="promptBlockingCurrentTurn"/> for why a background agent's prompt can't be latched.
    /// </summary>
    public void OnPermissionPromptRaised()
    {
        if (this.turnInFlight)
        {
            this.promptBlockingCurrentTurn = true;
        }
    }

    /// <summary>
    /// Stop - the foreground turn ended. <paramref name="outstandingBackgroundTasks"/> is the length
    /// of the payload's background_tasks list, or null when this Claude Code build didn't report one
    /// (in which case whatever was last known is kept rather than assumed to be zero).
    /// </summary>
    public void OnTurnEnded(string? promptId, int? outstandingBackgroundTasks)
    {
        // Reject a Stop that definitely belongs to an older, already-superseded turn - see
        // currentTurnPromptId. Only on a definite mismatch (both sides known and different); a null
        // on either side is trusted, since that's the only signal available when prompt_id isn't.
        if (this.currentTurnPromptId is not null && promptId is not null && this.currentTurnPromptId != promptId)
        {
            return;
        }

        this.turnInFlight = false;
        this.currentTurnPromptId = null;
        this.promptBlockingCurrentTurn = false;

        if (outstandingBackgroundTasks is { } count)
        {
            this.outstandingBackgroundTasks = count;
        }

        // A turn that ended by asking a question isn't "done" - leaving the question set keeps
        // NeedsInput showing (it outranks the outcome anyway), and not recording an outcome means
        // answering the question won't briefly flash Done.
        if (this.unansweredQuestionPromptId is null)
        {
            this.lastOutcome = TurnOutcome.Done;
            this.outcomeSeen = false;
        }
    }

    /// <summary>
    /// SubagentStop - re-reports how many background agents are still running (excluding the one
    /// that just stopped, which Claude Code still lists as running in its own stop payload).
    /// </summary>
    public void OnBackgroundTasksReported(int outstandingBackgroundTasks) =>
        this.outstandingBackgroundTasks = outstandingBackgroundTasks;

    /// <summary>
    /// The user interrupted (e.g. Escape), which Claude Code's Stop hook never fires for - see
    /// IPtyConnection.InterruptDetected. Abandons whatever the turn had outstanding.
    /// </summary>
    public void OnInterrupted()
    {
        this.turnInFlight = false;
        this.currentTurnPromptId = null;
        this.promptBlockingCurrentTurn = false;
        this.outstandingBackgroundTasks = 0;
        this.lastOutcome = TurnOutcome.Interrupted;
        this.outcomeSeen = false;
    }

    /// <summary>
    /// The CLI's own raw output contained an "API Error: ..." line - see
    /// ConPtyConnection.ScanForApiErrorMarker. Reached via a text scan rather than a hook, since a
    /// turn that dies this way never gets a matching Stop; abandons whatever the turn had
    /// outstanding, same as <see cref="OnInterrupted"/>, since nothing further is coming for it.
    /// </summary>
    public void OnApiErrorDetected()
    {
        this.turnInFlight = false;
        this.currentTurnPromptId = null;
        this.promptBlockingCurrentTurn = false;
        this.outstandingBackgroundTasks = 0;
        this.hasApiError = true;
    }

    /// <summary>The process is no longer running - nothing it reported still means anything.</summary>
    public void Reset()
    {
        this.turnInFlight = false;
        this.currentTurnPromptId = null;
        this.unansweredQuestionPromptId = null;
        this.promptBlockingCurrentTurn = false;
        this.outstandingBackgroundTasks = 0;
        this.lastOutcome = TurnOutcome.None;
        this.outcomeSeen = true;
        this.hasApiError = false;
    }

    /// <summary>
    /// The user looked at this session, so a settled outcome (or an answered question) has served
    /// its purpose. Never disturbs work that's genuinely still in flight.
    /// </summary>
    public void MarkSeen()
    {
        this.outcomeSeen = true;
        this.unansweredQuestionPromptId = null;
        this.promptBlockingCurrentTurn = false;
        this.hasApiError = false;
    }
}
