using MultiClod.App.Import;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace MultiClod.App.Tests;

public sealed class ClaudeSessionSearchServiceTests
{
    [Test]
    public async Task SearchAsync_AllWordsPresentAcrossDifferentLines_ReturnsMainSessionHit()
    {
        var root = CreateScratchDirectory();
        try
        {
            var sessionId = Guid.NewGuid();
            WriteMainSession(root, "multi-clod", sessionId, @"C:\_Gits-GS-Github\multi-clod",
                "alpha bravo",
                "charlie");

            var results = await Search(root, "alpha charlie");

            await Assert.That(results).Count().IsEqualTo(1);
            await Assert.That(results[0].ParentSessionId).IsEqualTo(sessionId);
            await Assert.That(results[0].ProjectDirectoryName).IsEqualTo("multi-clod");
        }
        finally
        {
            DeleteScratchDirectory(root);
        }
    }

    [Test]
    public async Task SearchAsync_MissingOneWord_ReturnsNoHit()
    {
        var root = CreateScratchDirectory();
        try
        {
            WriteMainSession(root, "multi-clod", Guid.NewGuid(), @"C:\_Gits-GS-Github\multi-clod", "alpha bravo");

            var results = await Search(root, "alpha zulu");

            await Assert.That(results).IsEmpty();
        }
        finally
        {
            DeleteScratchDirectory(root);
        }
    }

    [Test]
    public async Task SearchAsync_QueryHasMixedCaseAndExtraWhitespace_StillMatchesCaseInsensitively()
    {
        var root = CreateScratchDirectory();
        try
        {
            WriteMainSession(root, "multi-clod", Guid.NewGuid(), @"C:\_Gits-GS-Github\multi-clod", "Alpha Bravo");

            var results = await Search(root, "  BRAVO   alpha  ");

            await Assert.That(results).Count().IsEqualTo(1);
        }
        finally
        {
            DeleteScratchDirectory(root);
        }
    }

    [Test]
    public async Task SearchAsync_SubagentHit_ReportsParentSessionButOwnFilePath()
    {
        var root = CreateScratchDirectory();
        try
        {
            var sessionId = Guid.NewGuid();
            var projectDir = WriteMainSession(root, "multi-clod", sessionId, @"C:\_Gits-GS-Github\multi-clod", "unrelated main text");
            var subagentPath = WriteSubagent(projectDir, sessionId, "agent-abc", "needle in subagent transcript", metaDescription: null);

            var results = await Search(root, "needle subagent");

            await Assert.That(results).Count().IsEqualTo(1);
            var hit = results[0];
            await Assert.That(hit.ParentSessionId).IsEqualTo(sessionId);
            await Assert.That(hit.MatchedFileDisplayPath).Contains("subagents");
            await Assert.That(hit.MatchedFileDisplayPath).Contains(Path.GetFileName(subagentPath));
            await Assert.That(hit.WorkingDirectory).IsEqualTo(@"C:\_Gits-GS-Github\multi-clod");
        }
        finally
        {
            DeleteScratchDirectory(root);
        }
    }

    [Test]
    public async Task SearchAsync_GuidNamedFolderWithNoSiblingJsonl_IsSkippedWithoutThrowing()
    {
        var root = CreateScratchDirectory();
        try
        {
            var projectDir = Path.Combine(root, "multi-clod");
            Directory.CreateDirectory(projectDir);

            // No sibling <guid>.jsonl exists for this folder - must not be treated as a session dir.
            var orphanDir = Path.Combine(projectDir, Guid.NewGuid().ToString());
            Directory.CreateDirectory(Path.Combine(orphanDir, "subagents"));
            File.WriteAllText(Path.Combine(orphanDir, "subagents", "agent-x.jsonl"), "needle");

            var results = await Search(root, "needle");

            await Assert.That(results).IsEmpty();
        }
        finally
        {
            DeleteScratchDirectory(root);
        }
    }

