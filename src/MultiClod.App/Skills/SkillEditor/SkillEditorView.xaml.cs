using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace MultiClod.App.Skills.SkillEditor;

/// <summary>
/// Structured-fields-on-the-left, raw-YAML-plus-body-on-the-right editor for a SKILL.md - replaces
/// MarkdownEditorView for skills specifically, wherever a skill is selected (Skills\SkillsList or
/// SessionSkillsList) or newly created ("Add new" on a Skills heading). Plain UserControl with
/// code-behind logic, matching MarkdownEditorView/SettingsView - no ViewModel layer.
///
/// The left panel's ten curated fields and the top-right frontmatter YAML textarea are kept in
/// sync live, on every keystroke rather than waiting for a field to lose focus: a left-panel edit
/// regenerates the textarea immediately (unless it currently has keyboard focus, so in-progress
/// typing there is never clobbered), and the textarea pushes back into the left panel as soon as
/// its text parses as a YAML mapping - while it doesn't parse, the left panel just keeps its
/// last-known-good values, an inline error shows, and Save is blocked (see
/// SyncFrontmatterFromLeftPanel/OnFrontmatterYamlTextChanged). This stays acyclic because only one
/// side is ever mid-edit at a time (WPF focus is exclusive) and every programmatic write to the
/// other side is wrapped in a suppress flag that blocks its own change handlers from re-firing.
/// </summary>
public partial class SkillEditorView : UserControl
{
    private SkillDocument? document;
    private bool isStructuralMode;
    private bool suppressFieldEvents;
    private bool suppressYamlBoxSync;
    private bool leftPanelDirty;
    private bool yamlBoxDirty;
    private bool frontmatterYamlValid = true;

    public SkillEditorView()
    {
        this.InitializeComponent();
        this.ModelCombo.ItemsSource = KnownModelAliases.Values;
    }

    public bool IsDirty => this.leftPanelDirty || this.yamlBoxDirty || this.BodyRenderEdit.IsDirty;

    internal event EventHandler<string>? DocumentSaved;

    /// <summary>Loads an existing skill from disk - called whenever a skill is selected.</summary>
    internal void LoadDocument(string filePath) => this.LoadInternal(SkillDocument.Load(filePath));

    /// <summary>
    /// Starts a blank, not-yet-saved skill under <paramref name="skillsRoot"/> (the personal
    /// `~/.claude/skills` directory or a specific project's `.claude/skills`) - called by "Add new"
    /// on a Skills heading. The directory + SKILL.md are only created on the first Save.
    /// </summary>
    internal void LoadNewDocument(string skillsRoot)
    {
        this.LoadInternal(SkillDocument.CreateNew(skillsRoot));
        this.FolderNameBox.Focus();
    }

