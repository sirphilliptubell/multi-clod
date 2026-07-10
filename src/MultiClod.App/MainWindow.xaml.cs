using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;
using MultiClod.App.Import;
using MultiClod.App.Native;
using MultiClod.App.Persistence;
using MultiClod.App.Skills;
using MultiClod.App.Updates;
using MultiClod.App.Validation;
using MultiClod.Terminal.Abstractions;
using MultiClod.Terminal.Wpf;

namespace MultiClod.App;

public partial class MainWindow : Window
{
    private static readonly TerminalPaneTheme SessionTheme = new()
    {
        Background = Color.FromRgb(12, 12, 12),
        Foreground = Color.FromRgb(242, 242, 242),
        CursorColor = Color.FromRgb(242, 242, 242),
        SelectionBackground = Color.FromRgb(58, 150, 221),
    };

    private readonly SessionStore store;
    private readonly SessionTreeController controller;
    private readonly WindowLayoutStore layoutStore;
    private readonly ShiftDeleteHook shiftDeleteHook;
    private readonly TerminalArrowKeyRoutingHook arrowKeyRoutingHook;

    // Title as set by the ctor (includes the "(Debug)" suffix), before any update-status suffix -
    // see OnUpdateStatusChanged. Captured once rather than stripping a suffix back off later.
    private readonly string baseTitle;

    // Set in OnLoaded (mirrors app.FromHereRequests.Attach/Detach), so OnClosing can unsubscribe
    // the same delegate instance - AppUpdateCoordinator outlives this window's lifetime (it's owned
    // by App), so a leaked subscription would otherwise keep this window alive too.
    private AppUpdateCoordinator? updateCoordinator;

    // Set only from OnTreeMouseRightButtonDown (always fires before ContextMenuOpening for a
    // mouse-triggered open), so the context menu targets the row that was actually clicked
    // instead of whatever happened to be selected before. Null means "empty area" (no
    // TreeViewItem under the cursor) -> the menu offers "Add Project" only.
    private TreeNodeViewModel? rightClickedNode;

    // Right-clicking a node force-selects it (so the context menu can target it) without the
    // user meaning to navigate to/launch it - see OnTreeMouseRightButtonDown.
    private bool suppressSelectionSideEffects;

    // In-process-only drag payload identifier - the dragged TreeNodeViewModel instance itself is
    // carried through DataObject, never serialized.
    private const string DragFormat = "MultiClod.App.TreeNode";

    private Point? dragStartPoint;
    private TreeNodeViewModel? dragStartNode;

    private enum DropPosition { Before, Into, After }

    // Which of Tree/SkillsList (Panel) and the corresponding canvas content is currently visible -
    // see SetRailSection.
    private RailSection currentRailSection = RailSection.Sessions;

    // Populated on first click of the Skills rail icon; null means "not scanned yet this run" -
    // see EnsureSkillsLoaded. Skills are only rescanned on the next app launch, not live.
    private ObservableCollection<SkillNodeViewModel>? skillNodes;

    // Set while OnSkillsListSelectionChanged is itself reverting a rejected selection change
    // (user declined to discard a dirty edit), so that reversion doesn't recursively re-trigger
    // the same dirty-check - mirrors suppressSelectionSideEffects's role for the Tree above.
    private bool suppressSkillsSelectionSideEffects;

