namespace MultiClod.App.Persistence;

/// <summary>
/// The root of sessions.json. <see cref="Sessions"/> is a flat lookup of every session's data;
/// <see cref="Hierarchy"/> describes tree position/order only, referencing sessions by id. Bump
/// <see cref="SessionStore.CurrentVersion"/> and add a translation step in
/// <see cref="SessionStore"/> when this shape needs to change.
/// </summary>
public sealed class SessionsFile
{
    public int Version { get; init; } = SessionStore.CurrentVersion;

    public List<SessionRecord> Sessions { get; init; } = [];

    public List<HierarchyNode> Hierarchy { get; init; } = [];
}