    /// <summary>
    /// Called before MainWindow switches to a different skill, Context node, or rail section.
    /// Prompts to discard unsaved edits; returns false (caller should stay put) only if the user
    /// declines. Doesn't need to revert this control's own contents on confirmation - per the
    /// existing app-wide convention, the caller only calls this immediately before loading whatever
    /// comes next, which overwrites everything here anyway.
    /// </summary>
    internal bool TryNavigateAway()
    {
        if (!this.IsDirty)
        {
            return true;
        }

        var result = MessageBox.Show(
            Window.GetWindow(this),
            "Discard unsaved changes to this skill?",
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

    private void LoadInternal(SkillDocument loaded)
    {
        this.document = loaded;
        this.isStructuralMode = loaded.RawFrontmatterBlock is not null;

        this.FolderNameBox.IsReadOnly = !loaded.IsNew;
        this.FolderNameBox.Text = loaded.IsNew ? string.Empty : Path.GetFileName(loaded.FolderPath);
        this.HideFolderNameError();

        this.StructuredFieldsPanel.IsEnabled = !this.isStructuralMode;
        this.StructuralFallbackNotice.Visibility = this.isStructuralMode ? Visibility.Visible : Visibility.Collapsed;

        this.PopulateStructuredFieldsFromFrontmatter();
        this.RefreshFrontmatterYamlBoxFromDocument();

        this.BodyRenderEdit.DocumentDisplayName = "this skill's body";
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
        this.WhenToUseBox.Text = frontmatter.WhenToUse ?? string.Empty;
        this.ArgumentHintBox.Text = frontmatter.ArgumentHint ?? string.Empty;
        this.DisableModelInvocationToggle.IsChecked = frontmatter.DisableModelInvocation;
        this.UserInvocableToggle.IsChecked = frontmatter.UserInvocable;
        this.AllowedToolsBox.Text = string.Join(' ', frontmatter.AllowedTools);
        this.DisallowedToolsBox.Text = string.Join(' ', frontmatter.DisallowedTools);
        this.ModelCombo.Text = frontmatter.Model ?? string.Empty;
        this.SelectEffort(frontmatter.Effort);
        this.suppressFieldEvents = false;

        this.UpdateAllFieldDimming();
    }

    private void SelectEffort(string? effort)
    {
        foreach (var item in this.EffortCombo.Items)
        {
            if (item is ComboBoxItem { Content: string content } && string.Equals(content, effort ?? string.Empty, StringComparison.OrdinalIgnoreCase))
            {
                this.EffortCombo.SelectedItem = item;
                return;
            }
        }

        this.EffortCombo.SelectedIndex = 0;
    }

    private string? GetSelectedEffort() =>
        this.EffortCombo.SelectedItem is ComboBoxItem { Content: string { Length: > 0 } content } ? content : null;

    private static IReadOnlyList<string> SplitToolsList(string text) =>
        text.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

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

    private void OnFieldCommitted(object sender, SelectionChangedEventArgs e) => this.CommitFieldChange();

    // Editable ComboBoxes (Model) raise neither TextChanged nor SelectionChanged while the user is
    // typing rather than picking an item - KeyUp is what catches that so typed text still syncs
    // immediately, matching every other field.
    private void OnFieldCommitted(object sender, KeyEventArgs e) => this.CommitFieldChange();

    private void CommitFieldChange()
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
        frontmatter.SetWhenToUse(this.WhenToUseBox.Text);
        frontmatter.SetArgumentHint(this.ArgumentHintBox.Text);
        frontmatter.SetDisableModelInvocation(this.DisableModelInvocationToggle.IsChecked == true);
        frontmatter.SetUserInvocable(this.UserInvocableToggle.IsChecked == true);
        frontmatter.SetAllowedTools(SplitToolsList(this.AllowedToolsBox.Text));
        frontmatter.SetDisallowedTools(SplitToolsList(this.DisallowedToolsBox.Text));
        frontmatter.SetModel(this.ModelCombo.Text);
        frontmatter.SetEffort(this.GetSelectedEffort());

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
            : SkillFrontmatterYaml.SerializeBlock(this.document.Frontmatter);

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

        if (!SkillFrontmatterYaml.TryParseBlock(this.FrontmatterYamlBox.Text, out var parsed))
        {
            this.frontmatterYamlValid = false;
            this.ShowFrontmatterYamlError("Invalid YAML - fix this before saving.");
            this.UpdateSaveEnabled();
            return;
        }

        this.frontmatterYamlValid = true;
        this.HideFrontmatterYamlError();

        // The textarea is the source of truth for anything not in the curated fields - a valid
        // edit there replaces the whole mapping, so unrecognized keys the user typed survive.
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

    // --- Folder name (new skills only) ---------------------------------------------------------

    private void OnFolderNameTextChanged(object sender, TextChangedEventArgs e)
    {
        if (this.suppressFieldEvents)
        {
            return;
        }

        var sanitized = SkillFolderNameValidator.Sanitize(this.FolderNameBox.Text);
        if (sanitized != this.FolderNameBox.Text)
        {
            this.suppressFieldEvents = true;
            var caret = this.FolderNameBox.CaretIndex;
            this.FolderNameBox.Text = sanitized;
            this.FolderNameBox.CaretIndex = Math.Min(caret, sanitized.Length);
            this.suppressFieldEvents = false;
        }

        this.HideFolderNameError();
        this.leftPanelDirty = true;
        this.UpdateSaveEnabled();
    }

    private void ShowFolderNameError(string message)
    {
        this.FolderNameErrorText.Text = message;
        this.FolderNameErrorText.Visibility = Visibility.Visible;
    }

    private void HideFolderNameError() => this.FolderNameErrorText.Visibility = Visibility.Collapsed;

    // --- Dimming (default vs. non-default values) ----------------------------------------------

    private void UpdateAllFieldDimming()
    {
        foreach (var control in new Control[]
                 {
                     this.NameBox, this.DescriptionBox, this.WhenToUseBox, this.ArgumentHintBox,
                     this.AllowedToolsBox, this.DisallowedToolsBox, this.ModelCombo, this.EffortCombo,
                 })
        {
            this.UpdateFieldDimming(control);
        }

        this.DisableModelInvocationLabel.Foreground = this.ResolveForeground(isDefault: this.DisableModelInvocationToggle.IsChecked != true);
        this.UserInvocableLabel.Foreground = this.ResolveForeground(isDefault: this.UserInvocableToggle.IsChecked == true);
    }

    private void UpdateFieldDimming(Control control)
    {
        var isDefault = control switch
        {
            ComboBox { IsEditable: true } editableCombo => string.IsNullOrEmpty(editableCombo.Text),
            ComboBox fixedCombo => fixedCombo.SelectedIndex <= 0,
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
        // A brand-new skill has nothing on disk yet, so it's always worth allowing Save once the
        // YAML is valid, even before anything has technically been "changed" from its blank state.
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

        string? newFolderName = null;
        if (this.document.IsNew)
        {
            var sanitized = SkillFolderNameValidator.Sanitize(this.FolderNameBox.Text);
            if (!SkillFolderNameValidator.TryValidate(sanitized, this.document.FolderPath, out var folderError))
            {
                this.ShowFolderNameError(folderError!);
                return;
            }

            newFolderName = sanitized;
        }

        try
        {
            if (this.isStructuralMode)
            {
                this.document.SaveRaw(this.FrontmatterYamlBox.Text, this.BodyRenderEdit.Text, newFolderName);
            }
            else
            {
                this.document.Body = this.BodyRenderEdit.Text;
                this.document.Save(newFolderName);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(Window.GetWindow(this), $"Could not save this skill: {ex.Message}",
                "Save Failed", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        this.FolderNameBox.IsReadOnly = true;
        this.BodyRenderEdit.MarkSaved();
        this.leftPanelDirty = false;
        this.yamlBoxDirty = false;
        this.UpdateSaveEnabled();
        this.DocumentSaved?.Invoke(this, this.document.FilePath);
    }
}
