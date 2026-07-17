using System.IO;

namespace MultiClod.App.Context;

/// <summary>
/// Whether a ContextFileNodeViewModel's resolved path exists on disk, and if so whether it was
/// safe to expand further - see ContextTreeBuilder.
/// </summary>
internal enum ContextFileState
{
    Resolved,
    Missing,
    Cycle,
}

/// <summary>
/// A node in the CLAUDE.md @import tree - either the CLAUDE.md root itself or a resolved/missing/
/// cycle-detected import target. Mirrors SessionNodeViewModel's pattern of one state field driving
/// derived bools, but is otherwise immutable: a save-triggered refresh rebuilds the whole tree from
/// scratch (see MainWindow's DocumentSaved handling) rather than mutating an existing node in
/// place, so there's no need for these to raise PropertyChanged after construction.
/// </summary>
internal sealed class ContextFileNodeViewModel : TreeNodeViewModel
{
    public ContextFileNodeViewModel(string resolvedPath, string? rawImportText, ContextFileState state)
        : base(Path.GetFileName(resolvedPath))
    {
        this.ResolvedPath = resolvedPath;
        this.RawImportText = rawImportText;
        this.State = state;
    }

    public string ResolvedPath { get; }

    // Null for the CLAUDE.md root node itself, which isn't the target of an @import.
    public string? RawImportText { get; }

    public ContextFileState State { get; }

    public bool IsMissing => this.State == ContextFileState.Missing;

    public bool IsCycle => this.State == ContextFileState.Cycle;

    public string ToolTipText => this.State switch
    {
        ContextFileState.Missing => $"{this.ResolvedPath} (not found — click to create)",
        ContextFileState.Cycle => $"{this.ResolvedPath} (already loaded earlier in this chain)",
        _ => this.ResolvedPath,
    };
}