    [Test]
    public async Task SearchAsync_NonGuidTopLevelEntries_AreIgnoredWithoutThrowing()
    {
        var root = CreateScratchDirectory();
        try
        {
            var projectDir = Path.Combine(root, "multi-clod");
            Directory.CreateDirectory(projectDir);
            Directory.CreateDirectory(Path.Combine(projectDir, "memory"));
            File.WriteAllText(Path.Combine(projectDir, "memory", "notes.jsonl"), "needle");
            File.WriteAllText(Path.Combine(projectDir, "not-a-guid.jsonl"), "needle");

            var results = await Search(root, "needle");

            await Assert.That(results).IsEmpty();
        }
        finally
        {
            DeleteScratchDirectory(root);
        }
    }

    [Test]
    public async Task SearchAsync_RepeatedAiTitleLines_LastOneWins()
    {
        var root = CreateScratchDirectory();
        try
        {
            var sessionId = Guid.NewGuid();
            var projectDir = Path.Combine(root, "multi-clod");
            Directory.CreateDirectory(projectDir);
            var sessionPath = Path.Combine(projectDir, $"{sessionId}.jsonl");
            File.WriteAllLines(sessionPath,
            [
                "{\"cwd\":\"C:\\\\_Gits-GS-Github\\\\multi-clod\",\"type\":\"user\"}",
                "needle",
                $"{{\"type\":\"ai-title\",\"aiTitle\":\"First title\",\"sessionId\":\"{sessionId}\"}}",
                $"{{\"type\":\"ai-title\",\"aiTitle\":\"Latest title\",\"sessionId\":\"{sessionId}\"}}",
            ]);

            var results = await Search(root, "needle");

            await Assert.That(results).Count().IsEqualTo(1);
            await Assert.That(results[0].Summary).IsEqualTo("Latest title");
        }
        finally
        {
            DeleteScratchDirectory(root);
        }
    }

    [Test]
    public async Task SearchAsync_NoAiTitleButSubagentMetaHasDescription_UsesDescriptionAsSummary()
    {
        var root = CreateScratchDirectory();
        try
        {
            var sessionId = Guid.NewGuid();
            var projectDir = WriteMainSession(root, "multi-clod", sessionId, @"C:\_Gits-GS-Github\multi-clod", "needle in main");
            WriteSubagent(projectDir, sessionId, "agent-abc", "needle in subagent", metaDescription: "Investigate the needle bug");

            var results = await Search(root, "needle");

            // Both the main file and the subagent file match "needle" - the subagent hit falls
            // back to its own meta.json description since the parent has no ai-title.
            var subagentHit = results.Single(r => r.MatchedFileDisplayPath.Contains("subagents"));
            await Assert.That(subagentHit.Summary).IsEqualTo("Investigate the needle bug");
        }
        finally
        {
            DeleteScratchDirectory(root);
        }
    }

    [Test]
    public async Task SearchAsync_NeitherAiTitleNorMetaDescription_FallsBackToSessionIdString()
    {
        var root = CreateScratchDirectory();
        try
        {
            var sessionId = Guid.NewGuid();
            WriteMainSession(root, "multi-clod", sessionId, @"C:\_Gits-GS-Github\multi-clod", "needle");

            var results = await Search(root, "needle");

            await Assert.That(results[0].Summary).IsNull();
            await Assert.That(results[0].SummaryOrSessionId).IsEqualTo(sessionId.ToString());
        }
        finally
        {
            DeleteScratchDirectory(root);
        }
    }

    [Test]
    public async Task SearchAsync_CwdTakenFromFirstOccurrence_NotOverwrittenByLater()
    {
        var root = CreateScratchDirectory();
        try
        {
            var sessionId = Guid.NewGuid();
            var projectDir = Path.Combine(root, "multi-clod");
            Directory.CreateDirectory(projectDir);
            var sessionPath = Path.Combine(projectDir, $"{sessionId}.jsonl");
            File.WriteAllLines(sessionPath,
            [
                "{\"cwd\":\"C:\\\\First\\\\Path\",\"type\":\"user\"}",
                "{\"cwd\":\"C:\\\\Second\\\\Path\",\"type\":\"user\"}",
                "needle",
            ]);

            var results = await Search(root, "needle");

            await Assert.That(results[0].WorkingDirectory).IsEqualTo(@"C:\First\Path");
        }
        finally
        {
            DeleteScratchDirectory(root);
        }
    }

