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
}