    public MainWindow()
    {
        this.InitializeComponent();

#if DEBUG
        // Visibly distinguishes a Debug build's window from a concurrently-running Release
        // instance (see FromHereProtocol's Debug/Release isolation) so the two are never confused.
        this.Title += " (Debug)";
        this.DebugBorder.BorderThickness = new Thickness(4);
#endif

        // Application.Current is already this running App by now - App.OnStartup constructs
        // UpdateCoordinator and only calls base.OnStartup (which creates this window, via
        // StartupUri) afterward. Null in any unpublished/local build (no update feed baked in - see
        // UpdateFeedLocation), so plain debug/F5 runs show no version.
        if ((Application.Current as App)?.UpdateCoordinator?.CurrentVersionText is { } version)
        {
            this.Title += $" v{version}";
        }

        this.baseTitle = this.Title;

        this.store = new SessionStore();
        this.controller = new SessionTreeController(this.store);
        this.controller.Load();
        this.Tree.ItemsSource = this.controller.RootNodes;

        this.layoutStore = new WindowLayoutStore();
        ApplyWindowLayout(this, this.TreeColumn, this.layoutStore.Load());

        // Catches Shift+Delete even when the embedded terminal owns native Win32 keyboard focus,
        // which the Tree's own KeyDown handler (OnTreeKeyDown) cannot - see ShiftDeleteHook.
        this.shiftDeleteHook = new ShiftDeleteHook(this.OnDeleteShortcut);

        // Keeps arrow keys from leaking to the Tree while a terminal session has real Win32
        // focus - see TerminalArrowKeyRoutingHook's remarks. The hwnd getter is lazy (evaluated
        // per keystroke, not captured here) because this window's own hwnd doesn't exist yet
        // until WPF creates it later during Show()/OnSourceInitialized.
        this.arrowKeyRoutingHook = new TerminalArrowKeyRoutingHook(() => new WindowInteropHelper(this).Handle);

        this.Closing += this.OnClosing;

        // Deferred to Loaded (rather than attaching here in the ctor) so any buffered request -
        // in particular this process's own --from-here startup argument, posted from App.OnStartup
        // before this window even exists - is delivered only once SessionViewHost's terminal
        // control has a real HWND: starting a ConPTY session before that HWND exists spins up
        // conhost but silently never attaches a client process to it.
        this.Loaded += this.OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        this.Loaded -= this.OnLoaded;

        if (Application.Current is App app)
        {
            app.FromHereRequests.Attach(this.HandleFromHereRequest);

            this.updateCoordinator = app.UpdateCoordinator;
            if (this.updateCoordinator is not null)
            {
                this.updateCoordinator.StatusChanged += this.OnUpdateStatusChanged;

                // Covers the status the startup check already settled into (CheckingForUpdates
                // never actually observed here - that check runs synchronously in App.OnStartup,
                // before this window exists) - without this, the title would stay plain until the
                // periodic timer's first tick five minutes later.
                if (this.updateCoordinator.Status is { } currentStatus)
                {
                    this.OnUpdateStatusChanged(currentStatus);
                }
            }
        }
    }

    // AppUpdateCoordinator.SetStatus already marshals onto the UI dispatcher, so this always runs
    // on the UI thread.
    private void OnUpdateStatusChanged(AppUpdateStatus status)
    {
        this.Title = $"{this.baseTitle} ({status.Describe()})";
    }

