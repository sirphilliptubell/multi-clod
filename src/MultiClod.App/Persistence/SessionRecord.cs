namespace MultiClod.App.Persistence;

/// <summary>
/// A persisted session's identity and launch metadata. Referenced by <see cref="SessionHierarchyNode"/>
/// via <see cref="Id"/> so the tree's shape and a session's data can change independently.
/// </summary>
public sealed class SessionRecord
{
    public required Guid Id { get; init; }

    public required Guid ClaudeSessionId { get; init; }

    public required string Name { get; set; }

    public required string WorkingDirectory { get; set; }

    public bool HasBeenStarted { get; set; }

    // Last-known terminal title Claude Code set via an OSC 0/2 escape sequence, if any - shown in
    // the pane's title bar in place of Name once detected, and kept here so it still shows for a
    // stopped session before it's relaunched. Additive/nullable so old sessions.json files without
    // this field still deserialize fine.
    public string? DetectedTitle { get; set; }

    // Last known "settled" SessionActivity (NeedsInput/Done/Interrupted, or Idle once
    // acknowledged) - see SessionNodeViewModel.OnLiveSessionPropertyChanged, the only writer.
    // Working is deliberately never persisted here: it only means something while a turn is
    // genuinely in flight, and nothing survives a restart to ever clear it back off again.
    // Non-nullable and defaulting to Idle (enum value 0) is intentional - an old sessions.json
    // written before this field existed just deserializes it as Idle, which is exactly the
    // fallback a never-recorded session should show.
    public SessionActivity LastActivity { get; set; }
}
