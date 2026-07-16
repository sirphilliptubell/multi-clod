namespace MultiClod.App.SessionLog.Rendering;

/// <summary>
/// Top-level JSON fields present on essentially every "real" (user/assistant) transcript line -
/// shared by every row type derived from that kind of line, so Additional Properties doesn't
/// re-surface identical plumbing (uuid, timestamp, cwd, etc.) on every single row in the viewer.
/// </summary>
internal static class CommonEntryFields
{
    public static readonly IReadOnlySet<string> BaseConsumedPaths = new HashSet<string>
    {
        "type", "uuid", "parentUuid", "timestamp", "sessionId", "isSidechain",
        "cwd", "gitBranch", "version", "userType", "entrypoint", "isMeta", "permissionMode",
    };
}
