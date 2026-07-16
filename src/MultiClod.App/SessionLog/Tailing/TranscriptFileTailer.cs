using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace MultiClod.App.SessionLog.Tailing;

/// <summary>
/// State machine a TranscriptFileTailer walks through - the first two states cover the
/// never-started-session case (its project directory, or the transcript file itself, doesn't
/// exist yet), using cheap polling via DirectoryExistenceWatcher; only Tailing uses a
/// FileSystemWatcher.
/// </summary>
public enum TranscriptTailerState
{
    WaitingForDirectory,
    WaitingForFile,
    Tailing,
}

/// <summary>
/// Live-tails one JSONL file from byte offset 0 onward, emitting only complete ('\n'-terminated)
/// lines - a line that's mid-write when read is left buffered for the next pass rather than
/// emitted truncated. FileSystemWatcher (debounced) drives normal updates once tailing; a coarse
/// poll is a backstop for FSW's documented event-coalescing/overflow gaps under write bursts. Must
/// tolerate the separate `claude` CLI process holding the file open for write (FileShare.ReadWrite).
/// Plain, dependency-free C# by design - no WPF/dispatcher coupling - so callers (e.g.
/// TranscriptViewerControl) own marshalling events back to the UI thread.
/// </summary>
public sealed class TranscriptFileTailer : IDisposable
{
    private const int MaxConsecutiveIoFailures = 5;
    private static readonly TimeSpan DebounceDelay = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan BackstopPollInterval = TimeSpan.FromSeconds(2);

    private readonly string filePath;
    private readonly object gate = new();
    private readonly List<byte> pendingBytes = new();

    private DirectoryExistenceWatcher? waitingWatcher;
    private FileSystemWatcher? fileSystemWatcher;
    private Timer? debounceTimer;
    private Timer? backstopTimer;
    private long offset;
    private int consecutiveIoFailures;
    private bool disposed;

    public TranscriptFileTailer(string filePath)
    {
        this.filePath = filePath;

        // Deferred rather than called inline: BeginWaiting can synchronously reach all the way
        // through to the first ReadNewLines() call if the file already exists, which would raise
        // LinesAvailable before the constructor even returns - before the caller has had any
        // chance to subscribe. Queuing it ensures every event always has a subscriber listening.
        ThreadPool.QueueUserWorkItem(_ => this.BeginWaiting());
    }

    public event Action<TranscriptTailerState>? StateChanged;

    public event Action<IReadOnlyList<string>>? LinesAvailable;

    // Raised with true once MaxConsecutiveIoFailures consecutive read attempts fail, and with
    // false the next time a read succeeds - lets a viewer show/hide a "lost access, retrying"
    // status row without treating one transient lock (expected - the CLI holds this file open for
    // write) as an error.
    public event Action<bool>? AccessProblemChanged;

    public TranscriptTailerState State { get; private set; } = TranscriptTailerState.WaitingForDirectory;

    private void BeginWaiting()
    {
        var directory = Path.GetDirectoryName(this.filePath);
        if (directory is { Length: > 0 } && !Directory.Exists(directory))
        {
            this.SetState(TranscriptTailerState.WaitingForDirectory);
            this.waitingWatcher = DirectoryExistenceWatcher.ForDirectory(directory, this.OnDirectoryExists);
            return;
        }

        this.OnDirectoryExists();
    }

    private void OnDirectoryExists()
    {
        this.waitingWatcher?.Dispose();
        this.waitingWatcher = null;

        if (!File.Exists(this.filePath))
        {
            this.SetState(TranscriptTailerState.WaitingForFile);
            this.waitingWatcher = DirectoryExistenceWatcher.ForFile(this.filePath, this.OnFileExists);
            return;
        }

        this.OnFileExists();
    }

