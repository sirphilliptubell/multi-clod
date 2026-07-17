namespace MultiClod.App;

/// <summary>
/// Which major feature the rail's accent bar currently marks, and therefore which panel
/// (Tree vs. ContextPanel's ContextTree+SkillsList) and canvas content are visible. See
/// MainWindow.SetRailSection.
/// </summary>
internal enum RailSection
{
    Sessions,
    Context,
    Settings,
}
