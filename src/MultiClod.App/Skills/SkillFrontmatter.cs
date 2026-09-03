using MultiClod.App.Frontmatter;
using YamlDotNet.RepresentationModel;

namespace MultiClod.App.Skills;

/// <summary>
/// A SKILL.md frontmatter block: the generic ordered-mapping engine (Frontmatter.FrontmatterMapping)
/// plus typed get/set helpers for the curated fields the Skill editor (Skills\SkillEditor) renders
/// as structured controls. Any key this type has no accessor for (custom/unrecognized keys, or spec
/// fields the editor doesn't give a dedicated control) simply stays in Mapping untouched.
/// </summary>
internal sealed class SkillFrontmatter : FrontmatterMapping
{
    // Canonical order new keys are appended in - callers that regenerate the whole block from the
    // curated controls should call the corresponding setters in this order every time, since
    // SetOrRemoveScalar only appends a brand-new key at the current end of Mapping; keys that
    // already exist are updated in place regardless of call order.
    internal const string NameKey = "name";
    internal const string DescriptionKey = "description";
    internal const string WhenToUseKey = "when_to_use";
    internal const string ArgumentHintKey = "argument-hint";
    internal const string DisableModelInvocationKey = "disable-model-invocation";
    internal const string UserInvocableKey = "user-invocable";
    internal const string AllowedToolsKey = "allowed-tools";
    internal const string DisallowedToolsKey = "disallowed-tools";
    internal const string ModelKey = "model";
    internal const string EffortKey = "effort";

    internal SkillFrontmatter(YamlMappingNode mapping)
        : base(mapping)
    {
    }

    internal static SkillFrontmatter CreateEmpty() => new(new YamlMappingNode());

    internal string? Name => this.GetString(NameKey);

    internal string? Description => this.GetString(DescriptionKey);

    internal string? WhenToUse => this.GetString(WhenToUseKey);

    internal string? ArgumentHint => this.GetString(ArgumentHintKey);

    internal bool DisableModelInvocation => this.GetBool(DisableModelInvocationKey, defaultValue: false);

    internal bool UserInvocable => this.GetBool(UserInvocableKey, defaultValue: true);

    internal IReadOnlyList<string> AllowedTools => this.GetListOrString(AllowedToolsKey);

    internal IReadOnlyList<string> DisallowedTools => this.GetListOrString(DisallowedToolsKey);

    internal string? Model => this.GetString(ModelKey);

    internal string? Effort => this.GetString(EffortKey);

    internal void SetName(string? value) => this.SetOrRemoveScalar(NameKey, value);

    internal void SetDescription(string? value) => this.SetOrRemoveScalar(DescriptionKey, value);

    internal void SetWhenToUse(string? value) => this.SetOrRemoveScalar(WhenToUseKey, value);

    internal void SetArgumentHint(string? value) => this.SetOrRemoveScalar(ArgumentHintKey, value);

    internal void SetDisableModelInvocation(bool value) =>
        this.SetOrRemoveScalar(DisableModelInvocationKey, value == false ? null : "true");

    internal void SetUserInvocable(bool value) =>
        this.SetOrRemoveScalar(UserInvocableKey, value ? null : "false");

    internal void SetAllowedTools(IReadOnlyList<string> values) => this.SetListOrString(AllowedToolsKey, values);

    internal void SetDisallowedTools(IReadOnlyList<string> values) => this.SetListOrString(DisallowedToolsKey, values);

    internal void SetModel(string? value) => this.SetOrRemoveScalar(ModelKey, value);

    internal void SetEffort(string? value) => this.SetOrRemoveScalar(EffortKey, value);
}