    private void OnFileExists()
    {
        this.waitingWatcher?.Dispose();
        this.waitingWatcher = null;

        // StartTailing() (which constructs the FileSystemWatcher) must run before SetState raises
        // StateChanged - a listener reacting to the Tailing state (e.g. disposing this tailer and
        // deleting its directory, as tests do) can otherwise race ahead of the watcher's own
        // construction and crash it with a "directory no longer exists" error.
        this.StartTailing();
        this.SetState(TranscriptTailerState.Tailing);
    }

    private void StartTailing()
    {
        var directory = Path.GetDirectoryName(this.filePath)!;
        var fileName = Path.GetFileName(this.filePath);

        // Debounce/backstop timers are created (infinite due time) before the FileSystemWatcher
        // starts raising events, so ScheduleRead can never run against a not-yet-assigned timer.
        this.debounceTimer = new Timer(_ => this.ReadNewLines(), null, Timeout.Infinite, Timeout.Infinite);
        this.backstopTimer = new Timer(_ => this.ReadNewLines(), null, BackstopPollInterval, BackstopPollInterval);

        var watcher = new FileSystemWatcher(directory, fileName) { NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size };
        watcher.Changed += (_, _) => this.ScheduleRead();
        watcher.Created += (_, _) => this.ScheduleRead();
        watcher.EnableRaisingEvents = true;
        this.fileSystemWatcher = watcher;

        this.ReadNewLines();
    }

    private void ScheduleRead()
    {
        lock (this.gate)
        {
            this.debounceTimer?.Change(DebounceDelay, Timeout.InfiniteTimeSpan);
        }
    }

    private void ReadNewLines()
    {
        lock (this.gate)
        {
            if (this.disposed)
            {
                return;
            }

            try
            {
                using var stream = new FileStream(this.filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

                // The file was truncated or replaced entirely (e.g. /clear rewrites it) - restart
                // from the top rather than seeking past the end.
                if (stream.Length < this.offset)
                {
                    this.offset = 0;
                    this.pendingBytes.Clear();
                }

                stream.Seek(this.offset, SeekOrigin.Begin);
                using var buffer = new MemoryStream();
                stream.CopyTo(buffer);
                var newBytes = buffer.ToArray();
                this.offset += newBytes.Length;
                this.pendingBytes.AddRange(newBytes);

                var completeLines = this.ExtractCompleteLines();

                this.consecutiveIoFailures = 0;
                this.AccessProblemChanged?.Invoke(false);

                if (completeLines.Count > 0)
                {
                    this.LinesAvailable?.Invoke(completeLines);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                this.consecutiveIoFailures++;
                if (this.consecutiveIoFailures == MaxConsecutiveIoFailures)
                {
                    this.AccessProblemChanged?.Invoke(true);
                }
            }
        }
    }

    // Splits pendingBytes on '\n' (ASCII, so it never falls inside a multi-byte UTF-8 sequence),
    // keeping any trailing torn line buffered for the next pass instead of emitting it truncated.
    private List<string> ExtractCompleteLines()
    {
        var lines = new List<string>();
        var lineStart = 0;

        for (var i = 0; i < this.pendingBytes.Count; i++)
        {
            if (this.pendingBytes[i] != (byte)'\n')
            {
                continue;
            }

            var length = i - lineStart;
            if (length > 0 && this.pendingBytes[lineStart + length - 1] == (byte)'\r')
            {
                length--;
            }

            if (length > 0)
            {
                lines.Add(Encoding.UTF8.GetString(CollectionsMarshal.AsSpan(this.pendingBytes).Slice(lineStart, length)));
            }

            lineStart = i + 1;
        }

        this.pendingBytes.RemoveRange(0, lineStart);
        return lines;
    }

    private void SetState(TranscriptTailerState state)
    {
        this.State = state;
        this.StateChanged?.Invoke(state);
    }

    public void Dispose()
    {
        lock (this.gate)
        {
            this.disposed = true;
            this.waitingWatcher?.Dispose();
            this.fileSystemWatcher?.Dispose();
            this.debounceTimer?.Dispose();
            this.backstopTimer?.Dispose();
        }
    }
}
