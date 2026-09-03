using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace MultiClod.App.OutputStyles.OutputStyleEditor;

/// <summary>
/// Structured-fields-on-the-left, raw-YAML-plus-body-on-the-right editor for an output-style
/// Markdown file - replaces MarkdownEditorView for output styles specifically, wherever one is
/// selected (OutputStylesList or SessionOutputStylesList) or newly created ("Add new" on an Output
/// Styles heading). Plain UserControl with code-behind logic, matching MarkdownEditorView/
/// Skills.SkillEditor.SkillEditorView - no ViewModel layer.
///
/// The left panel's three curated fields and the top-right frontmatter YAML textarea are kept in
/// sync live, on every keystroke rather than waiting for a field to lose focus - identical rule to
/// SkillEditorView (see that type's class remarks for the full acyclic-sync rationale): a
/// left-panel edit regenerates the textarea immediately (unless it currently has keyboard focus),
/// and the textarea pushes back into the left panel as soon as its text parses as a YAML mapping.
/// </summary>
public partial class OutputStyleEditorView : UserControl
{
    private OutputStyleDocument? document;
    private bool isStructuralMode;
    private bool suppressFieldEvents;
    private bool suppressYamlBoxSync;
    private bool leftPanelDirty;
    private bool yamlBoxDirty;
    private bool frontmatterYamlValid = true;

    public OutputStyleEditorView()
    {
        this.InitializeComponent();
    }

    public bool IsDirty => this.leftPanelDirty || this.yamlBoxDirty || this.BodyRenderEdit.IsDirty;

    internal event EventHandler<string>? DocumentSaved;

    /// <summary>Loads an existing output style from disk - called whenever one is selected.</summary>
    internal void LoadDocument(string filePath) => this.LoadInternal(OutputStyleDocument.Load(filePath));

    /// <summary>
    /// Starts a blank, not-yet-saved output style under <paramref name="outputStylesRoot"/> (the
    /// personal `~/.claude/output-styles` directory or a specific project's
    /// `.claude/output-styles`) - called by "Add new" on an Output Styles heading. The file is only
    /// created on the first Save.
    /// </summary>
    internal void LoadNewDocument(string outputStylesRoot)
    {
        this.LoadInternal(OutputStyleDocument.CreateNew(outputStylesRoot));
        this.FileNameBox.Focus();
    }

    /// <summary>
    /// Called before MainWindow switches to a different output style, Context node, or rail
    /// section. Prompts to discard unsaved edits; returns false (caller should stay put) only if
    /// the user declines.
    /// </summary>
    internal bool TryNavigateAway()
    {
        if (!this.IsDirty)
        {
            return true;
        }

        var result = MessageBox.Show(
            Window.GetWindow(this),
            "Discard unsaved changes to this output style?",
            "Unsaved Changes",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes)
        {
            return false;
        }

        this.leftPanelDirty = false;
        this.yamlBoxDirty = false;
        return true;
    }

    internal void RefreshTheme()
    {
        this.BodyRenderEdit.RefreshTheme();
        this.UpdateAllFieldDimming();
    }

    private void LoadInternal(OutputStyleDocument loaded)
    {
        this.document = loaded;
        this.isStructuralMode = loaded.RawFrontmatterBlock is not null;

        this.FileNameBox.IsReadOnly = !loaded.IsNew;
        this.FileNameBox.Text = loaded.IsNew ? string.Empty : loaded.FileNameWithoutExtension;
        this.HideFileNameError();

        this.StructuredFieldsPanel.IsEnabled = !this.isStructuralMode;
        this.StructuralFallbackNotice.Visibility = this.isStructuralMode ? Visibility.Visible : Visibility.Collapsed;

        this.PopulateStructuredFieldsFromFrontmatter();
        this.RefreshFrontmatterYamlBoxFromDocument();

        this.BodyRenderEdit.DocumentDisplayName = "this output style's body";
        this.BodyRenderEdit.SetOriginalText(loaded.Body, startInEditMode: loaded.IsNew);

        this.leftPanelDirty = false;
        this.yamlBoxDirty = false;
        this.frontmatterYamlValid = true;
        this.UpdateSaveEnabled();
    }

