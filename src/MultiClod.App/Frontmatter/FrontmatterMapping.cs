using YamlDotNet.RepresentationModel;

namespace MultiClod.App.Frontmatter;

/// <summary>
/// Ordered YAML mapping backing a Markdown file's frontmatter block, plus generic string/bool/
/// list-or-string get/set helpers keyed by name. Wraps a YamlDotNet YamlMappingNode - which
/// preserves insertion order on enumeration - rather than a plain dictionary, so an edit to one
/// field never reorders every other key. Has no curated field accessors of its own; feature-specific
/// types (e.g. Skills.SkillFrontmatter, OutputStyles.OutputStyleFrontmatter) subclass this to add
/// typed accessors for whichever fields they curate - any key without one simply stays in
/// <see cref="Mapping"/> untouched.
/// </summary>
internal class FrontmatterMapping
{
    // Above this many items, a list-or-string field is always written as a YAML block list, even
    // if the file previously had it as a flat string - a 5+ item space-separated line is hard to
    // read, and this is the one place shape-preservation is deliberately overridden.
    private const int DefaultFlatStringMaxItemCount = 4;

    internal FrontmatterMapping(YamlMappingNode mapping)
    {
        this.Mapping = mapping;
    }

    internal YamlMappingNode Mapping { get; }

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
    // scalars, matching what fields like allowed-tools/disallowed-tools/paths accept.
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

    internal void SetListOrString(string key, IReadOnlyList<string> values, int flatStringMaxItemCount = DefaultFlatStringMaxItemCount)
    {
        if (values.Count == 0)
        {
            this.RemoveKey(key);
            return;
        }

        YamlNode node = values.Count > flatStringMaxItemCount
            ? new YamlSequenceNode(values.Select(v => new YamlScalarNode(v)))
            : new YamlScalarNode(string.Join(' ', values));

        this.SetOrReplace(key, node);
    }

    internal void SetOrRemoveScalar(string key, string? value)
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
            // in a subclass's declared field order each time the block is regenerated keeps
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
