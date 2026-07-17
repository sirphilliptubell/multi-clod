using System.IO;

namespace MultiClod.App.SessionLog.Tree;

/// <summary>
/// Watches for on-disk changes to a session's Tree-relevant files (the main transcript, every
/// already-known subagent transcript, and newly-appearing subagent files) without doing any parsing
/// or layout itself. Raises a single debounced <see cref="ChangesPending"/> event that
/// SessionTreeView turns into a "New entries - Refresh" banner (decision D7) - nothing here rebuilds
/// anything; that only happens when the user clicks Refresh. Plain, dependency-free C# (no WPF/
/// dispatcher coupling), matching TranscriptFileTailer/SubagentTranscriptWatcher's own convention -
/// the caller owns marshalling back to the UI thread.
/// </summary>
public sealed class TreeSnapshotWatcher : IDisposable
{
    private static readonly TimeSpan DebounceDelay = TimeSpan.FromMilliseconds(500);

    private readonly object gate = new();
    private readonly string mainFilePath;
    private readonly string subagentsDirectory;
    private readonly HashSet<string> knownAgentFilePaths;
    private FileSystemWatcher? mainFileWatcher;
    private FileSystemWatcher? subagentsWatcher;
    private SubagentTranscriptWatcher? newAgentWatcher;
    private Timer? debounceTimer;
    private bool disposed;

    // knownAgentFilePaths is the set of subagent files the just-built TreeGraph already reflects -
    // SubagentTranscriptWatcher (used below to catch brand-new agent files) always re-reports every
    // pre-existing file the moment it starts, which would otherwise fire a spurious "new entries"
    // banner immediately after every snapshot even though nothing actually changed since it was
    // built.
    public TreeSnapshotWatcher(string mainFilePath, string sessionDirectory, IReadOnlyCollection<string> knownAgentFilePaths)
    {
        this.mainFilePath = mainFilePath;
        this.subagentsDirectory = Path.Combine(sessionDirectory, "subagents");
        this.knownAgentFilePaths = new HashSet<string>(knownAgentFilePaths, StringComparer.OrdinalIgnoreCase);

        // Deferred for the same reason as TranscriptFileTailer/SubagentTranscriptWatcher's own
        // constructors: starting synchronously could raise ChangesPending before the caller has had
        // a chance to subscribe.
        ThreadPool.QueueUserWorkItem(_ => this.BeginWatching(sessionDirectory));
    }

    public event Action? ChangesPending;

    private void BeginWatching(string sessionDirectory)
    {
        lock (this.gate)
        {
            if (this.disposed)
            {
                return;
            }

            // Created before any watcher starts raising events, so ScheduleRaise can never run
            // against a not-yet-assigned timer - same fix as TranscriptFileTailer's constructor.
            this.debounceTimer = new Timer(_ => this.ChangesPending?.Invoke(), null, Timeout.Infinite, Timeout.Infinite);

            var mainDirectory = Path.GetDirectoryName(this.mainFilePath);
            if (mainDirectory is { Length: > 0 } && Directory.Exists(mainDirectory) && File.Exists(this.mainFilePath))
            {
                var watcher = new FileSystemWatcher(mainDirectory, Path.GetFileName(this.mainFilePath))
                {
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
                };
                watcher.Changed += (_, _) => this.ScheduleRaise();
                watcher.EnableRaisingEvents = true;
                this.mainFileWatcher = watcher;
            }

            if (Directory.Exists(this.subagentsDirectory))
            {
                var watcher = new FileSystemWatcher(this.subagentsDirectory, "agent-*.jsonl")
                {
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
                };
                watcher.Changed += (_, _) => this.ScheduleRaise();
                watcher.EnableRaisingEvents = true;
                this.subagentsWatcher = watcher;
            }

            var newAgentWatcher = new SubagentTranscriptWatcher(sessionDirectory);
            newAgentWatcher.SubagentDiscovered += source =>
            {
                if (!this.knownAgentFilePaths.Contains(source.FilePath))
                {
                    this.ScheduleRaise();
                }
            };
            this.newAgentWatcher = newAgentWatcher;
        }
    }

    private void ScheduleRaise()
    {
        lock (this.gate)
        {
            this.debounceTimer?.Change(DebounceDelay, Timeout.InfiniteTimeSpan);
        }
    }

    public void Dispose()
    {
        lock (this.gate)
        {
            this.disposed = true;
            this.mainFileWatcher?.Dispose();
            this.subagentsWatcher?.Dispose();
            this.newAgentWatcher?.Dispose();
            this.debounceTimer?.Dispose();
        }
    }
}
