using MultiClod.App.Theming;

namespace MultiClod.App.Persistence;

/// <summary>
/// The root of settings.json - user-configurable app preferences, shown/edited via
/// Settings\SettingsView and applied by MainWindow. New settings (e.g. a theme choice) get their
/// own property here alongside the rest. A record (rather than a plain class, like the rest of
/// this Persistence folder) so callers can use `with` to change one field of an otherwise-loaded
/// instance without hand-copying every other property along the way.
/// </summary>
public sealed record AppSettings
{
    /// <summary>
    /// App-wide color scheme - see Theming\ThemeManager for how this is applied.
    /// </summary>
    public AppTheme Theme { get; init; } = AppTheme.Dark;

    /// <summary>
    /// When true, Shift+Enter inserts a newline in a running session's terminal instead of Enter's
    /// normal submit behavior - a stand-in for Ctrl+Enter, which Claude Code also accepts for the
    /// same purpose. See TerminalContainer's NewlineOnShiftEnter for how this is actually applied.
    /// </summary>
    public bool UseShiftEnterForNewline { get; init; }

    /// <summary>
    /// Pre-fills the folder field of AddSessionDialog for a brand-new session under a Project (not
    /// one added from an existing session, which defaults to that session's own folder instead).
    /// Null/empty means "no default" - the dialog falls back to the user's profile folder.
    /// </summary>
    public string? DefaultRootFolder { get; init; }

    /// <summary>
    /// Pre-checks AddSessionDialog's "Create a git worktree" option for a brand-new session - the
    /// user can still uncheck it per-session. See Git\GitWorktree for what checking it actually
    /// does.
    /// </summary>
    public bool UseWorktreeByDefault { get; init; }

    /// <summary>
    /// The --permission-mode a brand-new session's `claude` process is launched with - see
    /// MainWindow.LaunchSession. Defaults to Manual (enum value 0), matching Claude Code's own
    /// safest-by-default behavior for a session nothing has configured yet.
    /// </summary>
    public ClaudePermissionMode DefaultPermissionMode { get; init; }

    /// <summary>
    /// Shows estimated Claude API cost badges next to session names (tree/tabs), in the Session Log
    /// window, and per transcript row - see Costs\CostDisplaySettings, the app-wide live-bindable
    /// flag this is pushed into. Defaults to true (on), unlike this file's other booleans, since
    /// cost visibility is the feature's whole point rather than an opt-in convenience.
    /// </summary>
    public bool ShowCosts { get; init; } = true;

    /// <summary>
    /// Passes CLAUDE_CODE_DISABLE_MOUSE=1 to a brand-new session's `claude` process - see
    /// MainWindow.LaunchSession. Claude Code's own TUI otherwise enables terminal mouse-reporting
    /// and auto-copies a dragged/double-clicked selection via an OSC 52 escape sequence, with no
    /// setting yet to opt out of just that (github.com/anthropics/claude-code/issues/60755) -
    /// disabling mouse-reporting entirely is the only lever available today, and it also gives up
    /// any other mouse interaction inside the CLI, so this defaults to off (false) rather than
    /// silently changing everyone's terminal behavior.
    /// </summary>
    public bool DisableMouseCopy { get; init; }

    /// <summary>
    /// Remaps Ctrl+Z to send Ctrl+_ instead, in every session's terminal - see
    /// TerminalContainer.RemapCtrlZForUndo. Claude Code's CLI reserves Ctrl+Z for Unix
    /// process-suspend (a no-op on Windows) and binds its own undo to Ctrl+_/Ctrl+Shift+-
    /// instead, so plain Ctrl+Z otherwise does nothing useful there. Defaults to true - unlike
    /// this file's other opt-in booleans, there's no real downside to remapping a key Windows
    /// sessions have no other use for.
    /// </summary>
    public bool RemapCtrlZForUndo { get; init; } = true;
}
