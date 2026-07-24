namespace MultiClod.App.Deeplink;

/// <summary>
/// One node in DeeplinkImportWindow's Sessions tree: either a main session (Children = its
/// subagent transcripts) or a leaf subagent entry (Children empty). Every node carries its own
/// FilePath so selecting any node - session or subagent - can point TranscriptViewerControl at it
/// directly, same as SessionLogSourceViewModel does for a live session's picker.
/// </summary>
internal sealed record DeeplinkSessionTreeItem(string DisplayName, string FilePath, IReadOnlyList<DeeplinkSessionTreeItem> Children)
{
    public static DeeplinkSessionTreeItem Leaf(string displayName, string filePath) =>
        new(displayName, filePath, Array.Empty<DeeplinkSessionTreeItem>());
}

/// <summary>One entry in DeeplinkImportWindow's Other-files list.</summary>
internal sealed record DeeplinkOtherFileItem(string DisplayName, string FilePath);
