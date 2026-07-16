using MultiClod.App.SessionLog.Tailing;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace MultiClod.App.Tests;

public sealed class TranscriptFileTailerTests
{
    [Test]
    public async Task Tailer_FileAlreadyExists_ReadsExistingLinesImmediately()
    {
        var path = CreateScratchFile();
        try
        {
            File.WriteAllText(path, "line one\nline two\n");
            var received = new List<string>();
            var gotLines = new TaskCompletionSource();

            using var tailer = new TranscriptFileTailer(path);
            tailer.LinesAvailable += lines =>
            {
                lock (received)
                {
                    received.AddRange(lines);
                }

                gotLines.TrySetResult();
            };

            await WaitOrTimeout(gotLines.Task);

            List<string> snapshot;
            lock (received)
            {
                snapshot = received.ToList();
            }

            await Assert.That(snapshot).IsEquivalentTo(["line one", "line two"]);
        }
        finally
        {
            DeleteScratchFile(path);
        }
    }

    [Test]
    public async Task Tailer_AppendedLineAfterInitialRead_IsEmittedExactlyOnce()
    {
        var path = CreateScratchFile();
        try
        {
            File.WriteAllText(path, "first\n");
            var received = new List<string>();
            var sawSecond = new TaskCompletionSource();

            using var tailer = new TranscriptFileTailer(path);
            tailer.LinesAvailable += lines =>
            {
                lock (received)
                {
                    received.AddRange(lines);
                }

                if (lines.Contains("second"))
                {
                    sawSecond.TrySetResult();
                }
            };

            await Task.Delay(300);
            File.AppendAllText(path, "second\n");

            await WaitOrTimeout(sawSecond.Task);

            List<string> snapshot;
            lock (received)
            {
                snapshot = received.ToList();
            }

            await Assert.That(snapshot.Count(l => l == "second")).IsEqualTo(1);
            await Assert.That(snapshot).Contains("first");
        }
        finally
        {
            DeleteScratchFile(path);
        }
    }

    [Test]
    public async Task Tailer_TornLineMidWrite_IsNotEmittedUntilNewlineArrives()
    {
        var path = CreateScratchFile();
        try
        {
            File.WriteAllText(path, string.Empty);
            var received = new List<string>();

            using var tailer = new TranscriptFileTailer(path);
            tailer.LinesAvailable += lines =>
            {
                lock (received)
                {
                    received.AddRange(lines);
                }
            };

            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite))
            using (var writer = new StreamWriter(stream))
            {
                writer.Write("partial-line-no-newline-yet");
                writer.Flush();

                await Task.Delay(300);

                List<string> midSnapshot;
                lock (received)
                {
                    midSnapshot = received.ToList();
                }

                await Assert.That(midSnapshot).IsEmpty();

                writer.Write("-now-complete\n");
                writer.Flush();
            }

            await WaitUntil(() =>
            {
                lock (received)
                {
                    return received.Count > 0;
                }
            });

            List<string> finalSnapshot;
            lock (received)
            {
                finalSnapshot = received.ToList();
            }

            await Assert.That(finalSnapshot).IsEquivalentTo(["partial-line-no-newline-yet-now-complete"]);
        }
        finally
        {
            DeleteScratchFile(path);
        }
    }

    [Test]
    public async Task Tailer_FileDoesNotExistYet_StartsInWaitingForFileState()
    {
        var directory = Path.Combine(Path.GetTempPath(), "MultiClod.App.Tests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "session.jsonl");

        try
        {
            using var tailer = new TranscriptFileTailer(path);

            // Construction defers its first state transition off-thread (so LinesAvailable
            // subscribers are never missed - see TranscriptFileTailer's constructor comment), so
            // the initial WaitingForDirectory default is expected to still be visible briefly.
            await WaitUntil(() => tailer.State == TranscriptTailerState.WaitingForFile);
            await Assert.That(tailer.State).IsEqualTo(TranscriptTailerState.WaitingForFile);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Test]
    public async Task Tailer_FileCreatedAfterConstruction_TransitionsToTailing()
    {
        var directory = Path.Combine(Path.GetTempPath(), "MultiClod.App.Tests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "session.jsonl");

        try
        {
            using var tailer = new TranscriptFileTailer(path);
            var becameTailing = new TaskCompletionSource();
            tailer.StateChanged += state =>
            {
                if (state == TranscriptTailerState.Tailing)
                {
                    becameTailing.TrySetResult();
                }
            };

            File.WriteAllText(path, "hello\n");

            await WaitOrTimeout(becameTailing.Task);
            await Assert.That(tailer.State).IsEqualTo(TranscriptTailerState.Tailing);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static async Task WaitOrTimeout(Task task, int timeoutMs = 5000)
    {
        var completed = await Task.WhenAny(task, Task.Delay(timeoutMs));
        if (completed != task)
        {
            throw new TimeoutException("Timed out waiting for the tailer event.");
        }
    }

    private static async Task WaitUntil(Func<bool> condition, int timeoutMs = 3000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(50);
        }

        throw new TimeoutException("Condition was not met in time.");
    }

    private static string CreateScratchFile()
    {
        var directory = Path.Combine(Path.GetTempPath(), "MultiClod.App.Tests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, "session.jsonl");
    }

    private static void DeleteScratchFile(string path)
    {
        try
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
