using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

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

    /// <summary>
    /// Re-renders the currently loaded skill's markdown so its colors match the just-applied
    /// theme - called by MainWindow.OnThemeChanged. A no-op if no skill is loaded (the view mode
    /// FlowDocumentScrollViewer/RawEditor's own DynamicResource-bound colors update on their own).
    /// </summary>
    internal void RefreshTheme()
    {
        if (this.currentSkill is not null)
        {
            this.RenderMarkdown(this.RawEditor.Text);
        }
    }

    // Markdig.Wpf's default styles (App.xaml's generic.xaml merge) set Foreground on each block
    // (heading/paragraph) style rather than relying on inheritance, so setting it once on the
    // FlowDocument itself doesn't cascade - those per-block style values win over it. Overriding
    // Foreground as a local value on every block instead beats the style setter. A live property
    // (not a cached field) so it reflects whatever theme is current at each RenderMarkdown call -
    // the FlowDocument itself is built once per render, not resource-bound, so it can't just pick
    // up a DynamicResource change on its own; see RefreshTheme.
    private static Brush MarkdownForeground =>
        (Brush)Application.Current.Resources["Theme.Brush.MarkdownForeground"];

    // Code spans/blocks (Markdig's CodeStyleKey / CodeBlockStyleKey) only set their own light
    // Background, relying on inheritance for Foreground - previously that inherited the document's
    // default black, giving readable dark-on-light. Now that everything else gets an explicit light
    // Foreground of its own, that would inherit into code areas too and make the text invisible
    // against their own light Background, so anything with its own Background needs this forced
    // dark Foreground instead.
    private static readonly Brush CodeForeground =
        (Brush)new BrushConverter().ConvertFromString("#FF1E1E1E")!;

    private void RenderMarkdown(string rawText)
    {
        var document = Markdig.Wpf.Markdown.ToFlowDocument(rawText);
        document.Foreground = MarkdownForeground;
        ApplyForegroundToBlocks(document.Blocks, insideOwnBackground: false);
        this.MarkdownViewer.Document = document;
    }

    // Fenced code blocks put the Background on an outer Section, not on the Paragraph(s) inside it,
    // so checking Background only on the element being visited isn't enough - insideOwnBackground
    // propagates down through the whole subtree once any ancestor is found to carry its own
    // Background, so everything inside consistently gets the dark, code-appropriate Foreground.
    private static void ApplyForegroundToBlocks(IEnumerable<Block> blocks, bool insideOwnBackground)
    {
        foreach (var block in blocks)
        {
            var ownBackground = insideOwnBackground || block.Background is not null;
            block.Foreground = ownBackground ? CodeForeground : MarkdownForeground;

            switch (block)
            {
                case Paragraph paragraph:
                    ApplyForegroundToInlines(paragraph.Inlines, ownBackground);
                    break;
                case Section section:
                    ApplyForegroundToBlocks(section.Blocks, ownBackground);
                    break;
                case List list:
                    foreach (var item in list.ListItems)
                    {
                        ApplyForegroundToBlocks(item.Blocks, ownBackground);
                    }
                    break;
                case Table table:
                    foreach (var rowGroup in table.RowGroups)
                    {
                        foreach (var row in rowGroup.Rows)
                        {
                            foreach (var cell in row.Cells)
                            {
                                var cellOwnBackground = ownBackground || cell.Background is not null;
                                cell.Foreground = cellOwnBackground ? CodeForeground : MarkdownForeground;
                                ApplyForegroundToBlocks(cell.Blocks, cellOwnBackground);
                            }
                        }
                    }
                    break;
            }
        }
    }

    private static void ApplyForegroundToInlines(IEnumerable<Inline> inlines, bool insideOwnBackground)
    {
        foreach (var inline in inlines)
        {
            if (!insideOwnBackground && inline.Background is not null)
            {
                inline.Foreground = CodeForeground;
            }

            if (inline is Span span)
            {
                ApplyForegroundToInlines(span.Inlines, insideOwnBackground);
            }
        }
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
