namespace MultiClod.App.Skills;

/// <summary>
/// A flat entry in SkillsList - unlike TreeNodeViewModel, skills have no hierarchy/children, and
/// nothing needs to drive ListBox selection programmatically the way drag/drop does for the
/// project tree, so this only exposes the display fields; SkillsList.SelectedItem is enough.
/// </summary>
internal sealed class SkillNodeViewModel
{
    public SkillNodeViewModel(SkillInfo info)
    {
        this.Info = info;
    }

    public SkillInfo Info { get; }

    public string Name => this.Info.Name;

    public string? Description => this.Info.Description;
}
