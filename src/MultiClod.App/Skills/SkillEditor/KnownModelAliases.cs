namespace MultiClod.App.Skills.SkillEditor;

/// <summary>
/// Small hardcoded seed list for the `model` field's ComboBox. There is no local/offline way to
/// enumerate Anthropic's current models from an installed claude CLI (checked `claude --help`'s
/// command list, `claude doctor`, and `~/.claude/*.json` - no models catalog anywhere), so this
/// can go stale between app releases. The ComboBox stays editable (IsEditable="True") so that
/// never blocks anyone from typing a newer alias or a fully-qualified dated model ID by hand.
/// </summary>
internal static class KnownModelAliases
{
    // Leading "" gives the editable ComboBox a one-click way back to "not set" (matches the blank
    // first item EffortCombo uses for the same reason) - typing over it or deleting the text also
    // works, but this is the discoverable affordance.
    public static readonly IReadOnlyList<string> Values = ["", "inherit", "sonnet", "opus", "haiku", "fable"];
}