    private void PopulateStructuredFieldsFromFrontmatter()
    {
        if (this.document is null)
        {
            return;
        }

        var frontmatter = this.document.Frontmatter;

        this.suppressFieldEvents = true;
        this.NameBox.Text = frontmatter.Name ?? string.Empty;
        this.DescriptionBox.Text = frontmatter.Description ?? string.Empty;
        this.KeepCodingInstructionsToggle.IsChecked = frontmatter.KeepCodingInstructions;
        this.suppressFieldEvents = false;

        this.UpdateAllFieldDimming();
    }

    // --- Left-panel field change handlers -----------------------------------------------------

    private void OnTextFieldChanging(object sender, TextChangedEventArgs e)
    {
        if (this.suppressFieldEvents)
        {
            return;
        }

        this.leftPanelDirty = true;
        this.UpdateFieldDimming((Control)sender);
        this.SyncFrontmatterFromLeftPanel();
    }

    private void OnToggleFieldCommitted(object sender, RoutedEventArgs e)
    {
        if (this.suppressFieldEvents)
        {
            return;
        }

        this.leftPanelDirty = true;
        this.UpdateAllFieldDimming();
        this.SyncFrontmatterFromLeftPanel();
    }

    private void SyncFrontmatterFromLeftPanel()
    {
        if (this.document is null || this.isStructuralMode)
        {
            return;
        }

        // Never overwrite the textarea while the user is actively typing in it - see this
        // control's class-level remarks on the acyclic sync rule.
        if (this.FrontmatterYamlBox.IsKeyboardFocusWithin)
        {
            return;
        }

        var frontmatter = this.document.Frontmatter;
        frontmatter.SetName(this.NameBox.Text);
        frontmatter.SetDescription(this.DescriptionBox.Text);
        frontmatter.SetKeepCodingInstructions(this.KeepCodingInstructionsToggle.IsChecked == true);

        this.RefreshFrontmatterYamlBoxFromDocument();
        this.UpdateSaveEnabled();
    }

    private void RefreshFrontmatterYamlBoxFromDocument()
    {
        if (this.document is null)
        {
            return;
        }

        var text = this.isStructuralMode
            ? this.document.RawFrontmatterBlock ?? string.Empty
            : OutputStyleFrontmatterYaml.SerializeBlock(this.document.Frontmatter);

        this.suppressYamlBoxSync = true;
        this.FrontmatterYamlBox.Text = text;
        this.suppressYamlBoxSync = false;
    }

    // --- Frontmatter YAML textarea -------------------------------------------------------------

    // Validated and synced into the left panel on every keystroke, not just on blur - as long as
    // the current text parses, the fields to the left reflect it immediately; while it doesn't
    // parse, the left panel just keeps showing its last-known-good values and Save stays blocked
    // (see UpdateSaveEnabled), with the specific error shown live too.
    private void OnFrontmatterYamlTextChanged(object sender, TextChangedEventArgs e)
    {
        if (this.suppressYamlBoxSync)
        {
            return;
        }

        this.yamlBoxDirty = true;

        if (this.document is null || this.isStructuralMode)
        {
            this.UpdateSaveEnabled();
            return;
        }

        if (!OutputStyleFrontmatterYaml.TryParseBlock(this.FrontmatterYamlBox.Text, out var parsed))
        {
            this.frontmatterYamlValid = false;
            this.ShowFrontmatterYamlError("Invalid YAML - fix this before saving.");
            this.UpdateSaveEnabled();
            return;
        }

        this.frontmatterYamlValid = true;
        this.HideFrontmatterYamlError();

        // The textarea is the source of truth for anything not in the curated fields - a valid
        // edit there replaces the whole mapping, so unrecognized keys (e.g. force-for-plugin) the
        // user typed survive.
        this.document.Frontmatter = parsed!;
        this.PopulateStructuredFieldsFromFrontmatter();
        this.UpdateSaveEnabled();
    }

    private void ShowFrontmatterYamlError(string message)
    {
        this.FrontmatterYamlErrorText.Text = message;
        this.FrontmatterYamlErrorText.Visibility = Visibility.Visible;
    }

