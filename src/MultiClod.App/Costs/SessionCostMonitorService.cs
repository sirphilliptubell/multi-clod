using System.Collections.Concurrent;
using System.ComponentModel;
using System.IO;
using System.Windows;
using MultiClod.App.SessionLog;
using MultiClod.App.Validation;

namespace MultiClod.App.Costs;

/// <summary>
/// App-wide singleton (one instance, owned by MainWindow) that watches every started session's
/// main log + subagent logs and keeps each SessionNodeViewModel's cost badge up to date. Unlike
/// TranscriptFileTailer/SubagentTranscriptWatcher (one FileSystemWatcher-driven thread per open
/// transcript viewer), every registered session's file-changed events feed ONE shared queue drained
/// by a single background thread - this is meant to run continuously for every started session in
/// the tree, not just whichever one happens to have a viewer open, so centralizing avoids a
/// thread-and-watcher-per-session footprint.
///
/// Reuses TranscriptFileTailer's wait-for-file mechanics (via DirectoryExistenceWatcher) for the
/// main log, and SubagentTranscriptWatcher's wait-for-subagents-directory + agent-*.jsonl discovery
/// for subagent logs - but collapses subagent watching to a single directory FileSystemWatcher per
/// session instead of one per file, since NotifyFilters.LastWrite already covers both new files and
/// growth of existing ones.
/// </summary>
public sealed class SessionCostMonitorService : IDisposable
{
    private static readonly TimeSpan DebounceDelay = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan BackstopPollInterval = TimeSpan.FromSeconds(2);

    private readonly object gate = new();
    private readonly Dictionary<Guid, WatchedSession> watchedSessions = new();
    private readonly BlockingCollection<QueueItem> queue = new();
    private readonly Thread drainThread;
    private readonly Timer backstopTimer;
    private bool disposed;

    public SessionCostMonitorService()
    {
        this.drainThread = new Thread(this.DrainLoop) { IsBackground = true, Name = "SessionCostMonitor" };
        this.drainThread.Start();

        // Insurance against a missed/coalesced FileSystemWatcher event - re-checks every currently
        // watched path on a fixed interval, same rationale (and matching interval) as
        // TranscriptFileTailer's own backstop timer.
        this.backstopTimer = new Timer(_ => this.EnqueueAllWatchedPaths(), null, BackstopPollInterval, BackstopPollInterval);
    }

    // Raised whenever a subagent log file's own cost changes - SessionLogWindow subscribes while
    // open to keep each subagent list entry's own total current. Not raised for the main log path;
    // the main log's contribution is folded into SessionNodeViewModel.CostSummary directly, which
    // SessionLogWindow's "Main Session" entry mirrors from the node itself.
    internal event Action<string, SessionCostSummary>? SubagentFileCostUpdated;

    public void RegisterSession(SessionNodeViewModel node)
    {
        lock (this.gate)
        {
            if (this.disposed || this.watchedSessions.ContainsKey(node.Id))
            {
                return;
            }

            var watched = new WatchedSession(node, this.Enqueue);
            this.watchedSessions[node.Id] = watched;
            watched.Start();
        }
    }

    public void UnregisterSession(SessionNodeViewModel node)
    {
        WatchedSession? watched;
        lock (this.gate)
        {
            if (!this.watchedSessions.Remove(node.Id, out watched))
            {
                return;
            }
        }

        watched.Dispose();
    }

    public void Dispose()
    {
        lock (this.gate)
        {
            if (this.disposed)
            {
                return;
            }

            this.disposed = true;
        }

        this.backstopTimer.Dispose();
        this.queue.CompleteAdding();
        this.drainThread.Join();

        List<WatchedSession> toDispose;
        lock (this.gate)
        {
            toDispose = this.watchedSessions.Values.ToList();
            this.watchedSessions.Clear();
        }

        foreach (var watched in toDispose)
        {
            watched.Dispose();
        }

        this.queue.Dispose();
    }

