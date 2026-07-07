namespace MultiClod.App;

/// <summary>
/// A name-only container - never nests, only ever appears at the tree's root. "Uncategorized" is
/// a reserved name for a system-managed instance of this same type (created/removed automatically
/// by <see cref="SessionTreeController"/>), not a distinct subclass.
/// </summary>
public sealed class ProjectNodeViewModel : TreeNodeViewModel
{
    public const string UncategorizedName = "Uncategorized";

    public ProjectNodeViewModel(Guid id, string name)
        : base(name)
    {
        this.Id = id;
    }

    public Guid Id { get; }

    public bool IsUncategorized => string.Equals(this.Name, UncategorizedName, StringComparison.OrdinalIgnoreCase);
}
