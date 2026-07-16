namespace MultiClod.App;

// Mirrors the modes Claude Code itself cycles through via Shift+Tab inside a running session -
// this only controls which one a brand-new session (see MainWindow.LaunchSession) starts in via
// --permission-mode, not anything a user does with Shift+Tab afterwards. Manual is first (so it's
// the enum's default(T) / AppSettings' unset value) since it's the safest starting point - every
// tool call still needs an explicit yes. Values must match `claude --help`'s --permission-mode
// choices exactly ("manual", "auto", "acceptEdits", "plan", "bypassPermissions") - see the mapping
// in MainWindow.LaunchSession.
public enum ClaudePermissionMode
{
    Manual,
    Auto,
    AcceptEdits,
    Plan,
    BypassPermissions,
}
