using MultiClod.App.Deeplink;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace MultiClod.App.Tests;

public sealed class ImportZipClassifierTests
{
    [Test]
    public async Task ClassifyAsync_MainSessionWithSubagent_ClassifiesBoth()
    {
        var root = CreateScratchDirectory();
        try
        {
            var sessionId = Guid.NewGuid();
            WriteMainSession(root, sessionId, @"C:\_Gits-GS-Github\multi-clod", "Imported title");
            var subagentPath = WriteSubagent(root, sessionId, "agent-abc", "subagent content");

            var contents = await ImportZipClassifier.ClassifyAsync(root, CancellationToken.None);

            await Assert.That(contents.Sessions).Count().IsEqualTo(1);
            var session = contents.Sessions[0];
            await Assert.That(session.SessionId).IsEqualTo(sessionId);
            await Assert.That(session.Cwd).IsEqualTo(@"C:\_Gits-GS-Github\multi-clod");
            await Assert.That(session.AiTitle).IsEqualTo("Imported title");
            await Assert.That(session.SubagentFilePaths).Count().IsEqualTo(1);
            await Assert.That(session.SubagentFilePaths[0]).IsEqualTo(subagentPath);
            await Assert.That(contents.OtherFilePaths).IsEmpty();
        }
        finally
        {
            DeleteScratchDirectory(root);
        }
    }

    [Test]
    public async Task ClassifyAsync_OrphanSubagentsFolderWithNoSiblingJsonl_GoesToOtherFiles()
    {
        var root = CreateScratchDirectory();
        try
        {
            var orphanId = Guid.NewGuid();
            var subagentsDir = Path.Combine(root, orphanId.ToString(), "subagents");
            Directory.CreateDirectory(subagentsDir);
            File.WriteAllText(Path.Combine(subagentsDir, "agent-x.jsonl"), "content");

            var contents = await ImportZipClassifier.ClassifyAsync(root, CancellationToken.None);

            await Assert.That(contents.Sessions).IsEmpty();
            await Assert.That(contents.OtherFilePaths).Count().IsEqualTo(1);
        }
        finally
        {
            DeleteScratchDirectory(root);
        }
    }

    [Test]
    public async Task ClassifyAsync_NonGuidNamedJsonl_GoesToOtherFiles()
    {
        var root = CreateScratchDirectory();
        try
        {
            File.WriteAllText(Path.Combine(root, "not-a-guid.jsonl"), "content");

            var contents = await ImportZipClassifier.ClassifyAsync(root, CancellationToken.None);

            await Assert.That(contents.Sessions).IsEmpty();
            await Assert.That(contents.OtherFilePaths).Count().IsEqualTo(1);
        }
        finally
        {
            DeleteScratchDirectory(root);
        }
    }

    [Test]
    public async Task ClassifyAsync_OnlyUnrelatedFiles_HasContentTrueWithNoSessions()
    {
        var root = CreateScratchDirectory();
        try
        {
            File.WriteAllText(Path.Combine(root, "notes.md"), "some notes");

            var contents = await ImportZipClassifier.ClassifyAsync(root, CancellationToken.None);

            await Assert.That(contents.Sessions).IsEmpty();
            await Assert.That(contents.OtherFilePaths).Count().IsEqualTo(1);
            await Assert.That(contents.HasContent).IsTrue();
        }
        finally
        {
            DeleteScratchDirectory(root);
        }
    }

    [Test]
    public async Task ClassifyAsync_EmptyDirectory_HasContentFalse()
    {
        var root = CreateScratchDirectory();
        try
        {
            var contents = await ImportZipClassifier.ClassifyAsync(root, CancellationToken.None);

            await Assert.That(contents.HasContent).IsFalse();
        }
        finally
        {
            DeleteScratchDirectory(root);
        }
    }

    private static void WriteMainSession(string root, Guid sessionId, string cwd, string aiTitle)
    {
        var lines = new[]
        {
            $"{{\"cwd\":\"{cwd.Replace("\\", "\\\\")}\",\"type\":\"user\"}}",
            $"{{\"type\":\"ai-title\",\"aiTitle\":\"{aiTitle}\",\"sessionId\":\"{sessionId}\"}}",
        };
        File.WriteAllLines(Path.Combine(root, $"{sessionId}.jsonl"), lines);
    }

    private static string WriteSubagent(string root, Guid sessionId, string agentFileStem, string content)
    {
        var subagentsDir = Path.Combine(root, sessionId.ToString(), "subagents");
        Directory.CreateDirectory(subagentsDir);

        var subagentPath = Path.Combine(subagentsDir, $"{agentFileStem}.jsonl");
        File.WriteAllText(subagentPath, content);
        return subagentPath;
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
        catch (IOException)
        {
        }
    }
}
