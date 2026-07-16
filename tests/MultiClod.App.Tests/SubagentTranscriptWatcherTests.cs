using MultiClod.App.SessionLog;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace MultiClod.App.Tests;

public sealed class SubagentTranscriptWatcherTests
{
    [Test]
    public async Task Watcher_SubagentsAlreadyExistBeforeConstruction_AreStillDiscovered()
    {
        var sessionDirectory = CreateScratchDirectory();
        try
        {
            var subagentsDirectory = Path.Combine(sessionDirectory, "subagents");
            Directory.CreateDirectory(subagentsDirectory);
            File.WriteAllText(Path.Combine(subagentsDirectory, "agent-1.jsonl"), "{}\n");

            var discovered = new List<SessionLogSourceViewModel>();
            var sawOne = new TaskCompletionSource();

            using var watcher = new SubagentTranscriptWatcher(sessionDirectory);
            watcher.SubagentDiscovered += source =>
            {
                lock (discovered)
                {
                    discovered.Add(source);
                }

                sawOne.TrySetResult();
            };

            await WaitOrTimeout(sawOne.Task);

            List<SessionLogSourceViewModel> snapshot;
            lock (discovered)
            {
                snapshot = discovered.ToList();
            }

            await Assert.That(snapshot.Select(s => s.DisplayName)).IsEquivalentTo(["agent-1"]);
        }
        finally
        {
            DeleteScratchDirectory(sessionDirectory);
        }
    }

    private static async Task WaitOrTimeout(Task task, int timeoutMs = 5000)
    {
        var completed = await Task.WhenAny(task, Task.Delay(timeoutMs));
        if (completed != task)
        {
            throw new TimeoutException("Timed out waiting for the watcher event.");
        }
    }

    private static string CreateScratchDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "MultiClod.App.Tests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void DeleteScratchDirectory(string path)
    {
        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
