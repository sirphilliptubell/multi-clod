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

    private SessionState state = SessionState.Starting;
    private string statusText = "Starting";
    private Brush statusBrush = StartingBrush;
    private bool isHollow;
    private string? detectedTitle;

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

    private void OnHostTitleChanged(object? sender, string title)
    {
        // Same cross-thread rationale as OnHostStateChanged below - ISessionHost.TitleChanged
        // fires from ConPtyConnection's output-pump thread.
        Application.Current.Dispatcher.BeginInvoke(() => this.DetectedTitle = title);
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
