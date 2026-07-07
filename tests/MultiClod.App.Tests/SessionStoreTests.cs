using MultiClod.App.Persistence;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace MultiClod.App.Tests;

public sealed class SessionStoreTests
{
    [Test]
    public async Task RoundTrip_SaveThenLoad_ReturnsSameData()
    {
        var scratchDir = CreateScratchDirectory();
        try
        {
            var store = new SessionStore(scratchDir);
            var file = SampleFile();

            store.ScheduleSave(file);
            store.Flush();

            var loaded = new SessionStore(scratchDir).Load();

            await Assert.That(loaded.Version).IsEqualTo(0);
            await Assert.That(loaded.Sessions).Count().IsEqualTo(1);
            await Assert.That(loaded.Sessions[0].Name).IsEqualTo("Backend work");
            await Assert.That(loaded.Hierarchy).Count().IsEqualTo(1);
        }
        finally
        {
            DeleteScratchDirectory(scratchDir);
        }
    }

    [Test]
    public async Task Load_NoFileYet_ReturnsEmptyFile()
    {
        var scratchDir = CreateScratchDirectory();
        try
        {
            var loaded = new SessionStore(scratchDir).Load();

            await Assert.That(loaded.Version).IsEqualTo(0);
            await Assert.That(loaded.Sessions).IsEmpty();
            await Assert.That(loaded.Hierarchy).IsEmpty();
        }
        finally
        {
            DeleteScratchDirectory(scratchDir);
        }
    }

    [Test]
    public async Task Load_CorruptPrimary_FallsBackToNewestBackup()
    {
        var scratchDir = CreateScratchDirectory();
        try
        {
            var store = new SessionStore(scratchDir);

            // First save: becomes the backup once the second save runs.
            var goodFile = SampleFile();
            store.ScheduleSave(goodFile);
            store.Flush();

            // Second save: this content becomes "corrupted" below, so the first save's content
            // (now sitting in sessions/) is what Load() should recover.
            store.ScheduleSave(new SessionsFile());
            store.Flush();

            File.WriteAllText(Path.Combine(scratchDir, "sessions.json"), "{ not valid json ");

            var loaded = new SessionStore(scratchDir).Load();

            await Assert.That(loaded.Sessions).Count().IsEqualTo(1);
            await Assert.That(loaded.Sessions[0].Name).IsEqualTo("Backend work");
        }
        finally
        {
            DeleteScratchDirectory(scratchDir);
        }
    }

    [Test]
    public async Task Load_CorruptPrimaryAndNoBackups_ReturnsEmptyFile()
    {
        var scratchDir = CreateScratchDirectory();
        try
        {
            Directory.CreateDirectory(scratchDir);
            File.WriteAllText(Path.Combine(scratchDir, "sessions.json"), "{ not valid json ");

            var loaded = new SessionStore(scratchDir).Load();

            await Assert.That(loaded.Sessions).IsEmpty();
            await Assert.That(loaded.Hierarchy).IsEmpty();

            // The broken primary file must be left untouched for manual inspection, not deleted.
            await Assert.That(File.Exists(Path.Combine(scratchDir, "sessions.json"))).IsTrue();
        }
        finally
        {
            DeleteScratchDirectory(scratchDir);
        }
    }

    [Test]
    public async Task RepeatedSaves_CapBackupsAtTen_AndNeverDropAWrite()
    {
        var scratchDir = CreateScratchDirectory();
        try
        {
            var store = new SessionStore(scratchDir);

            const int iterations = 15;
            for (var i = 0; i < iterations; i++)
            {
                var file = SampleFile();
                file.Sessions[0].Name = $"Iteration {i}";
                store.ScheduleSave(file);
                store.Flush();
            }

            var backupsDir = Path.Combine(scratchDir, "sessions");
            var backupFiles = Directory.GetFiles(backupsDir, "sessions-*.json");

            // 15 saves produce at most 14 backups (the very first save has nothing to back up),
            // pruned down to the 10 most recent - this also proves same-second filename
            // collisions were suffixed rather than silently failing the File.Copy, since a
            // swallowed collision would have aborted that iteration's write before the JSON
            // content was updated.
            await Assert.That(backupFiles.Length).IsEqualTo(10);

            var finalContent = new SessionStore(scratchDir).Load();
            await Assert.That(finalContent.Sessions[0].Name).IsEqualTo($"Iteration {iterations - 1}");
        }
        finally
        {
            DeleteScratchDirectory(scratchDir);
        }
    }

    private static SessionsFile SampleFile()
    {
        var sessionId = Guid.NewGuid();
        return new SessionsFile
        {
            Sessions =
            [
                new SessionRecord
                {
                    Id = sessionId,
                    ClaudeSessionId = Guid.NewGuid(),
                    Name = "Backend work",
                    WorkingDirectory = @"C:\_Gits-GS-Github\multi-clod",
                    HasBeenStarted = true,
                },
            ],
            Hierarchy =
            [
                new ProjectHierarchyNode
                {
                    Id = Guid.NewGuid(),
                    Name = "multi-clod",
                    Children = [new SessionHierarchyNode { SessionId = sessionId }],
                },
            ],
        };
    }

    private static string CreateScratchDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "MultiClod.App.Tests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(path);
        return path;
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
