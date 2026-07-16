using System.IO;

namespace MultiClod.App.SessionLog;

/// <summary>
/// Polls a path at a fixed interval, invoking a callback exactly once the first time it exists.
/// Used by TranscriptFileTailer (waiting for a never-started session's project directory, then its
/// transcript file) and SubagentTranscriptWatcher (waiting for a session's subagents directory) -
/// both need cheap "not created yet" polling before switching to a FileSystemWatcher, since FSW
/// can't watch a path that doesn't exist yet.
/// </summary>
public sealed class DirectoryExistenceWatcher : IDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);

    private readonly Func<bool> existsCheck;
    private readonly Action onExists;
    private readonly object gate = new();
    private readonly Timer timer;
    private bool fired;

    private DirectoryExistenceWatcher(Func<bool> existsCheck, Action onExists)
    {
        this.existsCheck = existsCheck;
        this.onExists = onExists;

        // Constructed with an infinite due time and only started via Change() afterwards, so the
        // `this.timer` field assignment always completes before Poll's first possible callback -
        // Poll disposes `this.timer` when it fires, which would race a still-in-progress
        // constructor if the timer could fire immediately.
        this.timer = new Timer(this.Poll, state: null, Timeout.Infinite, Timeout.Infinite);
        this.timer.Change(TimeSpan.Zero, PollInterval);
    }

    public static DirectoryExistenceWatcher ForDirectory(string path, Action onExists) =>
        new(() => Directory.Exists(path), onExists);

    public static DirectoryExistenceWatcher ForFile(string path, Action onExists) =>
        new(() => File.Exists(path), onExists);

    private void Poll(object? state)
    {
        lock (this.timer)
        {
            if (this.fired || !this.existsCheck())
            {
                return;
            }

            this.fired = true;
        }

        this.timer.Dispose();
        this.onExists();
    }

    public void Dispose() => this.timer.Dispose();
}
