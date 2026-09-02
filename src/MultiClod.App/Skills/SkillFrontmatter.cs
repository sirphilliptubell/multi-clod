using YamlDotNet.RepresentationModel;

namespace MultiClod.App.Skills;

/// <summary>
/// Ordered YAML mapping backing a SKILL.md frontmatter block, plus typed get/set helpers for the
/// curated fields the Skill editor (Skills\SkillEditor) renders as structured controls. Wraps a
/// YamlDotNet YamlMappingNode - which preserves insertion order on enumeration - rather than a
/// plain dictionary, so an edit to one field never reorders every other key; see
/// SkillFrontmatterYaml.Serialize for how that mapping becomes text again. Any key this type has
/// no accessor for (custom/unrecognized keys, or spec fields the editor doesn't give a dedicated
/// control) simply stays in Mapping untouched.
/// </summary>
internal sealed class SkillFrontmatter
{
    // Canonical order new keys are appended in - callers that regenerate the whole block from the
    // curated controls should call the corresponding setters in this order every time, since
    // SetOrRemove only appends a brand-new key at the current end of Mapping; keys that already
    // exist are updated in place regardless of call order.
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

    // Above this many items, a list-or-string field is always written as a YAML block list, even
    // if the file previously had it as a flat string - a 5+ item space-separated line is hard to
    // read, and this is the one place shape-preservation is deliberately overridden.
    private const int FlatStringMaxItemCount = 4;

    internal SkillFrontmatter(YamlMappingNode mapping)
    {
        this.Mapping = mapping;
    }

    internal static SkillFrontmatter CreateEmpty() => new(new YamlMappingNode());

    internal YamlMappingNode Mapping { get; }

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

    internal string? GetString(string key) =>
        this.TryFindValue(key, out var value) && value is YamlScalarNode { Value: { Length: > 0 } text } ? text : null;

    internal bool GetBool(string key, bool defaultValue)
    {
        var raw = this.GetString(key);
        if (raw is null)
        {
            return defaultValue;
        }

        return raw.ToLowerInvariant() switch
        {
            "true" or "yes" or "on" or "1" => true,
            "false" or "no" or "off" or "0" => false,
            _ => defaultValue,
        };
    }

    // Accepts either a flat "space or comma separated" scalar or a YAML block/flow sequence of
    // scalars, matching what allowed-tools/disallowed-tools/paths accept per the frontmatter spec.
    internal IReadOnlyList<string> GetListOrString(string key)
    {
        if (!this.TryFindValue(key, out var value))
        {
            return [];
        }

        if (value is YamlScalarNode { Value: { Length: > 0 } text })
        {
            return text.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }

        if (value is YamlSequenceNode sequence)
        {
            return sequence.Children.OfType<YamlScalarNode>().Select(n => n.Value ?? string.Empty).Where(s => s.Length > 0).ToList();
        }

        return [];
    }

    internal void SetListOrString(string key, IReadOnlyList<string> values)
    {
        if (values.Count == 0)
        {
            this.RemoveKey(key);
            return;
        }

        YamlNode node = values.Count > FlatStringMaxItemCount
            ? new YamlSequenceNode(values.Select(v => new YamlScalarNode(v)))
            : new YamlScalarNode(string.Join(' ', values));

        this.SetOrReplace(key, node);
    }

    private void SetOrRemoveScalar(string key, string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            this.RemoveKey(key);
            return;
        }

        this.SetOrReplace(key, new YamlScalarNode(value));
    }

    private void SetOrReplace(string key, YamlNode value)
    {
        var existingKey = this.FindKeyNode(key);
        if (existingKey is not null)
        {
            // Indexer assignment updates the existing entry's value without moving its position -
            // this is what makes SetX calls preserve original key order for keys that already
            // existed in the file.
            this.Mapping.Children[existingKey] = value;
        }
        else
        {
            // A brand-new key is appended at the mapping's current end - calling every SetX method
            // in SkillFrontmatter's declared field order each time the block is regenerated keeps
            // multiple new keys appended in that same canonical order relative to each other.
            this.Mapping.Children.Add(new YamlScalarNode(key), value);
        }
    }

    private void RemoveKey(string key)
    {
        var existingKey = this.FindKeyNode(key);
        if (existingKey is not null)
        {
            this.Mapping.Children.Remove(existingKey);
        }
    }

    private bool TryFindValue(string key, out YamlNode value)
    {
        var keyNode = this.FindKeyNode(key);
        if (keyNode is not null)
        {
            value = this.Mapping.Children[keyNode];
            return true;
        }

        value = default!;
        return false;
    }

    private YamlScalarNode? FindKeyNode(string key) =>
        this.Mapping.Children.Keys
            .OfType<YamlScalarNode>()
            .FirstOrDefault(k => string.Equals(k.Value, key, StringComparison.OrdinalIgnoreCase));
}