    private void Enqueue(Guid sessionId, string filePath, int generation)
    {
        if (!this.queue.IsAddingCompleted)
        {
            try
            {
                this.queue.Add(new QueueItem(sessionId, filePath, generation));
            }
            catch (InvalidOperationException)
            {
                // Lost a race with Dispose's CompleteAdding - safe to drop, the service is shutting
                // down anyway.
            }
        }
    }

    private void EnqueueAllWatchedPaths()
    {
        List<WatchedSession> sessions;
        lock (this.gate)
        {
            if (this.disposed)
            {
                return;
            }

            sessions = this.watchedSessions.Values.ToList();
        }

        foreach (var watched in sessions)
        {
            watched.EnqueueAllKnownPaths();
        }
    }

    private void DrainLoop()
    {
        foreach (var item in this.queue.GetConsumingEnumerable())
        {
            WatchedSession? watched;
            lock (this.gate)
            {
                this.watchedSessions.TryGetValue(item.SessionId, out watched);
            }

            watched?.ProcessFileChanged(item.FilePath, item.Generation, this.SubagentFileCostUpdated);
        }
    }

    private readonly record struct QueueItem(Guid SessionId, string FilePath, int Generation);

    // Everything this service tracks for one registered session: its watchers (main log + subagent
    // directory), the per-file cost dictionaries feeding its aggregate, and the debounce timers
    // coalescing rapid file-changed events. All mutable state here is protected by `gate` except
    // `perFileCosts`, which only the (single) drain thread ever touches - see ProcessFileChanged.
    private sealed class WatchedSession : IDisposable
    {
        private readonly SessionNodeViewModel node;
        private readonly Action<Guid, string, int> enqueue;
        private readonly object gate = new();
        private readonly Dictionary<string, Timer> debounceTimers = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> knownSubagentPaths = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, IReadOnlyDictionary<string, decimal?>> perFileCosts = new(StringComparer.OrdinalIgnoreCase);

        private string mainLogPath;
        private string subagentsDirectory;
        private int generation;
        private bool disposed;

        private DirectoryExistenceWatcher? mainLogWaitingWatcher;
        private FileSystemWatcher? mainLogFileWatcher;
        private DirectoryExistenceWatcher? subagentsWaitingWatcher;
        private FileSystemWatcher? subagentsFileWatcher;

        public WatchedSession(SessionNodeViewModel node, Action<Guid, string, int> enqueue)
        {
            this.node = node;
            this.enqueue = enqueue;
            this.mainLogPath = ClaudeProjectPath.GetSessionFilePath(node.WorkingDirectory, node.ClaudeSessionId);
            this.subagentsDirectory = ComputeSubagentsDirectory(this.mainLogPath, node.ClaudeSessionId);

            node.PropertyChanged += this.OnNodePropertyChanged;
        }

        public void Start() =>
            // Deferred rather than called inline, same reasoning as TranscriptFileTailer's own
            // constructor: BeginWaitingForMainLog can synchronously reach all the way through to an
            // immediate initial ProcessFileChanged if the file already exists, and queuing avoids
            // any ordering surprise from that running before RegisterSession has returned.
            ThreadPool.QueueUserWorkItem(_ =>
            {
                this.BeginWaitingForMainLog(this.generation);
                this.BeginWaitingForSubagents(this.generation);
            });

        public void EnqueueAllKnownPaths()
        {
            List<string> paths;
            int currentGeneration;
            lock (this.gate)
            {
                if (this.disposed)
                {
                    return;
                }

                currentGeneration = this.generation;
                paths = [this.mainLogPath, .. this.knownSubagentPaths];
            }

            foreach (var path in paths)
            {
                this.enqueue(this.node.Id, path, currentGeneration);
            }
        }

