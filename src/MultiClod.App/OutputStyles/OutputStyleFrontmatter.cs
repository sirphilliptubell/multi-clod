using MultiClod.App.Frontmatter;
using YamlDotNet.RepresentationModel;

namespace MultiClod.App.OutputStyles;

/// <summary>
/// An output-style Markdown file's frontmatter block: the generic ordered-mapping engine
/// (Frontmatter.FrontmatterMapping) plus typed get/set helpers for the curated fields the Output
/// Style editor (OutputStyles\OutputStyleEditor) renders as structured controls -
/// https://code.claude.com/docs/en/output-styles.md. <see cref="ForceForPluginKey"/> has no
/// accessor - it only does anything for a plugin-shipped output style, which this app never edits
/// (personal/project roots only) - so it stays in Mapping untouched, same as any unrecognized key.
/// </summary>
internal sealed class OutputStyleFrontmatter : FrontmatterMapping
{
    internal const string NameKey = "name";
    internal const string DescriptionKey = "description";
    internal const string KeepCodingInstructionsKey = "keep-coding-instructions";
    internal const string ForceForPluginKey = "force-for-plugin";

    internal OutputStyleFrontmatter(YamlMappingNode mapping)
        : base(mapping)
    {
    }

    internal static OutputStyleFrontmatter CreateEmpty() => new(new YamlMappingNode());

    internal string? Name => this.GetString(NameKey);

    internal string? Description => this.GetString(DescriptionKey);

    internal bool KeepCodingInstructions => this.GetBool(KeepCodingInstructionsKey, defaultValue: false);

    internal void SetName(string? value) => this.SetOrRemoveScalar(NameKey, value);

    internal void SetDescription(string? value) => this.SetOrRemoveScalar(DescriptionKey, value);

    internal void SetKeepCodingInstructions(bool value) =>
        this.SetOrRemoveScalar(KeepCodingInstructionsKey, value ? "true" : null);
}
