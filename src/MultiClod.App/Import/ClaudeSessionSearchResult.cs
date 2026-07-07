namespace MultiClod.App.Import;

/// <summary>
/// One search hit against the ~/.claude/projects tree. <see cref="ParentSessionId"/> and
/// <see cref="WorkingDirectory"/> always describe the resumable MAIN session, even when
/// <see cref="MatchedFileDisplayPath"/> points at one of its subagent transcripts - a subagent's
/// own id is never independently resumable via `claude --resume`.
/// </summary>
internal sealed record ClaudeSessionSearchResult(
    string ProjectDirectoryName,
    string MatchedFileDisplayPath,
    Guid ParentSessionId,
    string WorkingDirectory,
    string? Summary,
    DateTime LastModified)
{
    // Single source of truth for the "no title yet" fallback, used both for the Summary column
    // and as the imported tree node's Name - keeps the fallback rule from being duplicated.
    public string SummaryOrSessionId => this.Summary is { Length: > 0 } ? this.Summary : this.ParentSessionId.ToString();
}
