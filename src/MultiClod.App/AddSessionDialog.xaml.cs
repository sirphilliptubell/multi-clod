using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using MultiClod.App.Git;
using MultiClod.App.Native;

namespace MultiClod.App;

/// <summary>
/// Replaces the old name-prompt-then-folder-picker wizard (RenameDialog + OpenFolderDialog in
/// sequence) with a single form: session name, folder (pre-filled with a caller-supplied
/// default), and - only when that folder is actually inside a git repository - an optional
/// "create a git worktree" branch to base a new branch/worktree on instead of using the folder
/// directly.
/// </summary>
public partial class AddSessionDialog : Window
{
    private readonly bool useWorktreeByDefault;

    // True until the user types into NameBox themselves - see OnNameTextChanged. Keeps the name
    // field in sync with whichever folder is currently chosen (matching the old wizard's
    // auto-name-from-folder behavior) without ever overwriting something the user actually typed.
    private bool nameIsAutoFilled = true;
    private bool suppressNameTextChanged;

    // Applies useWorktreeByDefault only the first time a git repo is ever detected in this dialog
    // (typically the initial folder) - once the user has seen and possibly changed the checkbox,
    // browsing to a different repo folder shouldn't silently reset their choice back to the
    // app-wide default.
    private bool worktreeDefaultApplied;

    // Set by UpdateWorktreeSection whenever FolderBox points at a real git repo, null otherwise -
    // reused by OnAddClick so it doesn't need to re-resolve the same repo root a second time.
    private string? currentRepoRoot;

    public AddSessionDialog(string defaultFolder, bool useWorktreeByDefault)
    {
        this.InitializeComponent();
        DarkTitleBar.Apply(this);

        this.useWorktreeByDefault = useWorktreeByDefault;
        this.SetFolder(defaultFolder);

        this.Loaded += (_, _) => this.NameBox.Focus();
    }

    public string SessionName { get; private set; } = string.Empty;

    public string WorkingDirectory { get; private set; } = string.Empty;

    private void SetFolder(string folder)
    {
        this.FolderBox.Text = folder;

        if (this.nameIsAutoFilled)
        {
            this.suppressNameTextChanged = true;
            this.NameBox.Text = FolderDisplayName.GetName(folder);
            this.suppressNameTextChanged = false;
        }

        this.UpdateWorktreeSection(folder);
    }

    /// <summary>
    /// Shows/hides the whole worktree option based on whether Folder is actually inside a git
    /// repo, and (per the explicit git-branches-can-differ-per-folder requirement) refreshes
    /// BranchCombo every time this runs, not just when the checkbox happens to be checked.
    /// </summary>
    private void UpdateWorktreeSection(string folder)
    {
        var isGitRepo = GitRepository.TryGetRepoRoot(folder, out var repoRoot);
        this.currentRepoRoot = isGitRepo ? repoRoot : null;
        this.WorktreeSection.Visibility = isGitRepo ? Visibility.Visible : Visibility.Collapsed;

        if (isGitRepo)
        {
            if (!this.worktreeDefaultApplied)
            {
                this.worktreeDefaultApplied = true;
                this.WorktreeCheckBox.IsChecked = this.useWorktreeByDefault;
            }

            this.RefreshBranches(repoRoot);
        }
        else
        {
            this.WorktreeCheckBox.IsChecked = false;
            this.BranchCombo.Items.Clear();
        }

        this.ResizeToContent();
    }

    /// <summary>
    /// WPF's SizeToContent doesn't reliably shrink a NoResize window back down after a child's
    /// Visibility flips to Collapsed post-show (growing works fine; a later shrink can leave the
    /// window at its previous, taller size with the extra space rendered in Window.Background
    /// rather than whatever content used to fill it) - toggling SizeToContent off and back on
    /// forces WPF to redo the measurement instead of trusting its own stale cached size.
    /// </summary>
    private void ResizeToContent()
    {
        this.SizeToContent = SizeToContent.Manual;
        this.SizeToContent = SizeToContent.Height;
    }