    private void LaunchSession(SessionNodeViewModel node)
    {
        if (node.IsRunning)
        {
            return;
        }

        var host = new WpfSessionHost();
        host.Pane.ApplyTheme(SessionTheme);
        host.Pane.Title = node.DisplayTitle;
        host.CloseRequested += (_, _) => this.StopSession(node);

        // Added to the shared container exactly once, here, for this host's entire lifetime -
        // never remove a live session's view from the tree to hide it (only Visibility toggle -
        // removing it tears down the native terminal hwnd irrecoverably, see
        // OnTreeSelectedItemChanged). Removal only happens when the host is permanently discarded
        // (StopSession or Delete).
        host.Pane.View.Visibility = Visibility.Collapsed;
        this.SessionViewHost.Children.Add(host.Pane.View);

        // Constructed (and subscribed to host.StateChanged) before Start() runs - Start() fires
        // Starting/Running synchronously, so creating this after Start() would miss both events
        // and leave the node's spinner stuck on "Starting" forever.
        var session = new TerminalSession(node.WorkingDirectory, host);

        // --session-id pre-assigns this node's conversation identity on its first-ever launch;
        // every later launch passes --resume for that same id to reopen the same conversation.
        // HasBeenStarted is what remembers which one we're on, since both flags take the same GUID.
        var flag = node.HasBeenStarted ? "--resume" : "--session-id";
        var commandLine = $"cmd.exe /c claude {flag} {node.ClaudeSessionId}";
        host.Start(new TerminalLaunchOptions { WorkingDirectory = node.WorkingDirectory, CommandLine = commandLine });

        node.AttachLiveSession(session);

        // Subscribed on session (discarded on stop), not node (persists across relaunches) - the
        // latter would leak a closure over this host every relaunch, since nothing else would ever
        // unsubscribe it from the long-lived node's PropertyChanged.
        session.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(TerminalSession.DetectedTitle))
            {
                host.Pane.Title = node.DisplayTitle;
                this.controller.ScheduleSave();
            }
        };

        node.HasBeenStarted = true;
        this.controller.ScheduleSave();
    }

    private void StopSession(SessionNodeViewModel node)
    {
        if (node.LiveSession is not { } session)
        {
            return;
        }

        // Per WpfSessionHost/ConPtyConnection's single-use guard, this host instance can never be
        // Start()ed again - relaunching this node later always constructs a brand-new host, passing
        // --resume since HasBeenStarted is already persisted true.
        session.Host.Dispose();
        this.SessionViewHost.Children.Remove(session.Host.Pane.View);
        node.DetachLiveSession();
    }

    private void OnTreeSelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (this.suppressSelectionSideEffects)
        {
            return;
        }

        if (e.OldValue is SessionNodeViewModel { LiveSession: { } oldSession })
        {
            oldSession.Host.Pane.View.Visibility = Visibility.Collapsed;
        }

        if (e.NewValue is SessionNodeViewModel session)
        {
            if (!session.IsRunning)
            {
                this.controller.RevalidateBeforeLaunch(session);
                if (session.IsInvalid)
                {
                    this.PlaceholderText.Visibility = Visibility.Collapsed;
                    this.ErrorText.Text = session.ToolTipText;
                    this.ErrorText.Visibility = Visibility.Visible;
                    return;
                }

                this.LaunchSession(session);
            }

            this.ErrorText.Visibility = Visibility.Collapsed;
            this.PlaceholderText.Visibility = Visibility.Collapsed;
            session.LiveSession!.Host.Pane.View.Visibility = Visibility.Visible;

            // Deferred rather than called inline: Pane.Focus() bottoms out in
            // TerminalContainer_GotFocus, which calls native SetFocus on the terminal's hwnd -
            // bypassing WPF's focus system. We're still nested inside the very TreeViewItem
            // GotFocus/Selected dispatch that raised this SelectedItemChanged event, so stealing
            // real Win32 focus now (before that dispatch unwinds) leaves WPF's FocusManager out of
            // sync with what's actually focused. Left alone, this causes exactly the ancestor-
            // misrouting bug described on OnItemPreviewMouseLeftButtonDown above: the following
            // click resolves selection to the Project row instead of the session actually clicked.
            // Posting the focus call lets the current dispatch finish first.
            var liveSession = session.LiveSession;
            this.Dispatcher.BeginInvoke(() => liveSession.Host.Pane.Focus());
        }
        else
        {
            this.ErrorText.Visibility = Visibility.Collapsed;
            this.PlaceholderText.Visibility = Visibility.Visible;
        }
    }

    private void OnSessionsRailIconClick(object sender, MouseButtonEventArgs e)
    {
        this.SetRailSection(RailSection.Sessions);
    }

    private void OnSkillsRailIconClick(object sender, MouseButtonEventArgs e)
    {
        this.SetRailSection(RailSection.Skills);
    }

    private void SetRailSection(RailSection section)
    {
        if (this.currentRailSection == section)
        {
            return;
        }

        // Leaving Skills with an unsaved edit needs the same discard-confirmation as switching to
        // a different skill within the panel - see SkillDetailView.TryNavigateAway.
        if (this.currentRailSection == RailSection.Skills && !this.SkillDetail.TryNavigateAway())
        {
            return;
        }

        this.currentRailSection = section;

        this.SessionsAccentBar.Visibility = section == RailSection.Sessions ? Visibility.Visible : Visibility.Collapsed;
        this.SkillsAccentBar.Visibility = section == RailSection.Skills ? Visibility.Visible : Visibility.Collapsed;
        this.Tree.Visibility = section == RailSection.Sessions ? Visibility.Visible : Visibility.Collapsed;
        this.SkillsList.Visibility = section == RailSection.Skills ? Visibility.Visible : Visibility.Collapsed;

        if (section == RailSection.Skills)
        {
            // SessionViewHost's children are never hidden as a whole - only whichever pane
            // OnTreeSelectedItemChanged last made Visible - so that pane needs hiding explicitly
            // here; it's naturally re-shown by the replayed OnTreeSelectedItemChanged below when
            // switching back.
            this.HideActiveSessionPane();
            this.EnsureSkillsLoaded();
            this.RefreshSkillsCanvas();
        }
        else
        {
            this.SkillDetail.Visibility = Visibility.Collapsed;

            // oldValue is deliberately null - HideActiveSessionPane already hid whatever pane was
            // active before switching away from Sessions, so there's nothing left for
            // OnTreeSelectedItemChanged's own e.OldValue handling to hide again.
            this.OnTreeSelectedItemChanged(this, new RoutedPropertyChangedEventArgs<object>(null!, this.Tree.SelectedItem));
        }
    }

    private void HideActiveSessionPane()
    {
        if (this.Tree.SelectedItem is SessionNodeViewModel { LiveSession: { } session })
        {
            session.Host.Pane.View.Visibility = Visibility.Collapsed;
        }
    }

    private void EnsureSkillsLoaded()
    {
        if (this.skillNodes is not null)
        {
            return;
        }

        var skills = new SkillDiscoveryService().ScanPersonalSkills();
        this.skillNodes = new ObservableCollection<SkillNodeViewModel>(skills.Select(s => new SkillNodeViewModel(s)));
        this.SkillsList.ItemsSource = this.skillNodes;
    }

    private void OnSkillsListSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (this.suppressSkillsSelectionSideEffects)
        {
            return;
        }

        if (e.RemovedItems.Count > 0 && !this.SkillDetail.TryNavigateAway())
        {
            this.suppressSkillsSelectionSideEffects = true;
            this.SkillsList.SelectedItem = e.RemovedItems[0];
            this.suppressSkillsSelectionSideEffects = false;
            return;
        }

        this.RefreshSkillsCanvas();
    }

    private void RefreshSkillsCanvas()
    {
        if (this.SkillsList.SelectedItem is SkillNodeViewModel node)
        {
            this.PlaceholderText.Visibility = Visibility.Collapsed;
            this.ErrorText.Visibility = Visibility.Collapsed;
            this.SkillDetail.Visibility = Visibility.Visible;
            this.SkillDetail.LoadSkill(node.Info);
        }
        else
        {
            this.SkillDetail.Visibility = Visibility.Collapsed;
            this.ErrorText.Visibility = Visibility.Collapsed;
            this.PlaceholderText.Visibility = Visibility.Visible;
        }
    }

    private void OnSkillsListMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        var item = FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject);

        if (item is not null && !item.IsSelected)
        {
            item.IsSelected = true;
        }
    }

    private void OnSkillsListContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        this.SkillsContextMenu.Items.Clear();

        // Read the selection directly rather than tracking the right-clicked item in a field (as
        // the Tree does for its own context menu): selection is forced above in
        // OnSkillsListMouseRightButtonDown before this fires, so SelectedItem is already correct,
        // and a field-based version of this was observed going through empty here for reasons that
        // didn't reproduce under static inspection.
        if (this.SkillsList.SelectedItem is SkillNodeViewModel skill)
        {
            this.SkillsContextMenu.Items.Add(CreateMenuItem("Explore to", () => OpenContainingFolder(skill.Info.FilePath)));
        }
    }

    private static void OpenContainingFolder(string skillFilePath)
    {
        var folder = Path.GetDirectoryName(skillFilePath);
        if (folder is null)
        {
            return;
        }

        Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
    }

    private void OnTreeKeyDown(object sender, KeyEventArgs e)
    {
        if (this.Tree.SelectedItem is not TreeNodeViewModel node || node is ProjectNodeViewModel { IsUncategorized: true })
        {
            return;
        }

        if (e.Key == Key.F2)
        {
            this.OnRename(node);
            e.Handled = true;
        }
        else if (e.Key == Key.Delete && Keyboard.Modifiers == ModifierKeys.Shift)
        {
            this.OnDelete(node);
            e.Handled = true;
        }
    }

    // Fallback for when the Tree still has WPF keyboard focus - ShiftDeleteHook is the path that
    // matters once a session's terminal has stolen native focus (the common case; see its remarks).
    private void OnDeleteShortcut()
    {
        if (this.Tree.SelectedItem is not TreeNodeViewModel node || node is ProjectNodeViewModel { IsUncategorized: true })
        {
            return;
        }

        this.OnDelete(node);
    }

    private void OnTreeMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        var item = FindAncestor<TreeViewItem>(e.OriginalSource as DependencyObject);
        this.rightClickedNode = item?.DataContext as TreeNodeViewModel;

        if (item is not null && !item.IsSelected)
        {
            this.suppressSelectionSideEffects = true;
            item.IsSelected = true;
            this.suppressSelectionSideEffects = false;
        }
    }

    private void OnItemPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // The terminal's HwndHost sets real Win32 focus on its child hwnd (see
        // TerminalContainer.TerminalContainer_GotFocus), bypassing WPF's focus system - and that
        // can linger from any earlier session selection, not just the one being left just now.
        // While it lingers, WPF's own click-to-select handling on the *target* TreeViewItem
        // resolves incorrectly (per the DirectionalNavigation comment on the Window above,
        // WPF's navigation system misroutes focus to an ancestor while the terminal holds real
        // focus), so a click meant for a session can end up selecting its parent project instead.
        // This handler runs during the tunnel phase, before that resolution happens, so reclaiming
        // WPF focus here - on every click, unconditionally - guarantees it's back on the tree
        // before selection is decided.
        Keyboard.Focus(this.Tree);

        this.dragStartPoint = e.GetPosition(null);
        this.dragStartNode = (sender as TreeViewItem)?.DataContext as TreeNodeViewModel;
    }

    private void OnItemPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || this.dragStartPoint is not { } start || this.dragStartNode is not { } node)
        {
            return;
        }

        var current = e.GetPosition(null);
        if (Math.Abs(current.X - start.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(current.Y - start.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        this.dragStartPoint = null;
        this.dragStartNode = null;

        DragDrop.DoDragDrop((DependencyObject)sender, new DataObject(DragFormat, node), DragDropEffects.Move);
    }

    private void OnItemDragOver(object sender, DragEventArgs e)
    {
        e.Effects = DragDropEffects.None;
        e.Handled = true;

        if (sender is TreeViewItem item && e.Data.GetData(DragFormat) is TreeNodeViewModel dragged && item.DataContext is TreeNodeViewModel target)
        {
            var position = GetDropPosition(item, e.GetPosition(item));
            if (this.TryResolveDrop(dragged, target, position, out _, out _))
            {
                e.Effects = DragDropEffects.Move;
            }
        }
    }

    private void OnItemDrop(object sender, DragEventArgs e)
    {
        e.Handled = true;

        if (sender is not TreeViewItem item || e.Data.GetData(DragFormat) is not TreeNodeViewModel dragged || item.DataContext is not TreeNodeViewModel target)
        {
            return;
        }

        var position = GetDropPosition(item, e.GetPosition(item));
        if (!this.TryResolveDrop(dragged, target, position, out var newParent, out var index))
        {
            return;
        }

        this.MoveAndPersist(dragged, newParent, index);
    }

    private void OnTreeDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetData(DragFormat) is TreeNodeViewModel ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnTreeDrop(object sender, DragEventArgs e)
    {
        e.Handled = true;

        // Only reachable for a genuine empty-area drop - a drop landing on a row is handled (and
        // e.Handled = true is set) by that row's own OnItemDrop before bubbling gets here.
        if (e.Data.GetData(DragFormat) is not TreeNodeViewModel dragged)
        {
            return;
        }

        if (dragged is SessionNodeViewModel)
        {
            // Sessions can never be bare root nodes - route into Uncategorized instead.
            var uncategorized = this.controller.GetOrCreateUncategorized();
            this.MoveAndPersist(dragged, uncategorized, uncategorized.Children.Count);
        }
        else
        {
            this.MoveAndPersist(dragged, newParent: null, this.controller.RootNodes.Count);
        }
    }

    private void MoveAndPersist(TreeNodeViewModel dragged, TreeNodeViewModel? newParent, int index)
    {
        var oldParent = dragged.Parent;
        this.controller.MoveTo(dragged, newParent, index);
        this.controller.RemoveUncategorizedIfEmpty(oldParent);
        this.controller.ScheduleSave();
    }

    /// <summary>
    /// Resolves a drop's target parent/index without mutating anything, so OnItemDragOver can
    /// validate a hover and OnItemDrop can commit the same decision. Project nodes never nest
    /// (an "Into" drop on a project degrades to "After"); a session dropped "Into" another session
    /// becomes its last child; Project -> Session is always invalid.
    /// </summary>
    private bool TryResolveDrop(TreeNodeViewModel dragged, TreeNodeViewModel target, DropPosition position, out TreeNodeViewModel? newParent, out int index)
    {
        newParent = null;
        index = 0;

        if (ReferenceEquals(dragged, target) || SessionTreeController.WouldCreateCycle(dragged, target))
        {
            return false;
        }

        if (dragged is SessionNodeViewModel && target is SessionNodeViewModel)
        {
            if (position == DropPosition.Into)
            {
                newParent = target;
                index = target.Children.Count;
            }
            else
            {
                newParent = target.Parent;
                index = this.ResolveSiblingIndex(dragged, target, position, newParent);
            }

            return true;
        }

        if (dragged is SessionNodeViewModel && target is ProjectNodeViewModel project)
        {
            newParent = project;
            index = project.Children.Count;
            return true;
        }

        if (dragged is ProjectNodeViewModel && target is ProjectNodeViewModel)
        {
            var effectivePosition = position == DropPosition.Into ? DropPosition.After : position;
            newParent = null; // projects only ever live at the root
            index = this.ResolveSiblingIndex(dragged, target, effectivePosition, newParent);
            return true;
        }

        return false; // Project -> Session is always invalid
    }

    private int ResolveSiblingIndex(TreeNodeViewModel dragged, TreeNodeViewModel target, DropPosition position, TreeNodeViewModel? parent)
    {
        var siblings = parent?.Children ?? this.controller.RootNodes;
        var targetIndex = siblings.Where(n => !ReferenceEquals(n, dragged)).ToList().IndexOf(target);
        return position == DropPosition.Before ? targetIndex : targetIndex + 1;
    }

    // TreeViewItem.ActualHeight includes any expanded children's rendered rows, not just this
    // item's own header - so this fraction is only exact for a collapsed (or childless) item.
    // Hovering an expanded item's own header can misjudge Before/Into/After; dropping onto one of
    // its (separately hit-tested) child rows, or collapsing it first, is unaffected.
    private static DropPosition GetDropPosition(TreeViewItem item, Point position)
    {
        if (item.ActualHeight <= 0)
        {
            return DropPosition.Into;
        }

        var fraction = position.Y / item.ActualHeight;
        if (fraction < 0.25)
        {
            return DropPosition.Before;
        }

        if (fraction > 0.75)
        {
            return DropPosition.After;
        }

        return DropPosition.Into;
    }

    private void OnTreeContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        this.TreeContextMenu.Items.Clear();

        switch (this.rightClickedNode)
        {
            case null:
                this.TreeContextMenu.Items.Add(CreateMenuItem("Add Project", this.OnAddProject));
                break;

            case ProjectNodeViewModel project:
                this.TreeContextMenu.Items.Add(CreateMenuItem("Add Session", () => this.OnAddSession(project)));
                this.TreeContextMenu.Items.Add(CreateMenuItem("Import Session", () => this.OnImportSession(project)));
                if (!project.IsUncategorized)
                {
                    this.TreeContextMenu.Items.Add(new Separator());
                    this.TreeContextMenu.Items.Add(CreateMenuItem("Rename", () => this.OnRename(project), inputGestureText: "F2"));
                    this.TreeContextMenu.Items.Add(CreateMenuItem("Delete", () => this.OnDelete(project), inputGestureText: "Shift+Del"));
                }

                break;

            case SessionNodeViewModel session:
                this.TreeContextMenu.Items.Add(CreateMenuItem("Add Session", () => this.OnAddSession(session)));
                this.TreeContextMenu.Items.Add(CreateMenuItem("Import Session", () => this.OnImportSession(session)));
                this.TreeContextMenu.Items.Add(new Separator());

                // Plain enable/disable (unlike Delete's error-dialog approach) - "Stop while
                // dormant" needs no explanation, it's just not a currently valid action.
                this.TreeContextMenu.Items.Add(CreateMenuItem("Stop", () => this.StopSession(session), enabled: session.IsRunning));
                this.TreeContextMenu.Items.Add(CreateMenuItem("Rename", () => this.OnRename(session), inputGestureText: "F2"));
                this.TreeContextMenu.Items.Add(CreateMenuItem("Edit working directory", () => this.OnEditWorkingDirectory(session)));
                this.TreeContextMenu.Items.Add(CreateMenuItem("Delete", () => this.OnDelete(session), inputGestureText: "Shift+Del"));
                break;
        }
    }

    private void OnAddProject()
    {
        var dialog = new RenameDialog(
            string.Empty,
            "Add Project",
            candidate => this.controller.ValidateProjectName(candidate, excludingSelf: null))
        {
            Owner = this,
        };

        if (dialog.ShowDialog() == true)
        {
            this.controller.AddProject(dialog.NewName);
        }
    }

    private void OnAddSession(TreeNodeViewModel parent)
    {
        var nameDialog = new RenameDialog(string.Empty, "Add Session") { Owner = this };
        if (nameDialog.ShowDialog() != true)
        {
            return;
        }

        var folderDialog = new OpenFolderDialog
        {
            Title = "Choose a working directory for the new session",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        };

        if (folderDialog.ShowDialog(this) != true)
        {
            return;
        }

        this.controller.AddSession(parent, nameDialog.NewName, folderDialog.FolderName);
    }

    private void OnImportSession(TreeNodeViewModel parent)
    {
        var dialog = new ImportSessionWindow { Owner = this };
        if (dialog.ShowDialog() != true || dialog.SelectedResult is not { } result)
        {
            return;
        }

        this.controller.ImportSession(parent, result.SummaryOrSessionId, result.WorkingDirectory, result.ParentSessionId);
    }

    private void OnRename(TreeNodeViewModel node)
    {
        var project = node as ProjectNodeViewModel;
        var dialog = new RenameDialog(
            node.Name,
            "Rename",
            project is null ? null : candidate => this.controller.ValidateProjectName(candidate, project))
        {
            Owner = this,
        };

        if (dialog.ShowDialog() == true)
        {
            this.controller.Rename(node, dialog.NewName);
        }
    }

    private void OnEditWorkingDirectory(SessionNodeViewModel session)
    {
        var folderDialog = new OpenFolderDialog
        {
            Title = "Choose a working directory for this session",
            InitialDirectory = Directory.Exists(session.WorkingDirectory)
                ? session.WorkingDirectory
                : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        };

        if (folderDialog.ShowDialog(this) != true)
        {
            return;
        }

        this.controller.RelocateWorkingDirectory(session, folderDialog.FolderName);

        // Refresh the pane area if this is the node currently being looked at - relocating can
        // clear (or introduce) an error without the tree selection itself changing.
        if (ReferenceEquals(this.Tree.SelectedItem, session))
        {
            this.OnTreeSelectedItemChanged(this, new RoutedPropertyChangedEventArgs<object>(session, session));
        }
    }

    private void OnDelete(TreeNodeViewModel node)
    {
        if (node is SessionNodeViewModel { IsRunning: true } session)
        {
            this.StopSession(session);
        }

        if (!this.controller.TryDelete(node, out var error))
        {
            MessageBox.Show(this, error, "Cannot delete", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        this.shiftDeleteHook.Dispose();
        this.arrowKeyRoutingHook.Dispose();

        if (Application.Current is App app)
        {
            app.FromHereRequests.Detach();
        }

        if (this.updateCoordinator is not null)
        {
            this.updateCoordinator.StatusChanged -= this.OnUpdateStatusChanged;
        }

        // Captured before disposing - IsRunning/LiveSession can change once hosts are disposed.
        var runningSessions = this.controller.AllSessionNodes()
            .Where(n => n.IsRunning)
            .ToList();

        // Concurrent dispose so total shutdown time is bounded by the slowest single session, not
        // the sum across all of them.
        Task.WaitAll(runningSessions
            .Select(n => Task.Run(() => n.LiveSession!.Host.Dispose()))
            .ToArray());

        foreach (var node in runningSessions)
        {
            // Host.Dispose() kills the claude process tree (ConPtyConnection.Dispose ->
            // liveProcess.Kill(entireProcessTree: true)), and Claude Code writes its transcript
            // synchronously per message, so by now the file would exist if the user ever actually
            // sent anything. No transcript means the session was opened and abandoned without use -
            // drop it instead of persisting a dead entry.
            if (!File.Exists(ClaudeProjectPath.GetSessionFilePath(node.WorkingDirectory, node.ClaudeSessionId)))
            {
                this.controller.TryDelete(node, out _);
            }
        }

        this.controller.Flush();

        // RestoreBounds (rather than Left/Top/Width/Height directly) whenever the window isn't
        // Normal - those four properties reflect the maximized/minimized bounds while in that
        // state, which would otherwise overwrite the saved windowed-mode size/position with the
        // maximized (effectively full-screen) ones.
        var bounds = this.WindowState == WindowState.Normal
            ? new Rect(this.Left, this.Top, this.Width, this.Height)
            : this.RestoreBounds;

        this.layoutStore.Save(new WindowLayout
        {
            Left = bounds.Left,
            Top = bounds.Top,
            Width = bounds.Width,
            Height = bounds.Height,
            IsMaximized = this.WindowState == WindowState.Maximized,
            TreeColumnWidth = this.TreeColumn.Width.Value,
        });
    }

    /// <summary>
    /// Applies a saved layout before the window is shown. Static (rather than an instance method)
    /// so it can take the window/column as parameters and stay next to the ctor call site that's
    /// its only caller, without implying it depends on any other instance state.
    /// </summary>
    private static void ApplyWindowLayout(Window window, ColumnDefinition treeColumn, WindowLayout? layout)
    {
        if (layout is null)
        {
            return;
        }

        window.Width = layout.Width;
        window.Height = layout.Height;

        if (layout.Left is { } left && layout.Top is { } top)
        {
            window.Left = left;
            window.Top = top;
            window.WindowStartupLocation = WindowStartupLocation.Manual;
        }

        treeColumn.Width = new GridLength(layout.TreeColumnWidth);

        // Applied last, after Left/Top/Width/Height establish the restore bounds - setting
        // WindowState before those exist would snap the maximized window to the primary monitor
        // instead of whichever monitor it was last on.
        if (layout.IsMaximized)
        {
            window.WindowState = WindowState.Maximized;
        }
    }

    /// <summary>
    /// Handles both the pipe-driven hand-off from an already-running instance and this same
    /// process's own --from-here startup argument - see App.FromHereRequests. Always runs on the
    /// UI thread.
    /// </summary>
    private void HandleFromHereRequest(string? directory)
    {
        if (this.WindowState == WindowState.Minimized)
        {
            this.WindowState = WindowState.Normal;
        }

        this.Show();
        this.Activate();

        if (directory is null)
        {
            return;
        }

        var uncategorized = this.controller.GetOrCreateUncategorized();
        var name = GetFolderDisplayName(directory);
        var session = this.controller.AddSession(uncategorized, name, directory);

        // Launch unconditionally here rather than relying on selection to trigger it via
        // OnTreeSelectedItemChanged: selecting session below only takes effect once its
        // TreeViewItem container materializes (immediately if already realized, otherwise - e.g.
        // a from-here launch racing the very first layout pass - whenever WPF gets around to it),
        // and launch must not wait on that.
        this.controller.RevalidateBeforeLaunch(session);
        if (!session.IsInvalid)
        {
            this.LaunchSession(session);
        }

        // Two-way bound via the TreeViewItem style in MainWindow.xaml - WPF applies these the
        // moment each node's container exists, so this works whether or not that's already true.
        // OnItemSelected does the equivalent of the old imperative BringIntoView once session's
        // container actually selects.
        uncategorized.IsExpanded = true;
        session.IsSelected = true;
    }

    private static string GetFolderDisplayName(string directory)
    {
        var trimmed = directory.TrimEnd('\\', '/');
        var name = Path.GetFileName(trimmed);
        return string.IsNullOrEmpty(name) ? trimmed : name;
    }

    // Selected bubbles from any selected descendant, so without this guard a session becoming
    // selected would also fire (and needlessly BringIntoView) for every ancestor TreeViewItem.
    private void OnItemSelected(object sender, RoutedEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, sender) && sender is TreeViewItem item)
        {
            item.BringIntoView();
        }
    }

    private static MenuItem CreateMenuItem(string header, Action action, bool enabled = true, string? inputGestureText = null)
    {
        var item = new MenuItem { Header = header, IsEnabled = enabled, InputGestureText = inputGestureText };
        item.Click += (_, _) => action();
        return item;
    }

    private static T? FindAncestor<T>(DependencyObject? current)
        where T : DependencyObject
    {
        while (current is not null and not T)
        {
            current = VisualTreeHelper.GetParent(current);
        }

        return current as T;
    }
}
