using System.ComponentModel;
using System.Windows.Media;
using MultiClod.App.Validation;
using MultiClod.Terminal.Abstractions;

namespace MultiClod.App;

/// <summary>
/// A session's tree identity and persisted metadata, decoupled from whether it's currently
/// running. <see cref="LiveSession"/> is null for a dormant node (never started, or Stopped) -
/// see MainWindow.LaunchSession/StopSession, which are the only places that attach/detach it.
/// </summary>
public sealed class SessionNodeViewModel : TreeNodeViewModel
{
    // Matches TerminalSession.HollowBrush - a dormant node renders the same hollow LimeGreen
    // outline as a live NotStarted/Exited session, just without a TerminalSession behind it yet.
    private static readonly Brush DormantBrush = Brushes.LimeGreen;

    private string workingDirectory;
    private TerminalSession? liveSession;
    private SessionValidationProblem validationProblem;
    private string? detectedTitle;
    private Guid claudeSessionId;

    public SessionNodeViewModel(Guid id, Guid claudeSessionId, string name, string workingDirectory, bool hasBeenStarted, string? detectedTitle = null)
        : base(name)
    {
        this.Id = id;
        this.claudeSessionId = claudeSessionId;
        this.workingDirectory = workingDirectory;
        this.HasBeenStarted = hasBeenStarted;
        this.detectedTitle = detectedTitle;

        // Renaming a session, or persisting a newly detected title, should both be reflected in
        // DisplayTitle - see the DisplayTitle fallback chain above.
        this.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(this.Name) or nameof(this.DetectedTitle))
            {
                this.RaisePropertyChanged(nameof(this.DisplayTitle));
            }
        };
    }

    public Guid Id { get; }

    public Guid ClaudeSessionId => this.claudeSessionId;

    // Corrects the tracked id when Claude Code's own hooks report it has moved onto a different
    // conversation underneath us (/clear, or an in-CLI /resume) - see TerminalSession's
    // ObservedClaudeSessionId and MainWindow.LaunchSession, the only caller. Deliberately not a
    // plain setter: this should only ever be driven by that live-observation path, never by a UI
    // action, so a node's conversation identity can't be reassigned by accident.
    public void UpdateClaudeSessionId(Guid newClaudeSessionId) => this.SetField(ref this.claudeSessionId, newClaudeSessionId, nameof(this.ClaudeSessionId));

    // Phase 03 flips this to true right after the first successful launch, deciding whether the
    // next launch passes --session-id (never started) or --resume (continuing) to claude.
    public bool HasBeenStarted { get; set; }

    public string WorkingDirectory
    {
        get => this.workingDirectory;
        set => this.SetField(ref this.workingDirectory, value);
    }

    public bool IsRunning => this.liveSession is not null;

    public TerminalSession? LiveSession => this.liveSession;

    public Brush StatusBrush => this.liveSession?.StatusBrush ?? DormantBrush;

    // A dormant node (never started, or detached after Stop) is hollow, same as a live
    // NotStarted/Exited TerminalSession - see MainWindow.xaml's StatusDot IsHollow trigger.
    public bool IsHollow => this.liveSession?.IsHollow ?? true;

    public bool IsStarting => this.liveSession?.State == SessionState.Starting;

    public SessionActivity Activity => this.liveSession?.Activity ?? SessionActivity.Idle;

    // Called when the tree selection lands on this session - no-ops for a dormant node.
    public void ClearLatchedActivity() => this.liveSession?.ClearLatchedActivity();

    // Persisted so a stopped/never-relaunched session still shows its last-known Claude-set title
    // instead of reverting to the tree name - see SessionRecord.DetectedTitle.
    public string? DetectedTitle
    {
        get => this.detectedTitle;
        set => this.SetField(ref this.detectedTitle, value);
    }

    public string DisplayTitle => this.liveSession?.DetectedTitle is { Length: > 0 } live
        ? live
        : this.detectedTitle is { Length: > 0 } persisted
            ? persisted
            : this.Name;

    public SessionValidationProblem ValidationProblem
    {
        get => this.validationProblem;
        set
        {
            if (this.validationProblem == value)
            {
                return;
            }

            this.validationProblem = value;
            this.RaisePropertyChanged(nameof(this.ValidationProblem));
            this.RaisePropertyChanged(nameof(this.IsInvalid));
            this.RaisePropertyChanged(nameof(this.ToolTipText));
        }
    }

    public bool IsInvalid => this.validationProblem != SessionValidationProblem.None;

    public string ToolTipText
    {
        get
        {
            var basePath = this.validationProblem switch
            {
                SessionValidationProblem.WorkingDirectoryMissing => $"Working directory not found: {this.WorkingDirectory}",
                SessionValidationProblem.ClaudeDataMissing => "Claude conversation data not found for this session.",
                _ => this.WorkingDirectory,
            };

            return this.liveSession is { } session ? $"{basePath} ({session.StatusText})" : basePath;
        }
    }

    public void AttachLiveSession(TerminalSession session)
    {
        this.liveSession = session;
        session.PropertyChanged += this.OnLiveSessionPropertyChanged;
        this.RaisePropertyChanged(nameof(this.IsRunning));
        this.RaisePropertyChanged(nameof(this.StatusBrush));
        this.RaisePropertyChanged(nameof(this.IsHollow));
        this.RaisePropertyChanged(nameof(this.IsStarting));
        this.RaisePropertyChanged(nameof(this.Activity));
        this.RaisePropertyChanged(nameof(this.ToolTipText));
        this.RaisePropertyChanged(nameof(this.DisplayTitle));
    }

    public void DetachLiveSession()
    {
        if (this.liveSession is not null)
        {
            this.liveSession.PropertyChanged -= this.OnLiveSessionPropertyChanged;
        }

        this.liveSession = null;
        this.RaisePropertyChanged(nameof(this.IsRunning));
        this.RaisePropertyChanged(nameof(this.StatusBrush));
        this.RaisePropertyChanged(nameof(this.IsHollow));
        this.RaisePropertyChanged(nameof(this.IsStarting));
        this.RaisePropertyChanged(nameof(this.Activity));
        this.RaisePropertyChanged(nameof(this.ToolTipText));
        this.RaisePropertyChanged(nameof(this.DisplayTitle));
    }

    private void OnLiveSessionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TerminalSession.StatusBrush))
        {
            this.RaisePropertyChanged(nameof(this.StatusBrush));
        }
        else if (e.PropertyName == nameof(TerminalSession.IsHollow))
        {
            this.RaisePropertyChanged(nameof(this.IsHollow));
        }
        else if (e.PropertyName == nameof(TerminalSession.StatusText))
        {
            this.RaisePropertyChanged(nameof(this.ToolTipText));
        }
        else if (e.PropertyName == nameof(TerminalSession.State))
        {
            this.RaisePropertyChanged(nameof(this.IsStarting));
        }
        else if (e.PropertyName == nameof(TerminalSession.Activity))
        {
            this.RaisePropertyChanged(nameof(this.Activity));
        }
        else if (e.PropertyName == nameof(TerminalSession.DetectedTitle) && this.liveSession is { } session)
        {
            // Mirror the live detection onto the persisted field, so it's still shown after this
            // session stops (or the app restarts) even before it's relaunched.
            this.DetectedTitle = session.DetectedTitle;
        }
    }
}
