using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using MultiClod.App.Persistence;
using MultiClod.App.Theming;

namespace MultiClod.App.Settings;

/// <summary>
/// Canvas content for the Settings rail section: a plain list of toggles/fields, applied
/// immediately (no separate Save step, unlike SkillDetailView) - each one is just a persisted
/// value, not an edit a user could accidentally lose. New settings are added here as more controls
/// bound to AppSettings properties, same pattern as the ones below.
/// </summary>
public partial class SettingsView : UserControl
{
    // Set while LoadSettings itself is applying the persisted value, so that doesn't re-fire
    // the *Changed events below as if the user had just changed something.
    private bool suppressChangeEvents;

    public SettingsView()
    {
        this.InitializeComponent();
    }

    /// <summary>
    /// Raised only in response to the user actually flipping the toggle, with the new value -
    /// MainWindow persists it and pushes it to every currently-running session's pane.
    /// </summary>
    internal event EventHandler<bool>? UseShiftEnterForNewlineChanged;

    /// <summary>
    /// Raised only in response to the user picking a folder via Browse... - MainWindow persists it.
    /// </summary>
    internal event EventHandler<string?>? DefaultRootFolderChanged;

    /// <summary>
    /// Raised only in response to the user actually flipping the toggle - MainWindow persists it.
    /// </summary>
    internal event EventHandler<bool>? UseWorktreeByDefaultChanged;

    /// <summary>
    /// Raised only in response to the user actually picking a radio option - MainWindow persists
    /// it and applies it to sessions launched from then on.
    /// </summary>
    internal event EventHandler<ClaudePermissionMode>? DefaultPermissionModeChanged;

    /// <summary>
    /// Raised only in response to the user picking a different theme radio button - MainWindow
    /// persists it and applies it (see ThemeManager).
    /// </summary>
    internal event EventHandler<AppTheme>? ThemeChanged;

    internal void LoadSettings(AppSettings settings)
    {
        this.suppressChangeEvents = true;
        this.ShiftEnterToggle.IsChecked = settings.UseShiftEnterForNewline;
        this.DefaultRootFolderBox.Text = settings.DefaultRootFolder ?? string.Empty;
        this.WorktreeToggle.IsChecked = settings.UseWorktreeByDefault;

        this.PermissionModeCombo.SelectedIndex = settings.DefaultPermissionMode switch
        {
            ClaudePermissionMode.Auto => 1,
            ClaudePermissionMode.AcceptEdits => 2,
            ClaudePermissionMode.Plan => 3,
            ClaudePermissionMode.BypassPermissions => 4,
            _ => 0,
        };

        this.GetThemeRadio(settings.Theme).IsChecked = true;
        this.suppressChangeEvents = false;
    }

    private void OnShiftEnterToggleClick(object sender, RoutedEventArgs e)
    {
        if (this.suppressChangeEvents)
        {
            return;
        }

        this.UseShiftEnterForNewlineChanged?.Invoke(this, this.ShiftEnterToggle.IsChecked == true);
    }

    private void OnBrowseDefaultRootFolderClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Choose a default root folder for new sessions",
            InitialDirectory = Directory.Exists(this.DefaultRootFolderBox.Text)
                ? this.DefaultRootFolderBox.Text
                : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        };

        if (dialog.ShowDialog(Window.GetWindow(this)) != true)
        {
            return;
        }

        this.DefaultRootFolderBox.Text = dialog.FolderName;
        this.DefaultRootFolderChanged?.Invoke(this, dialog.FolderName);
    }

    private void OnWorktreeToggleClick(object sender, RoutedEventArgs e)
    {
        if (this.suppressChangeEvents)
        {
            return;
        }

        this.UseWorktreeByDefaultChanged?.Invoke(this, this.WorktreeToggle.IsChecked == true);
    }

    private void OnPermissionModeSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (this.suppressChangeEvents)
        {
            return;
        }

        var tag = (string)((ComboBoxItem)this.PermissionModeCombo.SelectedItem).Tag;
        this.DefaultPermissionModeChanged?.Invoke(this, Enum.Parse<ClaudePermissionMode>(tag));
    }

    private void OnThemeRadioChecked(object sender, RoutedEventArgs e)
    {
        if (this.suppressChangeEvents)
        {
            return;
        }

        var theme = sender switch
        {
            var s when s == this.DarkLowContrastThemeRadio => AppTheme.DarkLowContrast,
            var s when s == this.LightThemeRadio => AppTheme.Light,
            _ => AppTheme.Dark,
        };

        this.ThemeChanged?.Invoke(this, theme);
    }

    private RadioButton GetThemeRadio(AppTheme theme) => theme switch
    {
        AppTheme.DarkLowContrast => this.DarkLowContrastThemeRadio,
        AppTheme.Light => this.LightThemeRadio,
        _ => this.DarkThemeRadio,
    };
}
