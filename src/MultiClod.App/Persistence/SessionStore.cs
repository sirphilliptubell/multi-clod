using System.IO;
using System.Text.Json;
using System.Threading;

namespace MultiClod.App.Persistence;

/// <summary>
/// Owns sessions.json: loading (with fallback through backups on corruption), debounced saving,
/// and backup rotation. Has no WPF dependency - file I/O doesn't need a Dispatcher, and keeping
/// this class UI-free is what lets <c>MultiClod.App.Tests</c> exercise it directly.
/// </summary>
public sealed class SessionStore
{
    public const int CurrentVersion = 0;

    private const int MaxBackups = 10;
    private const int DebounceMilliseconds = 400;
    private const string BackupFilePrefix = "sessions-";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly string dataDirectory;
    private readonly string sessionsFilePath;
    private readonly string backupsDirectory;
    private readonly object fileLock = new();
    private readonly Timer debounceTimer;

    private SessionsFile? pendingSnapshot;

    public SessionStore(string? dataDirectoryOverride = null)
    {
        // dataDirectoryOverride exists only so tests/manual harnesses can point at a scratch
        // folder instead of the real ~/.multi-clod.
        this.dataDirectory = dataDirectoryOverride ?? MultiClodDataDirectory.Root;
        this.sessionsFilePath = Path.Combine(this.dataDirectory, "sessions.json");
        this.backupsDirectory = Path.Combine(this.dataDirectory, "sessions");

        // Dormant until the first ScheduleSave (Timeout.Infinite due time means "don't start yet").
        this.debounceTimer = new Timer(this.OnDebounceElapsed, null, Timeout.Infinite, Timeout.Infinite);
    }

    /// <summary>
    /// Loads sessions.json, falling back through backups (newest first) on corruption, and
    /// finally an empty tree if everything is unreadable - which also naturally covers first run
    /// (no file at all yet).
    /// </summary>
    public SessionsFile Load()
    {
        Directory.CreateDirectory(this.dataDirectory);
        Directory.CreateDirectory(this.backupsDirectory);

        var primary = this.TryLoadFile(this.sessionsFilePath);
        if (primary is not null)
        {
            return primary;
        }

        foreach (var backupPath in this.EnumerateBackupsNewestFirst())
        {
            var candidate = this.TryLoadFile(backupPath);
            if (candidate is not null)
            {
                return candidate;
            }
        }

        return new SessionsFile();
    }

    /// <summary>
    /// Schedules a write a short debounce window from now, collapsing rapid successive mutations
    /// (e.g. several drag-drop reparents in a row) into a single write.
    /// </summary>
    public void ScheduleSave(SessionsFile snapshot)
    {
        lock (this.fileLock)
        {
            this.pendingSnapshot = snapshot;
            this.debounceTimer.Change(DebounceMilliseconds, Timeout.Infinite);
        }
    }

    /// <summary>
    /// Writes any pending save immediately instead of waiting out the debounce window. Called
    /// from MainWindow.OnClosing so a mutation right before exit isn't lost.
    /// </summary>
    public void Flush()
    {
        this.debounceTimer.Change(Timeout.Infinite, Timeout.Infinite);

        SessionsFile? toWrite;
        lock (this.fileLock)
        {
            toWrite = this.pendingSnapshot;
            this.pendingSnapshot = null;
        }

        if (toWrite is not null)
        {
            this.WriteWithBackupRotation(toWrite);
        }
    }

    private void OnDebounceElapsed(object? state)
    {
        SessionsFile? toWrite;
        lock (this.fileLock)
        {
            toWrite = this.pendingSnapshot;
            this.pendingSnapshot = null;
        }

        if (toWrite is not null)
        {
            this.WriteWithBackupRotation(toWrite);
        }
    }

    private SessionsFile? TryLoadFile(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var file = JsonSerializer.Deserialize<SessionsFile>(File.ReadAllText(path), JsonOptions);

            // A newer app version's file (Version > CurrentVersion) is treated as unreadable
            // rather than guessed at; a future version bump adds a translation step here instead.
            return file is { Version: CurrentVersion } ? file : null;
        }
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private void WriteWithBackupRotation(SessionsFile file)
    {
        lock (this.fileLock)
        {
            try
            {
                Directory.CreateDirectory(this.dataDirectory);
                Directory.CreateDirectory(this.backupsDirectory);

                if (File.Exists(this.sessionsFilePath))
                {
                    File.Copy(this.sessionsFilePath, this.NextBackupPath(DateTime.Now));
                }

                var json = JsonSerializer.Serialize(file, JsonOptions);
                var tmpPath = this.sessionsFilePath + ".tmp";
                File.WriteAllText(tmpPath, json);

                // Write-then-move instead of writing sessions.json directly, so a crash mid-write
                // never leaves a half-written (unparseable) primary file on disk.
                File.Move(tmpPath, this.sessionsFilePath, overwrite: true);

                this.PruneOldBackups();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Best-effort persistence: a locked file or full disk shouldn't crash the app.
                // The next mutation's debounced save (or the next Flush) will simply retry.
            }
        }
    }

    private string NextBackupPath(DateTime now)
    {
        var stamp = now.ToString("yyyyMMdd-HHmmss");
        var basePath = Path.Combine(this.backupsDirectory, $"{BackupFilePrefix}{stamp}.json");
        if (!File.Exists(basePath))
        {
            return basePath;
        }

        var counter = 1;
        string candidate;
        do
        {
            candidate = Path.Combine(this.backupsDirectory, $"{BackupFilePrefix}{stamp}-{counter}.json");
            counter++;
        }
        while (File.Exists(candidate));

        return candidate;
    }

    private void PruneOldBackups()
    {
        var stale = this.EnumerateBackupsNewestFirst().Skip(MaxBackups);
        foreach (var path in stale)
        {
            try
            {
                File.Delete(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    private IEnumerable<string> EnumerateBackupsNewestFirst()
    {
        if (!Directory.Exists(this.backupsDirectory))
        {
            return [];
        }

        // The zero-padded "yyyyMMdd-HHmmss[-N]" filename format sorts lexically == chronologically.
        return Directory.GetFiles(this.backupsDirectory, $"{BackupFilePrefix}*.json")
            .OrderByDescending(f => f, StringComparer.Ordinal);
    }
}
