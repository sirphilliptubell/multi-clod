namespace MultiClod.App.OutputStyles;

/// <summary>
/// A flat entry in OutputStylesList/SessionOutputStylesList - mirrors Skills.SkillNodeViewModel.
/// Output styles have no hierarchy/children, and nothing needs to drive ListBox selection
/// programmatically, so this only exposes the display fields.
/// </summary>
internal sealed class OutputStyleNodeViewModel
{
    public OutputStyleNodeViewModel(OutputStyleInfo info)
    {
        this.Info = info;
    }

    public OutputStyleInfo Info { get; }

    public string Name => this.Info.Name;

    public string? Description => this.Info.Description;
}
