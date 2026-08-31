namespace MultiClod.App;

// Orthogonal to SessionState (which only tracks the OS process lifecycle) - this tracks what the
// Claude Code CLI running *inside* a Running session is doing, sourced from its own hooks (see
// TerminalSession.OnHostTitleChanged). NeedsInput and Done latch until the user re-focuses the
// session - see TerminalSession.ClearLatchedActivity.
public enum SessionActivity
{
    Idle,
    Working,
    NeedsInput,
    Done,

    // Claude Code's Stop hook never fires for a user-interrupted (e.g. Escape) turn, so unlike
    // NeedsInput/Done this isn't reached via a structured hook signal - see
    // TerminalSession.HandleInterruptDetected. Deliberately its own state rather than folding back
    // into Idle/Done: we only know the turn was cut off, not whether Claude was about to finish,
    // ask a question, or start a background task, so the UI shows this as "unknown" instead of
    // guessing. Latches like NeedsInput/Done - see TerminalSession.ClearLatchedActivity.
    Interrupted,

    // The CLI printed one of its own "API Error: ..." lines (e.g. a dropped connection
    // mid-response) - see ConPtyConnection.ScanForApiErrorMarker. Also reached only via a raw text
    // scan, not a hook: the CLI's own Stop hook never fires for a turn that died this way, so
    // without this the session would spin on Working forever. Outranks every other Activity (see
    // SessionActivityTracker.Activity) since it means something actually went wrong, not just that
    // the turn ended in some ordinary way. Latches like NeedsInput/Done - see
    // TerminalSession.ClearLatchedActivity.
    Error,
}
