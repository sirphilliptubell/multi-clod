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
}
