using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace MultiClod.App.Skills;

/// <summary>
/// Canvas content for a selected skill: a read-only Markdig.Wpf-rendered view by default, with a
/// toggle to a raw-text edit mode (Save button + Ctrl+S) that writes back to the SKILL.md file.
/// The only file in this app that references Markdig.Wpf types, mirroring how WpfTerminalPane is
/// the only file bridging Microsoft.Terminal.Wpf into this app's own abstractions.
/// </summary>
public partial class SkillDetailView : UserControl
{
    private SkillInfo? currentSkill;
    private string originalRawText = string.Empty;
    private bool suppressDirtyCheck;

    public SkillDetailView()
    {
        this.InitializeComponent();
    }

    public bool IsDirty { get; private set; }

    /// <summary>
    /// Reads the skill's raw text fresh from disk, renders it, and resets to view mode - called by
    /// MainWindow whenever a different skill is selected. Assumes the caller already confirmed any
    /// prior dirty edit via TryNavigateAway.
    /// </summary>
    internal void LoadSkill(SkillInfo info)
    {
        this.currentSkill = info;

        string rawText;
        try
        {
            rawText = File.ReadAllText(info.FilePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(Window.GetWindow(this), $"Could not read '{info.Name}': {ex.Message}",
                "Skill Load Failed", MessageBoxButton.OK, MessageBoxImage.Error);
            rawText = string.Empty;
        }

        this.originalRawText = rawText;

        this.suppressDirtyCheck = true;
        this.RawEditor.Text = rawText;
        this.suppressDirtyCheck = false;

        this.RenderMarkdown(rawText);
        this.SetEditMode(false);
        this.UpdateDirtyState();
    }

    /// <summary>
    /// Called before MainWindow switches to a different skill or rail section. Prompts to discard
    /// unsaved edits; returns false (caller should stay put) only if the user declines to discard.
    /// </summary>
    internal bool TryNavigateAway()
    {
        if (!this.IsDirty)
        {
            return true;
        }

        if (!this.ConfirmDiscard())
        {
            return false;
        }

        this.IsDirty = false;
        return true;
    }

    private void RenderMarkdown(string rawText)
    {
        this.MarkdownViewer.Document = Markdig.Wpf.Markdown.ToFlowDocument(rawText);
    }

    private void SetEditMode(bool editing)
    {
        this.EditToggle.IsChecked = editing;
        this.MarkdownViewer.Visibility = editing ? Visibility.Collapsed : Visibility.Visible;
        this.RawEditor.Visibility = editing ? Visibility.Visible : Visibility.Collapsed;
        this.SaveButton.Visibility = editing ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnEditToggleClick(object sender, RoutedEventArgs e)
    {
        // ToggleButton already flipped IsChecked before Click fires, so this reflects the state
        // being entered, not the one being left.
        var enteringEdit = this.EditToggle.IsChecked == true;
        if (!enteringEdit && this.IsDirty && !this.ConfirmDiscard())
        {
            this.EditToggle.IsChecked = true;
            return;
        }

        if (!enteringEdit && this.IsDirty)
        {
            // Confirmed discard - revert the buffer to the last-saved text rather than leaving the
            // (now-hidden) edit behind for the next toggle-into-edit to resurrect.
            this.suppressDirtyCheck = true;
            this.RawEditor.Text = this.originalRawText;
            this.suppressDirtyCheck = false;
            this.IsDirty = false;
            this.SaveButton.IsEnabled = false;
        }

        this.SetEditMode(enteringEdit);
    }

    private void OnRawEditorTextChanged(object sender, TextChangedEventArgs e)
    {
        if (this.suppressDirtyCheck)
        {
            return;
        }

        this.UpdateDirtyState();
    }

    private void UpdateDirtyState()
    {
        this.IsDirty = this.RawEditor.Text != this.originalRawText;
        this.SaveButton.IsEnabled = this.IsDirty;
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        this.Save();
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control && this.RawEditor.Visibility == Visibility.Visible)
        {
            this.Save();
            e.Handled = true;
        }
    }

    private void Save()
    {
        if (this.currentSkill is not { } skill)
        {
            return;
        }

        try
        {
            File.WriteAllText(skill.FilePath, this.RawEditor.Text);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(Window.GetWindow(this), $"Could not save '{skill.Name}': {ex.Message}",
                "Save Failed", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        this.originalRawText = this.RawEditor.Text;
        this.UpdateDirtyState();
        this.RenderMarkdown(this.originalRawText);
    }

    private bool ConfirmDiscard()
    {
        var name = this.currentSkill?.Name ?? "this skill";
        var result = MessageBox.Show(
            Window.GetWindow(this),
            $"Discard unsaved changes to '{name}'?",
            "Unsaved Changes",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        return result == MessageBoxResult.Yes;
    }
}
