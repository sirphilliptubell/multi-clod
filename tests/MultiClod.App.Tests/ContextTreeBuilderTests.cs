using MultiClod.App.Context;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace MultiClod.App.Tests;

public sealed class ContextTreeBuilderTests
{
    [Test]
    public async Task BuildRoot_ImportToMissingFile_ProducesMissingChild()
    {
        var scratchDir = CreateScratchDirectory();
        try
        {
            var claudeMdPath = WriteFile(scratchDir, "CLAUDE.md", "See @missing.md for details.");

            var root = ContextTreeBuilder.BuildRoot(claudeMdPath);

            await Assert.That(root.State).IsEqualTo(ContextFileState.Resolved);
            await Assert.That(root.Children).Count().IsEqualTo(1);
            var child = (ContextFileNodeViewModel)root.Children[0];
            await Assert.That(child.State).IsEqualTo(ContextFileState.Missing);
            await Assert.That(child.IsMissing).IsTrue();
        }
        finally
        {
            DeleteScratchDirectory(scratchDir);
        }
    }

    [Test]
    public async Task BuildRoot_AncestorCycle_IsDetectedAndNotExpanded()
    {
        var scratchDir = CreateScratchDirectory();
        try
        {
            var claudeMdPath = WriteFile(scratchDir, "CLAUDE.md", "@a.md");
            // a.md loops back to CLAUDE.md, its own ancestor - should be flagged Cycle, not re-expanded.
            WriteFile(scratchDir, "a.md", "@CLAUDE.md");

            var root = ContextTreeBuilder.BuildRoot(claudeMdPath);

            var aNode = (ContextFileNodeViewModel)root.Children[0];
            await Assert.That(aNode.State).IsEqualTo(ContextFileState.Resolved);
            var cycleNode = (ContextFileNodeViewModel)aNode.Children[0];
            await Assert.That(cycleNode.State).IsEqualTo(ContextFileState.Cycle);
            await Assert.That(cycleNode.IsCycle).IsTrue();
            await Assert.That(cycleNode.Children).IsEmpty();
        }
        finally
        {
            DeleteScratchDirectory(scratchDir);
        }
    }

    [Test]
    public async Task BuildRoot_DiamondImport_ExpandsIndependentlyInBothBranches()
    {
        var scratchDir = CreateScratchDirectory();
        try
        {
            var claudeMdPath = WriteFile(scratchDir, "CLAUDE.md", "@branch-x.md\n@branch-y.md");
            WriteFile(scratchDir, "branch-x.md", "@shared.md");
            WriteFile(scratchDir, "branch-y.md", "@shared.md");
            WriteFile(scratchDir, "shared.md", "# shared, no further imports");

            var root = ContextTreeBuilder.BuildRoot(claudeMdPath);

            var branchX = (ContextFileNodeViewModel)root.Children[0];
            var branchY = (ContextFileNodeViewModel)root.Children[1];
            var sharedUnderX = (ContextFileNodeViewModel)branchX.Children[0];
            var sharedUnderY = (ContextFileNodeViewModel)branchY.Children[0];

            // Neither is an ancestor of the other, so both resolve normally - not a cycle.
            await Assert.That(sharedUnderX.State).IsEqualTo(ContextFileState.Resolved);
            await Assert.That(sharedUnderY.State).IsEqualTo(ContextFileState.Resolved);
        }
        finally
        {
            DeleteScratchDirectory(scratchDir);
        }
    }

    [Test]
    public async Task BuildRoot_HopCap_StopsExpandingAtHopFour()
    {
        var scratchDir = CreateScratchDirectory();
        try
        {
            // CLAUDE.md (hop 0) -> hop1.md (hop 1) -> hop2.md (hop 2) -> hop3.md (hop 3) -> hop4.md (hop 4).
            // hop4.md's own @import is never parsed, since it was built at the cap.
            var claudeMdPath = WriteFile(scratchDir, "CLAUDE.md", "@hop1.md");
            WriteFile(scratchDir, "hop1.md", "@hop2.md");
            WriteFile(scratchDir, "hop2.md", "@hop3.md");
            WriteFile(scratchDir, "hop3.md", "@hop4.md");
            WriteFile(scratchDir, "hop4.md", "@hop5.md");
            WriteFile(scratchDir, "hop5.md", "# should never be reached");

            var root = ContextTreeBuilder.BuildRoot(claudeMdPath);

            var hop1 = (ContextFileNodeViewModel)root.Children[0];
            var hop2 = (ContextFileNodeViewModel)hop1.Children[0];
            var hop3 = (ContextFileNodeViewModel)hop2.Children[0];
            var hop4 = (ContextFileNodeViewModel)hop3.Children[0];

            await Assert.That(hop4.State).IsEqualTo(ContextFileState.Resolved);
            await Assert.That(hop4.Children).IsEmpty();
        }
        finally
        {
            DeleteScratchDirectory(scratchDir);
        }
    }

    [Test]
    public async Task BuildRoot_SelfImportAtHopZero_IsImmediatelyACycle()
    {
        var scratchDir = CreateScratchDirectory();
        try
        {
            var claudeMdPath = WriteFile(scratchDir, "CLAUDE.md", "@CLAUDE.md");

            var root = ContextTreeBuilder.BuildRoot(claudeMdPath);

            var selfNode = (ContextFileNodeViewModel)root.Children[0];
            await Assert.That(selfNode.State).IsEqualTo(ContextFileState.Cycle);
        }
        finally
        {
            DeleteScratchDirectory(scratchDir);
        }
    }

    [Test]
    public async Task BuildRoot_AncestorComparison_IsCaseInsensitive()
    {
        var scratchDir = CreateScratchDirectory();
        try
        {
            var claudeMdPath = WriteFile(scratchDir, "CLAUDE.md", "@a.md");
            // Loops back to CLAUDE.md's own resolved path, but with different casing - the cycle
            // check must still catch it since Windows paths are case-insensitive.
            WriteFile(scratchDir, "a.md", $"@{claudeMdPath.ToUpperInvariant()}");

            var root = ContextTreeBuilder.BuildRoot(claudeMdPath);

            var aNode = (ContextFileNodeViewModel)root.Children[0];
            var cycleNode = (ContextFileNodeViewModel)aNode.Children[0];
            await Assert.That(cycleNode.State).IsEqualTo(ContextFileState.Cycle);
        }
        finally
        {
            DeleteScratchDirectory(scratchDir);
        }
    }

    private static string WriteFile(string root, string fileName, string content)
    {
        var path = Path.Combine(root, fileName);
        File.WriteAllText(path, content);
        return path;
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
