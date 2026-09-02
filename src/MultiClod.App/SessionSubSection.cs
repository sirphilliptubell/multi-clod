namespace MultiClod.App;

/// <summary>
/// Which item is active in the session-scoped sub-panel below the tree - only meaningful while
/// RailSection.Sessions is the active top-level section. See MainWindow.SetSessionSubSection.
/// </summary>
internal enum SessionSubSection
{
    Memories,
    ContextSkills,
    OutputStyles,
}
