using System.IO;

namespace MultiClod.App.SessionLog;

/// <summary>
/// Live-discovers a session's subagent transcripts under &lt;sessionDir&gt;/subagents/agent-*.jsonl.
/// Raises <see cref="SubagentDiscovered"/> once per file - pre-existing files first (in ascending
/// creation-time order), then any created afterwards - so a caller can maintain its own UI-bound,
/// sorted collection via ordered insert rather than a clear+rebuild. Uses the same "wait for
/// directory, then watch it" approach as TranscriptFileTailer (via DirectoryExistenceWatcher),
/// since a session with no subagents yet has no subagents/ directory at all. Plain, dependency-free
/// C# - no WPF/dispatcher coupling - so the caller owns marshalling back to the UI thread, same as
/// TranscriptFileTailer.
/// </summary>
public sealed class SubagentTranscriptWatcher : IDisposable
{
    private readonly string subagentsDirectory;
    private readonly object gate = new();
    private readonly HashSet<string> knownPaths = new(StringComparer.OrdinalIgnoreCase);
    private DirectoryExistenceWatcher? waitingWatcher;
    private FileSystemWatcher? fileSystemWatcher;
    private bool disposed;

    public SubagentTranscriptWatcher(string sessionDirectory)
    {
        this.subagentsDirectory = Path.Combine(sessionDirectory, "subagents");
        this.BeginWaiting();
    }

    public event Action<SessionLogSourceViewModel>? SubagentDiscovered;

    private void BeginWaiting()
    {
        if (Directory.Exists(this.subagentsDirectory))
        {
            this.OnDirectoryExists();
            return;
        }

        this.waitingWatcher = DirectoryExistenceWatcher.ForDirectory(this.subagentsDirectory, this.OnDirectoryExists);
    }

    private void OnDirectoryExists()
    {
        List<SessionLogSourceViewModel> existing;

        lock (this.gate)
        {
            if (this.disposed)
            {
                return;
            }

            this.waitingWatcher?.Dispose();
            this.waitingWatcher = null;

            existing = Directory.EnumerateFiles(this.subagentsDirectory, "agent-*.jsonl")
                .Select(TryDescribe)
                .Where(source => source is not null)
                .Select(source => source!)
                .OrderBy(source => source.CreatedAtUtc)
                .ToList();

            foreach (var source in existing)
            {
                this.knownPaths.Add(source.FilePath);
            }

            var watcher = new FileSystemWatcher(this.subagentsDirectory, "agent-*.jsonl") { NotifyFilter = NotifyFilters.FileName };
            watcher.Created += (_, e) => this.OnFileCreated(e.FullPath);
            watcher.EnableRaisingEvents = true;
            this.fileSystemWatcher = watcher;
        }

        foreach (var source in existing)
        {
            this.SubagentDiscovered?.Invoke(source);
        }
    }

    private void OnFileCreated(string path)
    {
        SessionLogSourceViewModel? source;
        lock (this.gate)
        {
            if (this.disposed || !this.knownPaths.Add(path))
            {
                return;
            }

            source = TryDescribe(path);
        }

        if (source is not null)
        {
            this.SubagentDiscovered?.Invoke(source);
        }
    }

    private static SessionLogSourceViewModel? TryDescribe(string path)
    {
        try
        {
            return new SessionLogSourceViewModel(Path.GetFileNameWithoutExtension(path), path, File.GetCreationTimeUtc(path));
        }
        catch (IOException)
        {
            return null;
        }
    }

    public void Dispose()
    {
        lock (this.gate)
        {
            this.disposed = true;
            this.waitingWatcher?.Dispose();
            this.fileSystemWatcher?.Dispose();
        }
    }
}