    [Test]
    public async Task SearchAsync_MultipleProjectFolders_EachHitReportsCorrectDirectoryName()
    {
        var root = CreateScratchDirectory();
        try
        {
            WriteMainSession(root, "project-a", Guid.NewGuid(), @"C:\project-a", "needle");
            WriteMainSession(root, "project-b", Guid.NewGuid(), @"C:\project-b", "needle");

            var results = await Search(root, "needle");

            var directories = results.Select(r => r.ProjectDirectoryName).OrderBy(n => n).ToArray();
            await Assert.That(directories).IsEquivalentTo(["project-a", "project-b"]);
        }
        finally
        {
            DeleteScratchDirectory(root);
        }
    }

    [Test]
    public async Task SearchAsync_PopulatesLastModifiedFromTheMatchedFileItself()
    {
        var root = CreateScratchDirectory();
        try
        {
            var sessionId = Guid.NewGuid();
            var projectDir = WriteMainSession(root, "multi-clod", sessionId, @"C:\_Gits-GS-Github\multi-clod", "needle in main");
            var subagentPath = WriteSubagent(projectDir, sessionId, "agent-abc", "needle in subagent", metaDescription: null);

            var olderWriteTime = DateTime.Now.AddDays(-3);
            File.SetLastWriteTime(subagentPath, olderWriteTime);

            var results = await Search(root, "needle");

            // Each hit's LastModified is the file that actually matched (subagent vs main), not
            // shared from the parent - a subagent file can be touched independently of its parent.
            var mainHit = results.Single(r => !r.MatchedFileDisplayPath.Contains("subagents"));
            var subagentHit = results.Single(r => r.MatchedFileDisplayPath.Contains("subagents"));

            await Assert.That(mainHit.LastModified).IsGreaterThan(olderWriteTime);
            await Assert.That(subagentHit.LastModified).IsEqualTo(olderWriteTime);
        }
        finally
        {
            DeleteScratchDirectory(root);
        }
    }

    [Test]
    public async Task SearchAsync_BlankSearchText_ReturnsEmptyWithoutThrowing()
    {
        var root = CreateScratchDirectory();
        try
        {
            WriteMainSession(root, "multi-clod", Guid.NewGuid(), @"C:\_Gits-GS-Github\multi-clod", "needle");

            var results = await Search(root, "   ");

            await Assert.That(results).IsEmpty();
        }
        finally
        {
            DeleteScratchDirectory(root);
        }
    }

    private static async Task<IReadOnlyList<ClaudeSessionSearchResult>> Search(string root, string searchText)
    {
        var service = new ClaudeSessionSearchService(root);
        return await service.SearchAsync(searchText, CancellationToken.None);
    }

    private static string WriteMainSession(string root, string projectDirectoryName, Guid sessionId, string cwd, params string[] extraLines)
    {
        var projectDir = Path.Combine(root, projectDirectoryName);
        Directory.CreateDirectory(projectDir);

        var lines = new List<string> { $"{{\"cwd\":\"{cwd.Replace("\\", "\\\\")}\",\"type\":\"user\"}}" };
        lines.AddRange(extraLines);

        File.WriteAllLines(Path.Combine(projectDir, $"{sessionId}.jsonl"), lines);
        return projectDir;
    }

    private static string WriteSubagent(string projectDir, Guid sessionId, string agentFileStem, string content, string? metaDescription)
    {
        var subagentsDir = Path.Combine(projectDir, sessionId.ToString(), "subagents");
        Directory.CreateDirectory(subagentsDir);

        var subagentPath = Path.Combine(subagentsDir, $"{agentFileStem}.jsonl");
        File.WriteAllText(subagentPath, content);

        if (metaDescription is not null)
        {
            File.WriteAllText(Path.Combine(subagentsDir, $"{agentFileStem}.meta.json"), $"{{\"description\":\"{metaDescription}\"}}");
        }

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
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