        // Runs on the single shared drain thread only - perFileCosts is deliberately unsynchronized
        // since nothing else ever touches it.
        public void ProcessFileChanged(string filePath, int itemGeneration, Action<string, SessionCostSummary>? subagentUpdated)
        {
            string currentMainLogPath;
            lock (this.gate)
            {
                if (this.disposed || itemGeneration != this.generation)
                {
                    // Stale event from before a ClaudeSessionId change (e.g. /clear) - the session
                    // has already moved on to a different set of files; this path no longer belongs
                    // to the current generation, so applying it would pollute perFileCosts with a
                    // no-longer-relevant entry.
                    return;
                }

                currentMainLogPath = this.mainLogPath;
            }

            var costs = SessionCostFileProcessor.ProcessFile(filePath);
            this.perFileCosts[filePath] = costs;

            var summary = SessionCostAggregator.Aggregate(this.perFileCosts.Values);
            DispatchToUi(() => this.node.UpdateCostSummary(summary));

            if (!string.Equals(filePath, currentMainLogPath, StringComparison.OrdinalIgnoreCase))
            {
                var subagentSummary = SessionCostAggregator.Aggregate([costs]);
                DispatchToUi(() => subagentUpdated?.Invoke(filePath, subagentSummary));
            }
        }

        public void Dispose()
        {
            lock (this.gate)
            {
                this.disposed = true;
                this.node.PropertyChanged -= this.OnNodePropertyChanged;
                this.TearDownWatchersLocked();
            }
        }

        private void OnNodePropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(SessionNodeViewModel.ClaudeSessionId))
            {
                return;
            }

            int newGeneration;
            lock (this.gate)
            {
                if (this.disposed)
                {
                    return;
                }

                this.TearDownWatchersLocked();
                this.knownSubagentPaths.Clear();
                this.mainLogPath = ClaudeProjectPath.GetSessionFilePath(this.node.WorkingDirectory, this.node.ClaudeSessionId);
                this.subagentsDirectory = ComputeSubagentsDirectory(this.mainLogPath, this.node.ClaudeSessionId);
                newGeneration = ++this.generation;
            }

            this.perFileCosts.Clear();
            DispatchToUi(() => this.node.UpdateCostSummary(SessionCostSummary.NoData));

