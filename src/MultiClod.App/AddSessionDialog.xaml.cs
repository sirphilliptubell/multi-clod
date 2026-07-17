using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Microsoft.Win32;
using MultiClod.App.Extensions;
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

    // Mirrors nameIsAutoFilled/suppressNameTextChanged for WorktreeNameBox: true until the user
    // types into it themselves, so it can keep tracking a slugified version of NameBox without
    // ever overwriting something the user actually typed there.
    private bool worktreeNameIsAutoFilled = true;
    private bool suppressWorktreeNameTextChanged;

    // Applies useWorktreeByDefault only the first time a git repo is ever detected in this dialog
    // (typically the initial folder) - once the user has seen and possibly changed the checkbox,
    // browsing to a different repo folder shouldn't silently reset their choice back to the
    // app-wide default.
    private bool worktreeDefaultApplied;

    // Set by UpdateWorktreeSection whenever FolderBox points at a real git repo, null otherwise -
    // reused by OnAddClick so it doesn't need to re-resolve the same repo root a second time.
    private string? currentRepoRoot;

    // Backs BranchCombo's ItemsSource (via a CollectionView, so typing can filter without
    // re-querying git) and lets OnAddClick recognize a fully-typed branch name that was never
    // formally "selected" from the dropdown.
    private readonly List<string> allBranches = [];

    // The current BranchCombo filter text, applied by FilterBranch. Kept separate from
    // BranchCombo.Text so it can be reset without fighting the TextChanged handler below.
    private string branchFilterText = string.Empty;

    // True while code is programmatically assigning BranchCombo.SelectedItem/Text (e.g. picking
    // the default branch in RefreshBranches) - suppresses OnBranchComboTextChanged so that
    // assignment doesn't re-filter the list down to just the newly selected item.
    private bool suppressBranchTextChanged;

    public AddSessionDialog(string defaultFolder, bool useWorktreeByDefault)
    {
        this.InitializeComponent();
        DarkTitleBar.Apply(this);

        this.useWorktreeByDefault = useWorktreeByDefault;
        this.SetFolder(defaultFolder);

        this.Loaded += (_, _) =>
        {
            this.NameBox.Focus();
            this.NameBox.SelectAll();
        };
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
            this.allBranches.Clear();
            this.BranchCombo.ItemsSource = null;
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
        var localBranches = GitRepository.GetLocalBranches(repoRoot);
        var remoteOnlyBranches = GitRepository.GetRemoteBranches(repoRoot)
            .Where(remoteBranch => !localBranches.Contains(RemoteBranchShortName(remoteBranch)));

        var branches = localBranches.Concat(remoteOnlyBranches).ToList();

        var defaultBranch = GitRepository.GetDefaultRemoteBranch(repoRoot);
        var defaultLocalBranch = defaultBranch is not null ? RemoteBranchShortName(defaultBranch) : null;

        // Prefers the local form of the default branch when both exist (it's the one actually
        // left in `branches` after the remote-branch filter above), then falls back to the remote
        // form (a local branch was never created for it).
        var pinnedBranch =
            defaultLocalBranch is not null && branches.Contains(defaultLocalBranch) ? defaultLocalBranch :
            defaultBranch is not null && branches.Contains(defaultBranch) ? defaultBranch :
            null;

        // Pins the repo's default branch to the top of the list - the most likely branch to base
        // a new worktree on - with everything else alphabetical rather than the git-order jumble
        // of local-then-remote branches.
        var rest = branches.Where(branch => branch != pinnedBranch).OrderBy(branch => branch, StringComparer.OrdinalIgnoreCase);

        this.allBranches.Clear();
        if (pinnedBranch is not null)
        {
            this.allBranches.Add(pinnedBranch);
        }

        this.allBranches.AddRange(rest);

        this.branchFilterText = string.Empty;
        var view = CollectionViewSource.GetDefaultView(this.allBranches);
        view.Filter = this.FilterBranch;
        this.BranchCombo.ItemsSource = view;

        // No branch is pre-selected/pre-filled - the user picks or types one deliberately.
        this.suppressBranchTextChanged = true;
        this.BranchCombo.SelectedItem = null;
        this.BranchCombo.Text = string.Empty;
        this.suppressBranchTextChanged = false;
    }

    private bool FilterBranch(object item)
    {
        return this.branchFilterText.Length == 0
            || (item is string branch && branch.Contains(this.branchFilterText, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Makes BranchCombo searchable: since IsTextSearchEnabled is off (its built-in search only
    /// jumps to a matching item rather than filtering the list), typing here re-runs FilterBranch
    /// over the full branch list and drops the dropdown open so the narrowed results are visible.
    /// </summary>
    private void OnBranchComboTextChanged(object sender, TextChangedEventArgs e)
    {
        if (this.suppressBranchTextChanged)
        {
            return;
        }

        this.branchFilterText = this.BranchCombo.Text;
        (this.BranchCombo.ItemsSource as ICollectionView)?.Refresh();

        if (this.branchFilterText.Length > 0)
        {
            this.BranchCombo.IsDropDownOpen = true;
        }
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
        this.UpdateWorktreeNameFromSessionName();
    }

    /// <summary>
    /// Keeps WorktreeNameBox tracking a slugified version of the session name (e.g. "Do a
    /// Thing!" -> "do-a-thing") until the user edits WorktreeNameBox directly - mirrors how
    /// SetFolder keeps NameBox tracking the folder name.
    /// </summary>
    private void UpdateWorktreeNameFromSessionName()
    {
        if (!this.worktreeNameIsAutoFilled)
        {
            return;
        }

        this.suppressWorktreeNameTextChanged = true;
        this.WorktreeNameBox.Text = this.NameBox.Text.Slugify();
        this.suppressWorktreeNameTextChanged = false;
    }

    private void OnWorktreeNameTextChanged(object sender, TextChangedEventArgs e)
    {
        if (this.suppressWorktreeNameTextChanged)
        {
            return;
        }

        this.worktreeNameIsAutoFilled = false;
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
        this.WorktreeControls.Visibility = isChecked ? Visibility.Visible : Visibility.Collapsed;
        this.ResizeToContent();

        // Jumps straight to the field the user almost always wants to touch next - with its
        // auto-filled slug preselected so typing replaces it outright instead of requiring a
        // manual select-all first.
        if (isChecked)
        {
            this.WorktreeNameBox.Focus();
            this.WorktreeNameBox.SelectAll();
        }
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

            // BranchCombo is editable so a user can search-and-type instead of picking with the
            // mouse; SelectedItem is only null-safe if git commits (via Enter/arrow keys), so a
            // fully-typed exact match is accepted too rather than forcing an explicit pick.
            var baseBranch = this.BranchCombo.SelectedItem as string ?? this.BranchCombo.Text.Trim();
            if (!this.allBranches.Contains(baseBranch))
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
