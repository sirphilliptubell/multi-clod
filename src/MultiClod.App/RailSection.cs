namespace MultiClod.App;

/// <summary>
/// Which major feature the rail's accent bar currently marks, and therefore which panel
/// (Tree vs. SkillsList) and canvas content are visible. See MainWindow.SetRailSection.
/// </summary>
internal enum RailSection
{
    Sessions,
    Skills,
    Settings,
}
