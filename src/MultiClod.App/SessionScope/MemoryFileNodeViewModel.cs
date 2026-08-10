using System.IO;

namespace MultiClod.App.SessionScope;

/// <summary>
/// A flat entry in the session-scoped Memories list - mirrors Skills.SkillNodeViewModel's shape:
/// no hierarchy, and nothing needs to drive ListBox selection programmatically, so this only
/// exposes the display fields.
/// </summary>
internal sealed class MemoryFileNodeViewModel
{
    public MemoryFileNodeViewModel(string filePath)
    {
        this.FilePath = filePath;
        this.Name = Path.GetFileName(filePath);
    }

    public string FilePath { get; }

    public string Name { get; }
}
