using System.ComponentModel;

namespace MultiClod.App.SessionLog.Tree;

/// <summary>
/// What a box represents structurally, beyond its underlying row's own Category (User/Assistant/
/// ToolCall/etc. - see Rendering.TranscriptRowCategory). Entry = a normal row. SubagentSpawn = the
/// ToolCallRowViewModel whose tool_use id matches a child agent's meta.json - this is the box a
/// spawn connector originates from. SubagentReturn = a synthesized box, positioned at the parent's
/// tool_result line for that same child, reusing the SAME RowVm as the SubagentSpawn box (see
/// TreeGraphBuilder's remarks on why the factory can't give us two separate row instances here).
/// </summary>
public enum BoxKind
{
    Entry,
    SubagentSpawn,
    SubagentReturn,
}

/// <summary>
/// One rendered box in the Tree view. Wraps a Rendering.TranscriptRowViewModel (the exact same row
/// model the List view renders) plus Tree-only graph/layout state. A stable instance for the
/// lifetime of one snapshot - BuildSnapshot replaces the whole graph rather than mutating boxes in
/// place, so IsSelected identity is re-established by SourceLineOrdinal matching, not by object
/// reference (see SessionTreeView's view-state preservation).
/// </summary>
public sealed class BoxNode : INotifyPropertyChanged
{
    private bool isSelected;

    public BoxNode(Rendering.TranscriptRowViewModel rowVm, BoxKind kind, AgentNode owner, int sourceLineOrdinal, string? toolUseId)
    {
        this.RowVm = rowVm;
        this.Kind = kind;
        this.Owner = owner;
        this.SourceLineOrdinal = sourceLineOrdinal;
        this.ToolUseId = toolUseId;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public Rendering.TranscriptRowViewModel RowVm { get; }

    public BoxKind Kind { get; }

    public AgentNode Owner { get; }

    // 0-based ordinal of the raw jsonl line this box came from, within Owner's file. Used to
    // re-identify a box across a rebuild (Refresh / "Show all events" toggle) and as the row-order
    // tiebreak within one agent.
    public int SourceLineOrdinal { get; }

    // Only set for a SubagentSpawn or SubagentReturn box - the tool_use id linking it to a child
    // AgentNode's SpawnBox/ReturnBox/LinkedChild.
    public string? ToolUseId { get; }

    // Set by TreeGraphBuilder.LinkSpawns; only meaningful when Kind == SubagentSpawn.
    public AgentNode? LinkedChild { get; internal set; }

    public int Column { get; internal set; }

    public int Row { get; internal set; }

    public double X { get; internal set; }

    public double Y { get; internal set; }

    public bool IsSelected
    {
        get => this.isSelected;
        set
        {
            if (this.isSelected == value)
            {
                return;
            }

            this.isSelected = value;
            this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(this.IsSelected)));
        }
    }
}