    private void HideFrontmatterYamlError() => this.FrontmatterYamlErrorText.Visibility = Visibility.Collapsed;

    // --- File name (new output styles only) -----------------------------------------------------

    private void OnFileNameTextChanged(object sender, TextChangedEventArgs e)
    {
        if (this.suppressFieldEvents)
        {
            return;
        }

        var sanitized = OutputStyleFileNameValidator.Sanitize(this.FileNameBox.Text);
        if (sanitized != this.FileNameBox.Text)
        {
            this.suppressFieldEvents = true;
            var caret = this.FileNameBox.CaretIndex;
            this.FileNameBox.Text = sanitized;
            this.FileNameBox.CaretIndex = Math.Min(caret, sanitized.Length);
            this.suppressFieldEvents = false;
        }

        this.HideFileNameError();
        this.leftPanelDirty = true;
        this.UpdateSaveEnabled();
    }

    private void ShowFileNameError(string message)
    {
        this.FileNameErrorText.Text = message;
        this.FileNameErrorText.Visibility = Visibility.Visible;
    }

    private void HideFileNameError() => this.FileNameErrorText.Visibility = Visibility.Collapsed;

    // --- Dimming (default vs. non-default values) ----------------------------------------------

    private void UpdateAllFieldDimming()
    {
        foreach (var control in new Control[] { this.NameBox, this.DescriptionBox })
        {
            this.UpdateFieldDimming(control);
        }

        this.KeepCodingInstructionsLabel.Foreground = this.ResolveForeground(isDefault: this.KeepCodingInstructionsToggle.IsChecked != true);
    }

    private void UpdateFieldDimming(Control control)
    {
        var isDefault = control switch
        {
            TextBox textBox => string.IsNullOrEmpty(textBox.Text),
            _ => false,
        };

        control.Foreground = this.ResolveForeground(isDefault);
    }

    private Brush ResolveForeground(bool isDefault) =>
        (Brush)this.FindResource(isDefault ? "Theme.Brush.SecondaryForeground" : "Theme.Brush.PrimaryForeground");

    // --- Save / dirty state ---------------------------------------------------------------------

    private void OnBodyRenderEditDirtyChanged(object sender, EventArgs e) => this.UpdateSaveEnabled();

    private void UpdateSaveEnabled()
    {
        if (this.document is null)
        {
            this.SaveButton.IsEnabled = false;
            return;
        }

        var yamlOk = this.isStructuralMode || this.frontmatterYamlValid;
        // A brand-new output style has nothing on disk yet, so it's always worth allowing Save
        // once the YAML is valid, even before anything has technically been "changed" from its
        // blank state.
        this.SaveButton.IsEnabled = yamlOk && (this.document.IsNew || this.IsDirty);
    }

    private void OnSaveClick(object sender, RoutedEventArgs e) => this.Save();

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control && this.SaveButton.IsEnabled)
        {
            this.Save();
            e.Handled = true;
        }
    }

    private void Save()
    {
        if (this.document is null)
        {
            return;
        }

        string? newFileName = null;
        if (this.document.IsNew)
        {
            var sanitized = OutputStyleFileNameValidator.Sanitize(this.FileNameBox.Text);
            if (!OutputStyleFileNameValidator.TryValidate(sanitized, this.document.DirectoryPath, out var fileNameError))
            {
                this.ShowFileNameError(fileNameError!);
                return;
            }

            newFileName = sanitized;
        }

        try
        {
            if (this.isStructuralMode)
            {
                this.document.SaveRaw(this.FrontmatterYamlBox.Text, this.BodyRenderEdit.Text, newFileName);
            }
            else
            {
                this.document.Body = this.BodyRenderEdit.Text;
                this.document.Save(newFileName);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(Window.GetWindow(this), $"Could not save this output style: {ex.Message}",
                "Save Failed", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        this.FileNameBox.IsReadOnly = true;
        this.BodyRenderEdit.MarkSaved();
        this.leftPanelDirty = false;
        this.yamlBoxDirty = false;
        this.UpdateSaveEnabled();
        this.DocumentSaved?.Invoke(this, this.document.FilePath);
    }
}