            ThreadPool.QueueUserWorkItem(_ =>
            {
                this.BeginWaitingForMainLog(newGeneration);
                this.BeginWaitingForSubagents(newGeneration);
            });
        }

        private void BeginWaitingForMainLog(int startedForGeneration)
        {
            lock (this.gate)
            {
                if (this.disposed || startedForGeneration != this.generation)
                {
                    return;
                }

                var directory = Path.GetDirectoryName(this.mainLogPath);
                if (directory is { Length: > 0 } && !Directory.Exists(directory))
                {
                    this.mainLogWaitingWatcher = DirectoryExistenceWatcher.ForDirectory(directory, () => this.OnMainLogDirectoryExists(startedForGeneration));
                    return;
                }
            }

            this.OnMainLogDirectoryExists(startedForGeneration);
        }

        private void OnMainLogDirectoryExists(int startedForGeneration)
        {
            string path;
            lock (this.gate)
            {
                if (this.disposed || startedForGeneration != this.generation)
                {
                    return;
                }

                this.mainLogWaitingWatcher?.Dispose();
                this.mainLogWaitingWatcher = null;
                path = this.mainLogPath;

                if (!File.Exists(path))
                {
                    this.mainLogWaitingWatcher = DirectoryExistenceWatcher.ForFile(path, () => this.OnMainLogFileExists(startedForGeneration));
                    return;
                }
            }

            this.OnMainLogFileExists(startedForGeneration);
        }

        private void OnMainLogFileExists(int startedForGeneration)
        {
            lock (this.gate)
            {
                if (this.disposed || startedForGeneration != this.generation)
                {
                    return;
                }

                this.mainLogWaitingWatcher?.Dispose();
                this.mainLogWaitingWatcher = null;

                var directory = Path.GetDirectoryName(this.mainLogPath)!;
                var fileName = Path.GetFileName(this.mainLogPath);

                var watcher = new FileSystemWatcher(directory, fileName) { NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size };
                watcher.Changed += (_, _) => this.ScheduleEnqueue(this.mainLogPath, startedForGeneration);
                watcher.Created += (_, _) => this.ScheduleEnqueue(this.mainLogPath, startedForGeneration);
                watcher.EnableRaisingEvents = true;
                this.mainLogFileWatcher = watcher;
            }

            // Eager: compute this session's cost immediately once its main log is watchable,
            // rather than waiting for the first FileSystemWatcher event or backstop tick.
            this.enqueue(this.node.Id, this.mainLogPath, startedForGeneration);
        }

        private void BeginWaitingForSubagents(int startedForGeneration)
        {
            string subagentsDir;
            lock (this.gate)
            {
                if (this.disposed || startedForGeneration != this.generation)
                {
                    return;
                }

                subagentsDir = this.subagentsDirectory;

                if (!Directory.Exists(subagentsDir))
                {
                    this.subagentsWaitingWatcher = DirectoryExistenceWatcher.ForDirectory(subagentsDir, () => this.OnSubagentsDirectoryExists(startedForGeneration));
                    return;
                }
            }

            this.OnSubagentsDirectoryExists(startedForGeneration);
        }

        private void OnSubagentsDirectoryExists(int startedForGeneration)
        {
            string subagentsDir;
            List<string> existingPaths;

            lock (this.gate)
            {
                if (this.disposed || startedForGeneration != this.generation)
                {
                    return;
                }

                this.subagentsWaitingWatcher?.Dispose();
                this.subagentsWaitingWatcher = null;
                subagentsDir = this.subagentsDirectory;

                existingPaths = Directory.Exists(subagentsDir)
                    ? Directory.EnumerateFiles(subagentsDir, "agent-*.jsonl").ToList()
                    : [];

                foreach (var path in existingPaths)
                {
                    this.knownSubagentPaths.Add(path);
                }

                var watcher = new FileSystemWatcher(subagentsDir, "agent-*.jsonl")
                {
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
                };
                watcher.Changed += (_, e) => this.OnSubagentFileChanged(e.FullPath, startedForGeneration);
                watcher.Created += (_, e) => this.OnSubagentFileChanged(e.FullPath, startedForGeneration);
                watcher.EnableRaisingEvents = true;
                this.subagentsFileWatcher = watcher;
            }

            // Eager: every subagent file discovered up front gets an immediate initial check.
            foreach (var path in existingPaths)
            {
                this.enqueue(this.node.Id, path, startedForGeneration);
            }
        }

        private void OnSubagentFileChanged(string path, int eventGeneration)
        {
            lock (this.gate)
            {
                if (this.disposed || eventGeneration != this.generation)
                {
                    return;
                }

                this.knownSubagentPaths.Add(path);
            }

            this.ScheduleEnqueue(path, eventGeneration);
        }

        private void ScheduleEnqueue(string filePath, int eventGeneration)
        {
            lock (this.gate)
            {
                if (this.disposed || eventGeneration != this.generation)
                {
                    return;
                }

                if (this.debounceTimers.TryGetValue(filePath, out var existing))
                {
                    existing.Change(DebounceDelay, Timeout.InfiniteTimeSpan);
                    return;
                }

                var timer = new Timer(_ => this.enqueue(this.node.Id, filePath, eventGeneration), null, DebounceDelay, Timeout.InfiniteTimeSpan);
                this.debounceTimers[filePath] = timer;
            }
        }

        // Caller must already hold `gate`.
        private void TearDownWatchersLocked()
        {
            this.mainLogWaitingWatcher?.Dispose();
            this.mainLogWaitingWatcher = null;
            this.mainLogFileWatcher?.Dispose();
            this.mainLogFileWatcher = null;
            this.subagentsWaitingWatcher?.Dispose();
            this.subagentsWaitingWatcher = null;
            this.subagentsFileWatcher?.Dispose();
            this.subagentsFileWatcher = null;

            foreach (var timer in this.debounceTimers.Values)
            {
                timer.Dispose();
            }

            this.debounceTimers.Clear();
        }

        // Same folder shape SessionLogWindow already computes: <projectDir>/<claudeSessionId>/subagents.
        private static string ComputeSubagentsDirectory(string mainLogPath, Guid claudeSessionId)
        {
            var projectDir = Path.GetDirectoryName(mainLogPath)!;
            var sessionDir = Path.Combine(projectDir, claudeSessionId.ToString());
            return Path.Combine(sessionDir, "subagents");
        }

        private static void DispatchToUi(Action action) =>
            Application.Current?.Dispatcher.BeginInvoke(action);
    }
}
