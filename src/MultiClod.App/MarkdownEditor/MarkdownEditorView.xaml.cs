using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace MultiClod.App.MarkdownEditor;

/// <summary>
/// Canvas content for a selected markdown file: owns the file path/save/discard lifecycle around
/// an embedded MarkdownRenderEditControl, which owns the actual view/raw-toggle+rendering UI.
/// Shared by both the Context tree (CLAUDE.md and its @imports) and any other plain-markdown
/// document - skills now go through SkillEditorView instead, which needs the frontmatter and body
/// saved together rather than as one flat file write.
/// </summary>
public partial class MarkdownEditorView : UserControl
{
    private readonly Button saveButton;

    private MarkdownEditorTarget? currentTarget;

    public MarkdownEditorView()
    {
        this.InitializeComponent();
        this.saveButton = (Button)this.RenderEdit.LeadingToolbarContent;
    }

    public bool IsDirty => this.RenderEdit.IsDirty;

    /// <summary>
    /// Reads the target's raw text fresh from disk (or starts empty in edit mode if the file
    /// doesn't exist yet - e.g. a not-yet-created @import), renders it, and resets to view mode -
    /// called by MainWindow whenever a different Context node is selected. Assumes the caller
    /// already confirmed any prior dirty edit via TryNavigateAway.
    /// </summary>
    internal void LoadDocument(MarkdownEditorTarget target)
    {
        this.currentTarget = target;

        var fileExists = File.Exists(target.FilePath);
        string rawText;
        try
        {
            rawText = fileExists ? File.ReadAllText(target.FilePath) : string.Empty;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(Window.GetWindow(this), $"Could not read '{target.DisplayName}': {ex.Message}",
                "Load Failed", MessageBoxButton.OK, MessageBoxImage.Error);
            rawText = string.Empty;
        }

        this.RenderEdit.DocumentDisplayName = target.DisplayName;
        // A not-yet-created file (e.g. a missing @import) has nothing to render in view mode, so it
        // opens straight into edit mode with an empty buffer instead.
        this.RenderEdit.SetOriginalText(rawText, startInEditMode: !fileExists);
    }

    /// <summary>
    /// Called before MainWindow switches to a different Context node, skill, or rail section.
    /// Prompts to discard unsaved edits; returns false (caller should stay put) only if the user
    /// declines to discard.
    /// </summary>
    internal bool TryNavigateAway() => this.RenderEdit.ConfirmDiscardIfDirty();

    /// <summary>
    /// Re-renders the currently loaded document's markdown so its colors match the just-applied
    /// theme - called by MainWindow.OnThemeChanged.
    /// </summary>
    internal void RefreshTheme() => this.RenderEdit.RefreshTheme();

    /// <summary>
    /// Raised after a successful Save, with the saved file's path. The only refresh trigger for the
    /// Context tree - there's no FileSystemWatcher.
    /// </summary>
    internal event EventHandler<string>? DocumentSaved;

    private void OnRenderEditDirtyChanged(object sender, EventArgs e) =>
        this.saveButton.IsEnabled = this.RenderEdit.IsDirty;

    private void OnRenderEditEditModeChanged(object sender, EventArgs e) =>
        this.saveButton.Visibility = this.RenderEdit.IsEditing ? Visibility.Visible : Visibility.Collapsed;

    private void OnSaveClick(object sender, RoutedEventArgs e) => this.Save();

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control && this.RenderEdit.IsEditing)
        {
            this.Save();
            e.Handled = true;
        }
    }

    private void Save()
    {
        if (this.currentTarget is not { } target)
        {
            return;
        }

        try
        {
            // Harmless no-op when the directory already exists; creates missing parents when this
            // is a brand-new nested @import path being filled in for the first time.
            Directory.CreateDirectory(Path.GetDirectoryName(target.FilePath)!);
            File.WriteAllText(target.FilePath, this.RenderEdit.Text);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(Window.GetWindow(this), $"Could not save '{target.DisplayName}': {ex.Message}",
                "Save Failed", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        this.RenderEdit.MarkSaved();
        this.saveButton.IsEnabled = false;
        this.DocumentSaved?.Invoke(this, target.FilePath);
    }
}
