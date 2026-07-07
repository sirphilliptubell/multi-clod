using System.Text.Json.Serialization;

namespace MultiClod.App.Persistence;

/// <summary>
/// One node in the persisted Project/Session tree. Project nodes are only ever placed at the top
/// level of <see cref="SessionsFile.Hierarchy"/> by convention (projects never nest) - the type
/// system doesn't enforce this, <see cref="SessionTreeController"/> does.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(ProjectHierarchyNode), typeDiscriminator: "project")]
[JsonDerivedType(typeof(SessionHierarchyNode), typeDiscriminator: "session")]
public abstract class HierarchyNode
{
    public List<HierarchyNode> Children { get; init; } = [];
}

/// <summary>
/// A Project is a name-only container - it has no working directory of its own. The reserved
/// name "Uncategorized" is a normal node of this type, not a distinct schema shape - the tree
/// controller creates and removes it automatically.
/// </summary>
public sealed class ProjectHierarchyNode : HierarchyNode
{
    public required Guid Id { get; init; }

    public required string Name { get; set; }
}

/// <summary>
/// References a <see cref="SessionRecord"/> by <see cref="SessionId"/> rather than embedding it,
/// so a session's tree position and its data round-trip independently.
/// </summary>
public sealed class SessionHierarchyNode : HierarchyNode
{
    public required Guid SessionId { get; init; }
}