    /// <summary>
    /// Local branches, plus remote branches that don't already have a same-named local branch
    /// (e.g. "origin/main" is dropped once "main" is already in the local list - both would
    /// otherwise resolve to the same starting point for a new worktree, just via different refs).
    /// </summary>
    private void RefreshBranches(string repoRoot)
    {
        this.BranchCombo.Items.Clear();

        var localBranches = GitRepository.GetLocalBranches(repoRoot);
        var remoteOnlyBranches = GitRepository.GetRemoteBranches(repoRoot)
            .Where(remoteBranch => !localBranches.Contains(RemoteBranchShortName(remoteBranch)));

        var branches = localBranches.Concat(remoteOnlyBranches).ToList();

        foreach (var branch in branches)
        {
            this.BranchCombo.Items.Add(branch);
        }

        var defaultBranch = GitRepository.GetDefaultRemoteBranch(repoRoot);
        var defaultLocalBranch = defaultBranch is not null ? RemoteBranchShortName(defaultBranch) : null;

        // Prefers the local form of the default branch when both exist (it's the one actually
        // left in the list after the remote-branch filter above), then falls back to the remote
        // form (a local branch was never created for it) or just the first available branch.
        this.BranchCombo.SelectedItem =
            defaultLocalBranch is not null && branches.Contains(defaultLocalBranch) ? defaultLocalBranch :
            defaultBranch is not null && branches.Contains(defaultBranch) ? defaultBranch :
            branches.FirstOrDefault();
    }

    private static string RemoteBranchShortName(string remoteBranch)
    {
        var slashIndex = remoteBranch.IndexOf('/');
        return slashIndex < 0 ? remoteBranch : remoteBranch[(slashIndex + 1)..];
    }

    private void OnNameTextChanged(object sender, TextChangedEventArgs e)
    {
        if (this.suppressNameTextChanged)
        {
            return;
        }

        this.nameIsAutoFilled = false;
    }

    private void OnBrowseFolderClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Choose a working directory for the new session",
            InitialDirectory = Directory.Exists(this.FolderBox.Text)
                ? this.FolderBox.Text
                : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        this.SetFolder(dialog.FolderName);
    }

    private void OnWorktreeCheckedChanged(object sender, RoutedEventArgs e)
    {
        var isChecked = this.WorktreeCheckBox.IsChecked == true;
        this.WorktreeNameBox.IsEnabled = isChecked;
        this.BranchCombo.IsEnabled = isChecked;
    }

    private void OnAddClick(object sender, RoutedEventArgs e)
    {
        var name = this.NameBox.Text.Trim();
        if (name.Length == 0)
        {
            this.ShowError("Session name cannot be empty.");
            return;
        }

        var folder = this.FolderBox.Text.Trim();
        if (folder.Length == 0 || !Directory.Exists(folder))
        {
            this.ShowError("Choose a valid folder.");
            return;
        }

        var workingDirectory = folder;

        if (this.WorktreeCheckBox.IsChecked == true)
        {
            var worktreeName = this.WorktreeNameBox.Text.Trim();
            if (worktreeName.Length == 0)
            {
                this.ShowError("Worktree name cannot be empty.");
                return;
            }

            if (this.BranchCombo.SelectedItem is not string baseBranch)
            {
                this.ShowError("Choose a branch to create the worktree from.");
                return;
            }

            if (this.currentRepoRoot is not { } repoRoot)
            {
                this.ShowError("The selected folder is not inside a git repository.");
                return;
            }

            if (!GitWorktree.TryCreate(repoRoot, worktreeName, baseBranch, out workingDirectory, out var error))
            {
                this.ShowError(error);
                return;
            }
        }

        this.SessionName = name;
        this.WorkingDirectory = workingDirectory;
        this.DialogResult = true;
    }

    private void ShowError(string message)
    {
        this.ErrorText.Text = message;
        this.ErrorText.Visibility = Visibility.Visible;
    }
}
