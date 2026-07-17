namespace MultiClod.App.MarkdownEditor;

/// <summary>
/// Identifies what MarkdownEditorView is currently showing - a plain file path plus whatever name
/// should appear in load/save error dialogs and the unsaved-changes prompt. Deliberately not tied
/// to SkillInfo or any Context-tree type, since both features route through the same editor.
/// </summary>
internal readonly record struct MarkdownEditorTarget(string FilePath, string DisplayName);
