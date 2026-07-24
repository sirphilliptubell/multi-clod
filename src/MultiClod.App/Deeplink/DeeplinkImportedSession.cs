namespace MultiClod.App.Deeplink;

/// <summary>
/// One main session found inside an extracted deeplink zip, plus whichever subagent transcripts
/// were found alongside it. Unlike a live SessionNodeViewModel, this is a plain immutable snapshot
/// - the extracted files never change once extraction completes.
/// </summary>
public sealed record DeeplinkImportedSession(
    Guid SessionId,
    string MainFilePath,
    string? Cwd,
    string? AiTitle,
    IReadOnlyList<string> SubagentFilePaths)
{
    public string DisplayLabel => this.AiTitle ?? this.Cwd ?? this.SessionId.ToString();
}

/// <summary>
/// The result of classifying an extracted deeplink zip's contents - see ImportZipClassifier.
/// A zip with only OtherFilePaths (no Sessions) is valid; only both being empty is a failure.
/// </summary>
public sealed record ClassifiedImportContents(
    IReadOnlyList<DeeplinkImportedSession> Sessions,
    IReadOnlyList<string> OtherFilePaths)
{
    public bool HasContent => this.Sessions.Count > 0 || this.OtherFilePaths.Count > 0;
}
