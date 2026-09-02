using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace MultiClod.App.MarkdownEditor;

/// <summary>
/// The view/raw-edit toggle plus Markdig-rendered preview surface, extracted out of
/// MarkdownEditorView so it can be shared with SkillEditorView's body pane. Has no file I/O and no
/// Save button of its own - a host supplies text via SetOriginalText, reads it back via Text, and
/// reacts to DirtyChanged/EditModeChanged to drive its own Save affordance. Discard-confirmation on
/// leaving edit mode (the toggle) or switching to a different document (ConfirmDiscardIfDirty)
/// stays here since it only concerns this control's own buffer; a host just supplies
/// DocumentDisplayName for the confirmation dialog's wording.
/// </summary>
public partial class MarkdownRenderEditControl : UserControl
{
    private string originalText = string.Empty;
    private bool suppressDirtyCheck;

    public MarkdownRenderEditControl()
    {
        this.InitializeComponent();
    }

    public string Text => this.RawEditor.Text;

    public bool IsDirty { get; private set; }

    public bool IsEditing { get; private set; }

    public string DocumentDisplayName { get; set; } = "this document";

    /// <summary>
    /// Content shown left of the "Edit" label/toggle, in the same top-right row - lets a host
    /// (MarkdownEditorView's own Save button) share that row without this control needing to know
    /// anything about it. Set via XAML property-element syntax:
    /// &lt;MarkdownRenderEditControl.LeadingToolbarContent&gt;.
    /// </summary>
    public object? LeadingToolbarContent
    {
        get => this.LeadingToolbarContentPresenter.Content;
        set => this.LeadingToolbarContentPresenter.Content = value;
    }

    public event EventHandler? DirtyChanged;

    public event EventHandler? EditModeChanged;

    /// <summary>
    /// Resets the dirty-tracking baseline to <paramref name="text"/>, renders it, and switches
    /// mode - called whenever a host loads a new/different document. <paramref
    /// name="startInEditMode"/> mirrors MarkdownEditorView's "not-yet-created file" case, where
    /// there's nothing to render in view mode.
    /// </summary>
    public void SetOriginalText(string text, bool startInEditMode = false)
    {
        this.originalText = text;

        this.suppressDirtyCheck = true;
        this.RawEditor.Text = text;
        this.suppressDirtyCheck = false;

        this.RenderMarkdown(text);
        this.SetEditMode(startInEditMode);
        this.UpdateDirtyState();
    }

    /// <summary>
    /// Called by a host after it has successfully written <see cref="Text"/> to disk - moves the
    /// dirty-tracking baseline forward without touching edit/view mode (a host's own Save button
    /// doesn't force the user back to view mode, matching MarkdownEditorView's existing Save()).
    /// </summary>
    public void MarkSaved()
    {
        this.originalText = this.RawEditor.Text;
        this.RenderMarkdown(this.originalText);
        this.UpdateDirtyState();
    }

    /// <summary>
    /// If dirty, prompts to discard and, on confirmation, reverts the buffer to the last saved
    /// text; returns false only if the user declines. A host calls this before switching to a
    /// different document (MarkdownEditorView.TryNavigateAway) or lets the Edit toggle call it
    /// directly when leaving edit mode.
    /// </summary>
    public bool ConfirmDiscardIfDirty()
    {
        if (!this.IsDirty)
        {
            return true;
        }

        var result = MessageBox.Show(
            Window.GetWindow(this),
            $"Discard unsaved changes to '{this.DocumentDisplayName}'?",
            "Unsaved Changes",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes)
        {
            return false;
        }

        this.suppressDirtyCheck = true;
        this.RawEditor.Text = this.originalText;
        this.suppressDirtyCheck = false;
        this.RenderMarkdown(this.originalText);
        this.UpdateDirtyState();
        return true;
    }

    /// <summary>
    /// Re-renders the currently loaded text so its colors match the just-applied theme - called by
    /// a host's own OnThemeChanged.
    /// </summary>
    public void RefreshTheme() => this.RenderMarkdown(this.RawEditor.Text);

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
        this.IsEditing = editing;
        this.EditModeChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnEditToggleClick(object sender, RoutedEventArgs e)
    {
        // ToggleButton already flipped IsChecked before Click fires, so this reflects the state
        // being entered, not the one being left.
        var enteringEdit = this.EditToggle.IsChecked == true;
        if (!enteringEdit && !this.ConfirmDiscardIfDirty())
        {
            this.EditToggle.IsChecked = true;
            return;
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
        var wasDirty = this.IsDirty;
        this.IsDirty = this.RawEditor.Text != this.originalText;
        if (this.IsDirty != wasDirty)
        {
            this.DirtyChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
