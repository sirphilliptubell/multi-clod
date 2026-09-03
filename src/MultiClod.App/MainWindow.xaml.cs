using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using MultiClod.App.Activation;
using MultiClod.App.Context;
using MultiClod.App.Costs;
using MultiClod.App.Deeplink;
using MultiClod.App.EnvironmentVariables;
using MultiClod.App.Diagnostics;
using MultiClod.App.Import;
using MultiClod.App.MarkdownEditor;
using MultiClod.App.Native;
using MultiClod.App.OutputStyles;
using MultiClod.App.Persistence;
using MultiClod.App.SessionLog;
using MultiClod.App.SessionScope;
using MultiClod.App.Settings;
using MultiClod.App.Skills;
using MultiClod.App.Skills.SkillEditor;
using MultiClod.App.Theming;
using MultiClod.App.Undo;
using MultiClod.App.Updates;
using MultiClod.Terminal.Abstractions;
using MultiClod.Terminal.Wpf;

namespace MultiClod.App;

public partial class MainWindow : Window
{
    private readonly SessionStore store;
    private readonly SessionTreeController controller;
    private readonly WindowLayoutStore layoutStore;
    private readonly AppSettingsStore settingsStore;
    private AppSettings appSettings;
    private readonly ClaudeSessionHooksInstaller hooksInstaller;
    private readonly ShiftDeleteHook shiftDeleteHook;
    private readonly TerminalKeyRoutingHook terminalKeyRoutingHook;
    private readonly UndoRedoShortcutHook undoRedoHook;
    private readonly SessionLogWindowRegistry sessionLogWindows = new();
    private readonly DeeplinkImportWindowRegistry deeplinkWindows = new();

    // Covers Move/Delete/Rename on the tree - see the three call sites below. In-memory only, not
    // persisted, so history is lost on restart same as any other unsaved UI state.
    private readonly UndoManager undoManager = new();

    // Set around every modal ShowDialog() call (OnAddProject/OnAddSession/OnImportSession/OnRename)
    // so UndoRedoShortcutHook's global Ctrl+Z/Ctrl+Y can tell it should let the keystroke fall
    // through to whichever TextBox has real focus in the dialog, instead of undoing the tree.
    private bool isModalDialogOpen;

    // Owns every started session's FileSystemWatcher-driven cost tracking - see
    // SessionCostMonitorService's remarks for why this is centralized rather than per-session.
    // Scoped to this window's lifetime, same as sessionLogWindows above, since only MainWindow has
    // the SessionTreeController reference it needs to enumerate started sessions.
    private readonly SessionCostMonitorService costMonitor = new();

    // Sessions currently open as tabs in the main panel, in tab-strip order - see OpenTab/CloseTab.
    // Bound to TabStrip.ItemsSource in the ctor. Decoupled from Tree.SelectedItem: selecting a
    // Project node in the tree leaves this (and whichever tab is active) untouched.
    private readonly ObservableCollection<SessionNodeViewModel> openTabs = new();

    // Captured in the ctor for RestoreOpenTabs, called from OnLoaded once SessionViewHost's
    // terminal control has a real HWND - same deferral rationale as the --from-here handling below.
    private readonly WindowLayout? savedLayout;

    // Title as set by the ctor (includes the "(Debug)" suffix), before any update-status suffix -
    // see OnUpdateStatusChanged. Captured once rather than stripping a suffix back off later.
    private readonly string baseTitle;

    // Set in OnLoaded (mirrors app.ActivationRequests.Attach/Detach), so OnClosing can unsubscribe
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

    // Separate drag payload identifier from DragFormat above so a drag started on a tab is never
    // mistaken for a tree-node drag (or vice versa) if it happens to end up over the other control.
    private const string TabDragFormat = "MultiClod.App.TabItem";

    private Point? tabDragStartPoint;
    private SessionNodeViewModel? tabDragStartSession;

    // Which of Tree/ContextPanel (Panel) and the corresponding canvas content is currently visible -
    // see SetRailSection.
    private RailSection currentRailSection = RailSection.Sessions;

    // Populated on first click of the Context rail icon; null means "not scanned yet this run" -
    // see EnsureSkillsLoaded. Skills are only rescanned on the next app launch, not live.
    private ObservableCollection<SkillNodeViewModel>? skillNodes;

    // Mirrors skillNodes, for the Output Styles group - see EnsureOutputStylesLoaded.
    private ObservableCollection<OutputStyleNodeViewModel>? outputStyleNodes;

    // Set while OnSkillsListSelectionChanged is itself reverting a rejected selection change
    // (user declined to discard a dirty edit), so that reversion doesn't recursively re-trigger
    // the same dirty-check - mirrors suppressSelectionSideEffects's role for the Tree above.
    private bool suppressSkillsSelectionSideEffects;

    // Mirrors suppressSkillsSelectionSideEffects, for OutputStylesList specifically.
    private bool suppressOutputStylesSelectionSideEffects;

    // Whether the Skills/Output Styles accordion groups in ContextPanel are currently expanded -
    // both start expanded (Skills preserves the panel's previous always-visible behavior; Output
    // Styles is surfaced too so it's noticed). Not persisted across restarts. See
    // ToggleContextGroup/OnSkillsGroupHeaderClick/OnOutputStylesGroupHeaderClick.
    private bool skillsGroupExpanded = true;

    private bool outputStylesGroupExpanded = true;

    // Built on first switch to the Context rail section; null means "not built yet this run" - see
    // EnsureContextTreeLoaded. Only rebuilt after a save through MarkdownEditor (see
    // OnMarkdownEditorDocumentSaved) - no FileSystemWatcher, matching the Skills list's own
    // rescan-on-next-launch-only convention.
    private ContextFileNodeViewModel? contextRoot;

    // ContextTree and SkillsList share one MarkdownEditor - selecting in one clears the other's
    // selection so only one node is ever visually "active". Set while that clearing itself is in
    // flight, so it doesn't recursively re-trigger the other control's selection handler.
    private bool suppressContextPanelSelectionSync;

    // Which control's item is currently loaded into the shared MarkdownEditor - drives which
    // @import tree (if any) gets rebuilt on save (see OnMarkdownEditorDocumentSaved; skills/
    // memories never need it, matching their own rescan-on-next-launch-only convention) and lets
    // the session-scoped sub-panel tell whether it's the one currently occupying the shared editor
    // (see IsShowingSessionScopedDocument).
    private enum MarkdownEditorSource
    {
        None,
        TopLevelContext,
        TopLevelSkill,
        TopLevelOutputStyle,
        SessionMemory,
        SessionContext,
        SessionSkill,
        SessionOutputStyle,
    }

    private MarkdownEditorSource activeMarkdownEditorSource;

    // Set only from OnContextTreeMouseRightButtonDown before ContextMenuOpening fires - mirrors
    // the Sessions Tree's own rightClickedNode field (the field-based approach the Tree already
    // uses successfully, vs. the ListBox's selection-forcing approach - see
    // OnSkillsListMouseRightButtonDown for why the ListBox needed a different approach).
    private ContextFileNodeViewModel? rightClickedContextNode;

    // Which of Memories/ContextSkills is showing in the session-scoped sub-panel below the tree,
    // or null if neither - only meaningful while RailSection.Sessions is active. See
    // SetSessionSubSection.
    private SessionSubSection? currentSessionSubSection;

    // Whether the session-scoped sub-panel is currently the collapsed horizontal icon strip. Not
    // persisted - re-derived from the foreground session's own SessionPanelAvailability every time
    // the active tab changes (see RefreshSessionSubPanelForActiveTab), with a manual override via
    // SetSessionSubPanelCollapsed that only lasts until the next tab switch.
    private bool isSessionSubPanelCollapsed = true;

    // Fixed height of SessionSubPanelRow while collapsed - just enough for one row of 32x32 icons.
    // Matches the RowDefinition's own XAML default, which is what's on screen before the first tab
    // ever activates (see ctor/RestoreOpenTabs - a fresh launch with no tabs to restore never calls
    // SetSessionSubPanelCollapsed at all).
    private const double CollapsedSessionSubPanelHeight = 40;

    // Applied to whichever of the two icon elements (expanded rail + collapsed strip) currently
    // has nothing behind it - see ApplySessionSubPanelIconAvailability. Dimmed via Opacity rather
    // than IsEnabled, since a greyed icon must stay clickable (see SetSessionSubSection).
    private const double DimmedIconOpacity = 0.4;

    // The user's dragged (or persisted-on-load) expanded height, kept in sync with
    // SessionSubPanelRow.Height whenever the panel is actually expanded, and restored from here
    // when re-expanding after a collapse (manual or automatic).
    private double sessionSubPanelExpandedHeight;

    // Which working directory sessionMemoryNodes was last built for - avoids rebuilding on every
    // tab reselect when the foreground session's cwd hasn't actually changed. Tracked separately
    // from sessionContextSkillsScopedWorkingDirectory below since the two sections rebuild
    // independently (only the currently-active one, on demand) - a single shared tracker would
    // incorrectly mark the *other* section's stale data as fresh once only one of them rebuilds.
    private string? sessionMemoriesScopedWorkingDirectory;

    // Same as above, for sessionContextRoot/sessionSkillNodes together (they always rebuild as a
    // pair - see RebuildSessionContextSkills).
    private string? sessionContextSkillsScopedWorkingDirectory;

    // Same as above, for sessionOutputStyleNodes - see RebuildSessionOutputStyles.
    private string? sessionOutputStylesScopedWorkingDirectory;

    private ObservableCollection<MemoryFileNodeViewModel>? sessionMemoryNodes;

    private ContextFileNodeViewModel? sessionContextRoot;

    private ObservableCollection<SkillNodeViewModel>? sessionSkillNodes;

    private ObservableCollection<OutputStyleNodeViewModel>? sessionOutputStyleNodes;

    // Mirrors suppressSkillsSelectionSideEffects, for MemoriesList specifically.
    private bool suppressMemoriesSelectionSideEffects;

    // Mirrors suppressSkillsSelectionSideEffects, for SessionOutputStylesList specifically.
    private bool suppressSessionOutputStylesSelectionSideEffects;

    // Mirrors suppressContextPanelSelectionSync, scoped to SessionContextTree/SessionSkillsList's
    // own mutual exclusivity - Memories isn't part of this pair, since it's a separate sub-section
    // never visible at the same time as these two.
    private bool suppressSessionContextPanelSelectionSync;

    // Mirrors suppressSkillsSelectionSideEffects, for SessionSkillsList specifically.
    private bool suppressSessionSkillsSelectionSideEffects;

    // Guards the revert-on-declined-discard path in OnTabStripSelectionChanged, mirroring the same
    // pattern the Skills/Context selection handlers already use - needed now that a tab switch can
    // navigate away from a dirty session-scoped document in the shared MarkdownEditor, which wasn't
    // possible before this sub-panel existed (RailSection.Sessions never used to show the editor).
    private bool suppressTabStripSelectionSideEffects;

    public MainWindow()
    {
        this.InitializeComponent();

        DarkTitleBar.Apply(this);

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

        // Eager, not lazy-on-scroll: every already-started session gets its cost computed as soon
        // as the tree loads, not just whichever ones happen to be scrolled into view. Cheap in the
        // common case - a session whose .mc.json sidecar mtime still matches its jsonl only costs a
        // stat call, not a reparse.
        foreach (var startedSession in this.controller.AllSessionNodes().Where(session => session.HasBeenStarted))
        {
            this.costMonitor.RegisterSession(startedSession);
        }

        this.TabStrip.ItemsSource = this.openTabs;

        this.layoutStore = new WindowLayoutStore();
        this.savedLayout = this.layoutStore.Load();
        ApplyWindowLayout(this, this.TreeColumn, this.savedLayout);

        // Only the *expanded* height is persisted (see WindowLayout.SessionSubPanelHeight) -
        // SessionSubPanelRow itself stays at its collapsed XAML default until the first tab
        // activates and RefreshSessionSubPanelForActiveTab decides whether to expand it.
        this.sessionSubPanelExpandedHeight = this.savedLayout?.SessionSubPanelHeight ?? 180;

        this.settingsStore = new AppSettingsStore();
        this.appSettings = this.settingsStore.Load();
        ThemeManager.Apply(this.appSettings.Theme);
        this.SettingsView.LoadSettings(this.appSettings);
        this.SettingsView.UseShiftEnterForNewlineChanged += this.OnUseShiftEnterForNewlineChanged;
        this.SettingsView.DefaultRootFolderChanged += this.OnDefaultRootFolderChanged;
        this.SettingsView.UseWorktreeByDefaultChanged += this.OnUseWorktreeByDefaultChanged;
        this.SettingsView.DefaultPermissionModeChanged += this.OnDefaultPermissionModeChanged;
        this.SettingsView.ThemeChanged += this.OnThemeChanged;
        this.SettingsView.ShowCostsChanged += this.OnShowCostsChanged;
        this.SettingsView.DisableMouseCopyChanged += this.OnDisableMouseCopyChanged;
        this.SettingsView.RemapCtrlZForUndoChanged += this.OnRemapCtrlZForUndoChanged;

        // Only trigger for a save that came from the Context tree (see
        // OnMarkdownEditorDocumentSaved) - saving a Skill doesn't need the Context tree rebuilt.
        this.MarkdownEditor.DocumentSaved += this.OnMarkdownEditorDocumentSaved;
        this.SkillEditor.DocumentSaved += this.OnSkillEditorDocumentSaved;
        this.OutputStyleEditor.DocumentSaved += this.OnOutputStyleEditorDocumentSaved;

        // Seeds the live-bindable flag every cost badge across the app reads from - see
        // CostDisplaySettings' remarks. Every later toggle flows through OnShowCostsChanged instead.
        CostDisplaySettings.Instance.ShowCosts = this.appSettings.ShowCosts;

        // Best-effort - see ClaudeSessionHooksInstaller.SettingsFilePath, which LaunchSession
        // checks before appending --settings, so a failed write here just forgoes activity icons.
        this.hooksInstaller = new ClaudeSessionHooksInstaller();
        this.hooksInstaller.EnsureInstalled();

        // Catches Shift+Delete even when the embedded terminal owns native Win32 keyboard focus,
        // which the Tree's own KeyDown handler (OnTreeKeyDown) cannot - see ShiftDeleteHook.
        this.shiftDeleteHook = new ShiftDeleteHook(this.OnDeleteShortcut);

        // Keeps arrow keys and Ctrl+C/Ctrl+Z/Ctrl+Y from leaking to the Tree while a terminal
        // session has real Win32 focus - see TerminalKeyRoutingHook's remarks. The hwnd getter is
        // lazy (evaluated per keystroke, not captured here) because this window's own hwnd
        // doesn't exist yet until WPF creates it later during Show()/OnSourceInitialized.
        this.terminalKeyRoutingHook = new TerminalKeyRoutingHook(() => new WindowInteropHelper(this).Handle);

        // Same rationale as shiftDeleteHook above - Ctrl+Z/Ctrl+Y need to reach the tree's undo
        // stack even while a terminal owns native focus. See UndoRedoShortcutHook's remarks for
        // why isModalDialogOpen is checked live rather than captured.
        this.undoRedoHook = new UndoRedoShortcutHook(() => this.isModalDialogOpen, this.OnUndoShortcut, this.OnRedoShortcut);

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

        // Belt-and-suspenders alongside WPF's own Show() activation - under Remote Desktop, the
        // brief foreground-grant a freshly launched process gets can expire before this window
        // actually appears (worse on an unoptimized Debug build), leaving it open behind everything
        // else with just a taskbar flash. See ForceForeground's remarks.
        ForceForeground.Apply(this);

        this.RestoreOpenTabs(this.savedLayout);

        if (Application.Current is App app)
        {
            app.ActivationRequests.Attach(this.HandleActivationRequest);

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
        host.Pane.ApplyTheme(ThemeManager.GetTerminalTheme(this.appSettings.Theme));
        host.Pane.NewlineOnShiftEnter = this.appSettings.UseShiftEnterForNewline;
        host.Pane.RemapCtrlZForUndo = this.appSettings.RemapCtrlZForUndo;
        host.Pane.Title = node.DisplayTitle;

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
        var session = new TerminalSession(node.WorkingDirectory, host, node.LastActivity);

        // --session-id pre-assigns this node's conversation identity on its first-ever launch;
        // every later launch passes --resume for that same id to reopen the same conversation.
        // HasBeenStarted is what remembers which one we're on, since both flags take the same GUID.
        var flag = node.HasBeenStarted ? "--resume" : "--session-id";

        // Claude Code's own TUI otherwise enables terminal mouse-reporting and auto-copies a
        // dragged/double-clicked selection to the OS clipboard via an OSC 52 escape sequence, with
        // no setting yet to opt out of just that (github.com/anthropics/claude-code/issues/60755) -
        // disabling mouse-reporting entirely is the only lever available today, hence this being
        // opt-out (Settings) rather than always-on. With it on, mouse selection stays local to the
        // terminal (highlight only, no escape codes sent to the CLI), and Ctrl+C (see
        // TerminalContainer's WM_KEYDOWN handling) is the only thing that touches the clipboard.
        var mousePrefix = this.appSettings.DisableMouseCopy ? "set CLAUDE_CODE_DISABLE_MOUSE=1 && " : string.Empty;
        var commandLine = $"cmd.exe /c {mousePrefix}claude {flag} {node.ClaudeSessionId}";

        // Only ever applies to sessions launched through multi-clod - a claude session started
        // any other way never sees this flag, so the hooks it wires up (see
        // ClaudeSessionHooksInstaller) can't affect anything outside this app.
        if (this.hooksInstaller.SettingsFilePath is { } settingsPath)
        {
            commandLine += $" --settings \"{settingsPath}\"";
        }

        // Only passed on a brand-new (--session-id) launch. Claude Code itself remembers the mode
        // a conversation was last left in (including any in-CLI Shift+Tab cycling) and restores it
        // on --resume, so passing this again here would fight that and force every resume back to
        // whatever Settings' DefaultPermissionMode currently says instead of leaving it alone.
        if (!node.HasBeenStarted)
        {
            var permissionModeFlag = this.appSettings.DefaultPermissionMode switch
            {
                ClaudePermissionMode.Auto => "auto",
                ClaudePermissionMode.AcceptEdits => "acceptEdits",
                ClaudePermissionMode.Plan => "plan",
                ClaudePermissionMode.BypassPermissions => "bypassPermissions",
                _ => "manual",
            };
            commandLine += $" --permission-mode {permissionModeFlag}";
        }

        host.Start(new TerminalLaunchOptions { WorkingDirectory = node.WorkingDirectory, CommandLine = commandLine });

        // Feeds the tree node's "Environment Variables" context menu - resolved fresh on every
        // start/resume so it reflects what this launch actually used, not what's on disk now.
        node.SetCapturedEnvironmentVariables(ClaudeEnvironmentResolver.Resolve(node.WorkingDirectory));

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
            else if (e.PropertyName == nameof(TerminalSession.ObservedClaudeSessionId))
            {
                // Claude Code's hooks just reported a session_id that doesn't match what this node
                // has on file - it moved onto a different transcript underneath us (most commonly
                // /clear inside the CLI). Correct and re-persist immediately so the next launch's
                // --resume targets the conversation the user is actually using, not an abandoned one.
                if (session.ObservedClaudeSessionId is { } observedSessionId && observedSessionId != node.ClaudeSessionId)
                {
                    DebugLog.LogTerminal($"Correcting node={node.Name} old={node.ClaudeSessionId} new={observedSessionId}");
                    node.UpdateClaudeSessionId(observedSessionId);
                    this.controller.ScheduleSave();
                }
                else
                {
                    DebugLog.LogTerminal(
                        $"ObservedClaudeSessionId changed but no correction: node={node.Name} observed={session.ObservedClaudeSessionId} current={node.ClaudeSessionId}");
                }
            }
            else if (e.PropertyName == nameof(TerminalSession.Activity))
            {
                // Only worth a sound if the user isn't already looking at it - either this isn't
                // the on-screen session pane, or the window itself isn't focused. IsActive/pane
                // Visibility are both read fresh here rather than cached, since either can change
                // out from under a long-lived session between activity events.
                var isOnScreen = this.IsActive && host.Pane.View.Visibility == Visibility.Visible;
                if (!isOnScreen)
                {
                    if (session.Activity is SessionActivity.NeedsInput or SessionActivity.Error)
                    {
                        // Error reuses the NeedsInput cue rather than getting its own sound asset -
                        // both mean "go look at this session", just for different reasons.
                        SessionActivitySounds.PlayNeedsInput();
                    }
                    else if (session.Activity == SessionActivity.Done)
                    {
                        SessionActivitySounds.PlayDone();
                    }
                }

                // Working itself is never persisted (see SessionRecord.LastActivity's remarks) -
                // only save once node.LastActivity has actually settled onto something new, so a
                // relaunch/restart shows the same glyph this session was left in.
                if (session.Activity != SessionActivity.Working)
                {
                    this.controller.ScheduleSave();
                }
            }
            else if (e.PropertyName == nameof(TerminalSession.State) && session.State == SessionState.Faulted)
            {
                // See SessionDiagnosticsLog - captures the exit code and whatever the process
                // printed right before dying, since by the time a user notices "Faulted" the
                // terminal pane itself has often already moved on or been closed.
                SessionDiagnosticsLog.LogFault(node.Name, node.WorkingDirectory, commandLine, host.LastExitCode, host.LastOutputTail);
            }
        };

        node.HasBeenStarted = true;
        this.controller.ScheduleSave();
        this.costMonitor.RegisterSession(node);
    }

    private void StopSession(SessionNodeViewModel node)
    {
        if (node.LiveSession is not { } session)
        {
            return;
        }

        // Clear any latched NeedsInput/Done/Interrupted glyph before detaching - otherwise
        // DetachLiveSession's Activity falls back to lastActivity, which would still hold the
        // latched value and keep showing that glyph instead of the dormant hollow dot.
        node.ClearLatchedActivity();

        // Per WpfSessionHost/ConPtyConnection's single-use guard, this host instance can never be
        // Start()ed again - relaunching this node later always constructs a brand-new host, passing
        // --resume since HasBeenStarted is already persisted true.
        session.Host.Dispose();
        this.SessionViewHost.Children.Remove(session.Host.Pane.View);
        node.DetachLiveSession();
    }

    // Selecting a Session node opens/activates its tab; selecting a Project node (or nothing, e.g.
    // after a delete) is deliberately a no-op here - it leaves whichever tab is currently active
    // untouched, same as clicking a folder in an editor's sidebar doesn't blank the open editor.
    private void OnTreeSelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (this.suppressSelectionSideEffects)
        {
            return;
        }

        if (e.NewValue is SessionNodeViewModel session)
        {
            // The user just looked at this session - a latched NeedsInput/Done icon (if any) has
            // done its job. No-ops on a dormant node or one still Working.
            session.ClearLatchedActivity();

            this.OpenTab(session);
        }
    }

    // Ensures `session` has a tab in the strip and makes it the active one. Reselecting the same
    // tree node twice in a row (or via RestoreOpenTabs/HandleFromHereSession) never changes
    // TabStrip.SelectedItem, so ActivateTab is called directly in that case since
    // OnTabStripSelectionChanged wouldn't otherwise fire.
    private void OpenTab(SessionNodeViewModel session)
    {
        if (!this.openTabs.Contains(session))
        {
            this.openTabs.Add(session);
        }

        if (ReferenceEquals(this.TabStrip.SelectedItem, session))
        {
            this.ActivateTab(session);
        }
        else
        {
            this.TabStrip.SelectedItem = session;
        }
    }

    // Launches (if needed) and shows the given tab's pane. The sole place that flips a pane
    // Visible - callers (OpenTab, OnTabStripSelectionChanged, RefreshSessionsCanvas) all funnel
    // through here.
    private void ActivateTab(SessionNodeViewModel session)
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
        // bypassing WPF's focus system. We're still nested inside the very TreeViewItem/ListBoxItem
        // GotFocus/Selected dispatch that raised this selection-changed event, so stealing real
        // Win32 focus now (before that dispatch unwinds) leaves WPF's FocusManager out of sync with
        // what's actually focused. Left alone, this causes exactly the ancestor-misrouting bug
        // described on OnItemPreviewMouseLeftButtonDown above: the following click resolves
        // selection to the Project row instead of the session actually clicked. Posting the focus
        // call lets the current dispatch finish first. Priority must be below Render
        // (DispatcherPriority.Normal, the default, is numerically *higher* than Render - it would
        // run before the layout pass that the Visibility flip just above schedules, and focusing a
        // control before it's actually been laid out/rendered silently fails). ContextIdle
        // guarantees the pending layout/render for the pane just made Visible has completed first.
        var liveSession = session.LiveSession;
        this.Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() => liveSession.Host.Pane.Focus()));
    }

    // Fires on both user clicks on a tab and the programmatic TabStrip.SelectedItem assignments in
    // OpenTab/CloseTab.
    private void OnTabStripSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (this.suppressTabStripSelectionSideEffects)
        {
            return;
        }

        if (this.IsShowingSessionScopedDocument && e.RemovedItems.Count > 0 && !this.TryNavigateAwayFromActiveEditor())
        {
            this.suppressTabStripSelectionSideEffects = true;
            this.TabStrip.SelectedItem = e.RemovedItems[0];
            this.suppressTabStripSelectionSideEffects = false;
            return;
        }

        if (e.RemovedItems.Count > 0 && e.RemovedItems[0] is SessionNodeViewModel { LiveSession: { } oldSession })
        {
            oldSession.Host.Pane.View.Visibility = Visibility.Collapsed;
        }

        if (this.TabStrip.SelectedItem is SessionNodeViewModel session)
        {
            session.ClearLatchedActivity();
            this.ActivateTab(session);

            // Keep the tree selection following the active tab (without recursing back into
            // OpenTab, which would just re-select the already-active tab) - so the tree's
            // highlighted row always matches what's on screen.
            if (!ReferenceEquals(this.Tree.SelectedItem, session))
            {
                this.suppressSelectionSideEffects = true;
                session.IsSelected = true;
                this.suppressSelectionSideEffects = false;
            }

            this.RefreshSessionSubPanelForActiveTab();
        }
        else
        {
            this.RefreshSessionSubPanelForActiveTab();
            this.ErrorText.Visibility = Visibility.Collapsed;
            this.PlaceholderText.Visibility = Visibility.Visible;
        }
    }

    // The tab strip's own X is the only close affordance sessions have now (the pane itself no
    // longer shows one - see WpfTerminalPane), so it stops the session outright rather than just
    // hiding the tab: same StopSession call as the tree's "Stop" context-menu item, which is what
    // hollows the tree's status dot (SessionNodeViewModel.IsHollow follows IsRunning).
    private void OnCloseTabClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is SessionNodeViewModel session)
        {
            this.CloseTab(session);
            this.StopSession(session);
        }
    }

    // Removes `session`'s tab from the strip. Does not itself stop the session - callers that want
    // that (OnCloseTabClick, OnDelete) call StopSession separately. Used on its own by
    // RestoreOpenTabs/OnTreeSelectedItemChanged's OpenTab path, where the session should keep
    // running in the background.
    private void CloseTab(SessionNodeViewModel session)
    {
        var index = this.openTabs.IndexOf(session);
        if (index < 0)
        {
            return;
        }

        var wasActive = ReferenceEquals(this.TabStrip.SelectedItem, session);
        this.openTabs.RemoveAt(index);

        if (!wasActive)
        {
            return;
        }

        if (this.openTabs.Count > 0)
        {
            // Prefer the tab that slid into this index (i.e. what was the next tab), falling back
            // to the new last tab - matches a browser/editor tab strip's close behavior.
            this.TabStrip.SelectedItem = this.openTabs[Math.Min(index, this.openTabs.Count - 1)];
        }
        else if (session.IsSelected)
        {
            // No tabs left to fall back to. Removing the last tab already cleared
            // TabStrip.SelectedItem to null (via OnTabStripSelectionChanged, triggered by the
            // ObservableCollection change above), which shows the placeholder - but the tree still
            // shows this node selected. Deselect it too, so clicking it again actually fires a new
            // SelectedItemChanged (WPF doesn't raise one for reselecting an already-selected node)
            // and reopens its tab instead of silently doing nothing.
            session.IsSelected = false;
        }
    }

    private void OnTabItemPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        this.tabDragStartPoint = e.GetPosition(null);
        this.tabDragStartSession = (sender as ListBoxItem)?.DataContext as SessionNodeViewModel;
    }

    // Middle-click closes a tab the same way its X button does (browser/editor convention), without
    // going through selection first - e.Handled stops the ListBox from also selecting the tab.
    private void OnTabItemPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Middle)
        {
            return;
        }

        e.Handled = true;
        if ((sender as FrameworkElement)?.DataContext is SessionNodeViewModel session)
        {
            this.CloseTab(session);
            this.StopSession(session);
        }
    }

    private void OnTabItemPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || this.tabDragStartPoint is not { } start || this.tabDragStartSession is not { } session)
        {
            return;
        }

        var current = e.GetPosition(null);
        if (Math.Abs(current.X - start.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(current.Y - start.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        this.tabDragStartPoint = null;
        this.tabDragStartSession = null;

        DragDrop.DoDragDrop((DependencyObject)sender, new DataObject(TabDragFormat, session), DragDropEffects.Move);
    }

    private void OnTabItemDragOver(object sender, DragEventArgs e)
    {
        e.Effects = DragDropEffects.None;
        e.Handled = true;

        if (sender is ListBoxItem item && e.Data.GetData(TabDragFormat) is SessionNodeViewModel dragged && item.DataContext is SessionNodeViewModel target &&
            !ReferenceEquals(dragged, target))
        {
            e.Effects = DragDropEffects.Move;
        }
    }

    private void OnTabItemDrop(object sender, DragEventArgs e)
    {
        e.Handled = true;

        if (sender is not ListBoxItem item || e.Data.GetData(TabDragFormat) is not SessionNodeViewModel dragged || item.DataContext is not SessionNodeViewModel target ||
            ReferenceEquals(dragged, target))
        {
            return;
        }

        var before = GetHorizontalDropPosition(item, e.GetPosition(item)) == DropPosition.Before;
        this.MoveTab(dragged, target, before);
    }

    private void OnTabStripDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetData(TabDragFormat) is SessionNodeViewModel ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnTabStripDrop(object sender, DragEventArgs e)
    {
        e.Handled = true;

        // Only reachable for a genuine past-the-last-tab drop - a drop landing on a tab is handled
        // (and e.Handled = true is set) by that tab's own OnTabItemDrop before bubbling gets here.
        if (e.Data.GetData(TabDragFormat) is not SessionNodeViewModel dragged)
        {
            return;
        }

        var oldIndex = this.openTabs.IndexOf(dragged);
        if (oldIndex >= 0 && oldIndex != this.openTabs.Count - 1)
        {
            this.openTabs.Move(oldIndex, this.openTabs.Count - 1);
        }
    }

    // Moves `dragged` next to `target` within openTabs (before or after it), preserving
    // TabStrip.SelectedItem's identity - ObservableCollection.Move (unlike Remove+Insert) raises a
    // single Move notification that ListBox applies without disturbing selection.
    private void MoveTab(SessionNodeViewModel dragged, SessionNodeViewModel target, bool before)
    {
        var oldIndex = this.openTabs.IndexOf(dragged);
        var targetIndex = this.openTabs.IndexOf(target);
        if (oldIndex < 0 || targetIndex < 0)
        {
            return;
        }

        var newIndex = before ? targetIndex : targetIndex + 1;
        if (oldIndex < newIndex)
        {
            newIndex--;
        }

        if (newIndex != oldIndex)
        {
            this.openTabs.Move(oldIndex, newIndex);
        }
    }

    // ListBoxItem.ActualWidth is just this tab's own rendered width (tabs don't nest, unlike
    // TreeViewItem), so - unlike GetDropPosition's height-based fraction below - the split is a
    // simple half rather than a three-way Before/Into/After band; Into is never returned.
    private static DropPosition GetHorizontalDropPosition(ListBoxItem item, Point position)
    {
        return item.ActualWidth > 0 && position.X / item.ActualWidth > 0.5 ? DropPosition.After : DropPosition.Before;
    }

    // Re-syncs the canvas with whatever tab is currently active - used when returning to the
    // Sessions rail section (see SetRailSection), where simply replaying the tree selection would
    // miss the case where a Project node (not the active tab's session) is what's tree-selected.
    private void RefreshSessionsCanvas()
    {
        if (this.TabStrip.SelectedItem is SessionNodeViewModel session)
        {
            this.ActivateTab(session);
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

    private void OnContextRailIconClick(object sender, MouseButtonEventArgs e)
    {
        this.SetRailSection(RailSection.Context);
    }

    private void OnSettingsRailIconClick(object sender, MouseButtonEventArgs e)
    {
        this.SetRailSection(RailSection.Settings);
    }

    private void SetRailSection(RailSection section)
    {
        if (this.currentRailSection == section)
        {
            return;
        }

        // Leaving Context with an unsaved edit needs the same discard-confirmation as switching to
        // a different skill/Context node within the panel - see MarkdownEditorView.TryNavigateAway.
        if (this.currentRailSection == RailSection.Context && !this.TryNavigateAwayFromActiveEditor())
        {
            return;
        }

        this.currentRailSection = section;

        this.SessionsAccentBar.Visibility = section == RailSection.Sessions ? Visibility.Visible : Visibility.Collapsed;
        this.ContextAccentBar.Visibility = section == RailSection.Context ? Visibility.Visible : Visibility.Collapsed;
        this.SettingsAccentBar.Visibility = section == RailSection.Settings ? Visibility.Visible : Visibility.Collapsed;
        this.Tree.Visibility = section == RailSection.Sessions ? Visibility.Visible : Visibility.Collapsed;
        this.SessionSubPanelHost.Visibility = section == RailSection.Sessions ? Visibility.Visible : Visibility.Collapsed;
        this.ContextPanel.Visibility = section == RailSection.Context ? Visibility.Visible : Visibility.Collapsed;
        this.TabStripHost.Visibility = section == RailSection.Sessions ? Visibility.Visible : Visibility.Collapsed;
        this.UpdateSessionSubPanelSplitterVisibility();

        // All three cleared unconditionally here - the branches below re-show whichever one
        // actually applies (RefreshContextPanelCanvas may re-show MarkdownEditor or SkillEditor;
        // the Settings branch always shows SettingsView). None is otherwise toggled anywhere else.
        this.HideActiveDocumentEditors();
        this.SettingsView.Visibility = Visibility.Collapsed;

        if (section == RailSection.Context)
        {
            // SessionViewHost's children are never hidden as a whole - only whichever pane is the
            // active tab - so that pane needs hiding explicitly here; RefreshSessionsCanvas
            // naturally re-shows it when switching back.
            this.HideActiveSessionPane();
            this.EnsureSkillsLoaded();
            this.EnsureOutputStylesLoaded();
            this.EnsureContextTreeLoaded();
            this.RefreshContextPanelCanvas();
        }
        else if (section == RailSection.Settings)
        {
            this.HideActiveSessionPane();
            this.PlaceholderText.Visibility = Visibility.Collapsed;
            this.ErrorText.Visibility = Visibility.Collapsed;
            this.SettingsView.Visibility = Visibility.Visible;
        }
        else
        {
            this.RefreshSessionsCanvas();
        }
    }

    // AppSettingsStore is best-effort (see its own remarks) - a failed Save here just means the
    // toggle won't survive a restart, not a crash.
    private void OnUseShiftEnterForNewlineChanged(object? sender, bool useShiftEnterForNewline)
    {
        this.appSettings = this.appSettings with { UseShiftEnterForNewline = useShiftEnterForNewline };
        this.settingsStore.Save(this.appSettings);

        // Only already-running sessions need pushing live - LaunchSession reads this.appSettings
        // fresh for every session started after this point.
        foreach (var node in this.controller.AllSessionNodes())
        {
            if (node.LiveSession is { } session)
            {
                session.Host.Pane.NewlineOnShiftEnter = useShiftEnterForNewline;
            }
        }
    }

    private void OnDefaultRootFolderChanged(object? sender, string? defaultRootFolder)
    {
        this.appSettings = this.appSettings with { DefaultRootFolder = defaultRootFolder };
        this.settingsStore.Save(this.appSettings);
    }

    private void OnUseWorktreeByDefaultChanged(object? sender, bool useWorktreeByDefault)
    {
        this.appSettings = this.appSettings with { UseWorktreeByDefault = useWorktreeByDefault };
        this.settingsStore.Save(this.appSettings);
    }

    private void OnDefaultPermissionModeChanged(object? sender, ClaudePermissionMode defaultPermissionMode)
    {
        this.appSettings = this.appSettings with { DefaultPermissionMode = defaultPermissionMode };
        this.settingsStore.Save(this.appSettings);
    }

    private void OnShowCostsChanged(object? sender, bool showCosts)
    {
        this.appSettings = this.appSettings with { ShowCosts = showCosts };
        this.settingsStore.Save(this.appSettings);

        // Every cost badge across the tree, tabs, and any open Session Log window is bound to this
        // one flag (via CostVisibilityConverter) - setting it here is the entire live-push, no
        // per-session/per-window loop needed like OnUseShiftEnterForNewlineChanged's.
        CostDisplaySettings.Instance.ShowCosts = showCosts;
    }

    // No live push to already-running sessions - like DefaultPermissionModeChanged, this is only
    // read by LaunchSession at the moment a brand-new claude process is started.
    private void OnDisableMouseCopyChanged(object? sender, bool disableMouseCopy)
    {
        this.appSettings = this.appSettings with { DisableMouseCopy = disableMouseCopy };
        this.settingsStore.Save(this.appSettings);
    }

    private void OnRemapCtrlZForUndoChanged(object? sender, bool remapCtrlZForUndo)
    {
        this.appSettings = this.appSettings with { RemapCtrlZForUndo = remapCtrlZForUndo };
        this.settingsStore.Save(this.appSettings);

        // Unlike DisableMouseCopy (an env var only read at launch), this is a per-pane remap
        // TerminalContainer applies live - same push-to-already-running-sessions pattern as
        // OnUseShiftEnterForNewlineChanged.
        foreach (var node in this.controller.AllSessionNodes())
        {
            if (node.LiveSession is { } session)
            {
                session.Host.Pane.RemapCtrlZForUndo = remapCtrlZForUndo;
            }
        }
    }

    private void OnThemeChanged(object? sender, AppTheme theme)
    {
        this.appSettings = this.appSettings with { Theme = theme };
        this.settingsStore.Save(this.appSettings);

        ThemeManager.Apply(theme);

        // Chrome picks up the new theme automatically (every color is a DynamicResource), but
        // each live terminal pane's colors were baked in at ApplyTheme time in LaunchSession, so
        // already-running sessions need pushing explicitly - same pattern as
        // OnUseShiftEnterForNewlineChanged. Sessions started after this point read
        // this.appSettings.Theme fresh in LaunchSession.
        var terminalTheme = ThemeManager.GetTerminalTheme(theme);
        foreach (var node in this.controller.AllSessionNodes())
        {
            if (node.LiveSession is { } session)
            {
                session.Host.Pane.ApplyTheme(terminalTheme);
            }
        }

        this.MarkdownEditor.RefreshTheme();
        this.SkillEditor.RefreshTheme();
    }

    private void HideActiveSessionPane()
    {
        if (this.TabStrip.SelectedItem is SessionNodeViewModel { LiveSession: { } session })
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

        this.RebuildSkillsList();
    }

    // Split out from EnsureSkillsLoaded so a Skill saved/created through SkillEditor (see
    // OnSkillEditorDocumentSaved) can force a rescan instead of only ever loading once per run,
    // unlike EnsureSkillsLoaded's own once-per-launch convention for the read-only case.
    private void RebuildSkillsList()
    {
        var skills = new SkillDiscoveryService().ScanPersonalSkills();
        this.skillNodes = new ObservableCollection<SkillNodeViewModel>(skills.Select(s => new SkillNodeViewModel(s)));
        this.SkillsList.ItemsSource = this.skillNodes;
        this.SkillsList.Visibility = this.skillNodes.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        this.SkillsEmptyText.Visibility = this.skillNodes.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        // Dimmed but still clickable - same "greyed but expandable to an empty-state message" rule
        // the session sub-panel's icons already follow (see ApplySessionSubPanelIconAvailability).
        this.SkillsGroupHeader.Opacity = this.skillNodes.Count == 0 ? DimmedIconOpacity : 1.0;
    }

    private void EnsureOutputStylesLoaded()
    {
        if (this.outputStyleNodes is not null)
        {
            return;
        }

        this.RebuildOutputStylesList();
    }

    // Split out from EnsureOutputStylesLoaded so an output style saved/created through
    // OutputStyleEditor (see OnOutputStyleEditorDocumentSaved) can force a rescan instead of only
    // ever loading once per run, unlike EnsureOutputStylesLoaded's own once-per-launch convention
    // for the read-only case.
    private void RebuildOutputStylesList()
    {
        var outputStyles = new OutputStyleDiscoveryService().ScanPersonalOutputStyles();
        this.outputStyleNodes = new ObservableCollection<OutputStyleNodeViewModel>(outputStyles.Select(s => new OutputStyleNodeViewModel(s)));
        this.OutputStylesList.ItemsSource = this.outputStyleNodes;
        this.OutputStylesList.Visibility = this.outputStyleNodes.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        this.OutputStylesEmptyText.Visibility = this.outputStyleNodes.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        this.OutputStylesGroupHeader.Opacity = this.outputStyleNodes.Count == 0 ? DimmedIconOpacity : 1.0;
    }

    private void EnsureContextTreeLoaded()
    {
        if (this.contextRoot is not null)
        {
            return;
        }

        this.RebuildContextTree();
    }

    private void OnMarkdownEditorDocumentSaved(object? sender, string filePath)
    {
        switch (this.activeMarkdownEditorSource)
        {
            case MarkdownEditorSource.TopLevelContext:
                this.RebuildContextTree();
                break;
            case MarkdownEditorSource.SessionContext:
                this.RebuildSessionContextTreeOnly();
                break;
        }
    }

    // Refreshes whichever list (personal or project-level) the saved/newly-created skill belongs
    // to - there's no FileSystemWatcher, matching every other list in this app's own
    // rescan-on-next-launch-only convention, so a save is the only other refresh trigger. Also
    // re-checks the affected session's SessionPanelAvailability so its rail icon un-greys
    // immediately if this was that session's first skill.
    private void OnSkillEditorDocumentSaved(object? sender, string filePath)
    {
        switch (this.activeMarkdownEditorSource)
        {
            case MarkdownEditorSource.TopLevelSkill:
                this.RebuildSkillsList();
                break;
            case MarkdownEditorSource.SessionSkill:
                if (this.TabStrip.SelectedItem is SessionNodeViewModel session)
                {
                    this.RebuildSessionContextSkills(session.WorkingDirectory);
                    this.RefreshSessionSubPanelForActiveTab();
                }

                break;
        }
    }

    // Mirrors OnSkillEditorDocumentSaved - refreshes whichever list (personal or project-level)
    // the saved/newly-created output style belongs to, and re-checks the affected session's
    // SessionPanelAvailability so its rail icon un-greys immediately if this was that session's
    // first output style.
    private void OnOutputStyleEditorDocumentSaved(object? sender, string filePath)
    {
        switch (this.activeMarkdownEditorSource)
        {
            case MarkdownEditorSource.TopLevelOutputStyle:
                this.RebuildOutputStylesList();
                break;
            case MarkdownEditorSource.SessionOutputStyle:
                if (this.TabStrip.SelectedItem is SessionNodeViewModel outputStyleSession)
                {
                    this.RebuildSessionOutputStyles(outputStyleSession.WorkingDirectory);
                    this.RefreshSessionSubPanelForActiveTab();
                }

                break;
        }
    }

    // A full rebuild (rather than patching just the saved node's subtree) is deliberately simpler
    // and trivially correct for diamond imports (see ContextTreeBuilder's remarks) - the only cost
    // is losing collapse/expand state that isn't captured below, so previously-expanded nodes are
    // re-expanded by resolved path after the new tree is built.
    private void RebuildContextTree()
    {
        var expandedPaths = this.contextRoot is null
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : CollectExpandedPaths(this.contextRoot);

        this.contextRoot = ContextTreeBuilder.BuildRoot();
        ReapplyExpansion(this.contextRoot, expandedPaths);
        this.ContextTree.ItemsSource = new[] { this.contextRoot };
    }

    private static HashSet<string> CollectExpandedPaths(ContextFileNodeViewModel node)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        CollectExpandedPaths(node, result);
        return result;
    }

    private static void CollectExpandedPaths(ContextFileNodeViewModel node, HashSet<string> result)
    {
        if (node.IsExpanded)
        {
            result.Add(node.ResolvedPath);
        }

        foreach (var child in node.Children.OfType<ContextFileNodeViewModel>())
        {
            CollectExpandedPaths(child, result);
        }
    }

    private static void ReapplyExpansion(ContextFileNodeViewModel node, HashSet<string> expandedPaths)
    {
        if (expandedPaths.Contains(node.ResolvedPath))
        {
            node.IsExpanded = true;
        }

        foreach (var child in node.Children.OfType<ContextFileNodeViewModel>())
        {
            ReapplyExpansion(child, expandedPaths);
        }
    }

    private void OnSkillsListSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (this.suppressSkillsSelectionSideEffects || this.suppressContextPanelSelectionSync)
        {
            return;
        }

        if (e.RemovedItems.Count > 0 && !this.TryNavigateAwayFromActiveEditor())
        {
            this.suppressSkillsSelectionSideEffects = true;
            this.SkillsList.SelectedItem = e.RemovedItems[0];
            this.suppressSkillsSelectionSideEffects = false;
            return;
        }

        // Clear ContextTree's/OutputStylesList's selection so only one of the three stacked
        // controls is ever visually "active" - guarded so this side effect doesn't recursively
        // re-enter their own selection-changed handlers (see each handler's own top-of-method
        // guard).
        if (this.SkillsList.SelectedItem is not null)
        {
            this.ClearOtherContextSelections(this.SkillsList);
        }

        this.RefreshContextPanelCanvas();
    }

    private void OnOutputStylesListSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (this.suppressOutputStylesSelectionSideEffects || this.suppressContextPanelSelectionSync)
        {
            return;
        }

        if (e.RemovedItems.Count > 0 && !this.TryNavigateAwayFromActiveEditor())
        {
            this.suppressOutputStylesSelectionSideEffects = true;
            this.OutputStylesList.SelectedItem = e.RemovedItems[0];
            this.suppressOutputStylesSelectionSideEffects = false;
            return;
        }

        if (this.OutputStylesList.SelectedItem is not null)
        {
            this.ClearOtherContextSelections(this.OutputStylesList);
        }

        this.RefreshContextPanelCanvas();
    }

    private void OnContextTreeSelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (this.suppressContextPanelSelectionSync)
        {
            return;
        }

        if (e.OldValue is ContextFileNodeViewModel oldNode && !this.TryNavigateAwayFromActiveEditor())
        {
            this.suppressContextPanelSelectionSync = true;
            oldNode.IsSelected = true;
            this.suppressContextPanelSelectionSync = false;
            return;
        }

        // Clear SkillsList's/OutputStylesList's selection so only one of the three stacked
        // controls is ever visually "active" - guarded so this side effect doesn't recursively
        // re-enter their own selection-changed handlers (see each handler's own top-of-method
        // guard).
        if (e.NewValue is ContextFileNodeViewModel)
        {
            this.ClearOtherContextSelections(this.ContextTree);
        }

        this.RefreshContextPanelCanvas();
    }

    // Shared by OnSkillsListSelectionChanged/OnOutputStylesListSelectionChanged/
    // OnContextTreeSelectedItemChanged now that three controls (rather than two) share one
    // MarkdownEditor and must stay mutually exclusive - each guarded the same way the original
    // pairwise clearing was, so clearing one doesn't recursively re-trigger its own handler.
    private void ClearOtherContextSelections(object except)
    {
        if (!ReferenceEquals(except, this.ContextTree) && this.ContextTree.SelectedItem is ContextFileNodeViewModel contextNode)
        {
            this.suppressContextPanelSelectionSync = true;
            contextNode.IsSelected = false;
            this.suppressContextPanelSelectionSync = false;
        }

        if (!ReferenceEquals(except, this.SkillsList) && this.SkillsList.SelectedItem is not null)
        {
            this.suppressContextPanelSelectionSync = true;
            this.SkillsList.SelectedItem = null;
            this.suppressContextPanelSelectionSync = false;
        }

        if (!ReferenceEquals(except, this.OutputStylesList) && this.OutputStylesList.SelectedItem is not null)
        {
            this.suppressContextPanelSelectionSync = true;
            this.OutputStylesList.SelectedItem = null;
            this.suppressContextPanelSelectionSync = false;
        }
    }

    private void RefreshContextPanelCanvas()
    {
        if (this.ContextTree.SelectedItem is ContextFileNodeViewModel contextNode)
        {
            this.PlaceholderText.Visibility = Visibility.Collapsed;
            this.ErrorText.Visibility = Visibility.Collapsed;
            this.SkillEditor.Visibility = Visibility.Collapsed;
            this.OutputStyleEditor.Visibility = Visibility.Collapsed;
            this.MarkdownEditor.Visibility = Visibility.Visible;
            this.activeMarkdownEditorSource = MarkdownEditorSource.TopLevelContext;
            this.MarkdownEditor.LoadDocument(new MarkdownEditorTarget(contextNode.ResolvedPath, contextNode.Name));
        }
        else if (this.SkillsList.SelectedItem is SkillNodeViewModel node)
        {
            this.PlaceholderText.Visibility = Visibility.Collapsed;
            this.ErrorText.Visibility = Visibility.Collapsed;
            this.MarkdownEditor.Visibility = Visibility.Collapsed;
            this.OutputStyleEditor.Visibility = Visibility.Collapsed;
            this.SkillEditor.Visibility = Visibility.Visible;
            this.activeMarkdownEditorSource = MarkdownEditorSource.TopLevelSkill;
            this.SkillEditor.LoadDocument(node.Info.FilePath);
        }
        else if (this.OutputStylesList.SelectedItem is OutputStyleNodeViewModel outputStyleNode)
        {
            this.PlaceholderText.Visibility = Visibility.Collapsed;
            this.ErrorText.Visibility = Visibility.Collapsed;
            this.SkillEditor.Visibility = Visibility.Collapsed;
            this.MarkdownEditor.Visibility = Visibility.Collapsed;
            this.OutputStyleEditor.Visibility = Visibility.Visible;
            this.activeMarkdownEditorSource = MarkdownEditorSource.TopLevelOutputStyle;
            this.OutputStyleEditor.LoadDocument(outputStyleNode.Info.FilePath);
        }
        else
        {
            this.HideActiveDocumentEditors();
            this.ErrorText.Visibility = Visibility.Collapsed;
            this.PlaceholderText.Visibility = Visibility.Visible;
            this.activeMarkdownEditorSource = MarkdownEditorSource.None;
        }
    }

    private void OnSkillsGroupHeaderClick(object sender, MouseButtonEventArgs e) =>
        this.ToggleContextGroup(ref this.skillsGroupExpanded, this.SkillsGroupContent, this.SkillsGroupChevronRotate, this.SkillsList);

    private void OnOutputStylesGroupHeaderClick(object sender, MouseButtonEventArgs e) =>
        this.ToggleContextGroup(ref this.outputStylesGroupExpanded, this.OutputStylesGroupContent, this.OutputStylesGroupChevronRotate, this.OutputStylesList);

    // Collapsing a group whose own item is the one currently loaded into MarkdownEditor must not
    // silently discard an unsaved edit: clearing the list's selection re-enters that list's own
    // guarded SelectionChanged handler (OnSkillsListSelectionChanged/OnOutputStylesListSelectionChanged),
    // which reverts the selection right back if TryNavigateAwayFromActiveEditor() is declined - if
    // that happened (SelectedItem is still set afterward), the collapse is aborted here too.
    private void ToggleContextGroup(ref bool expanded, UIElement content, RotateTransform chevron, ListBox list)
    {
        if (expanded && list.SelectedItem is not null)
        {
            list.SelectedItem = null;
            if (list.SelectedItem is not null)
            {
                return;
            }
        }

        expanded = !expanded;
        content.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        chevron.Angle = expanded ? 90 : 0;
    }

    private bool IsShowingSessionScopedDocument =>
        this.activeMarkdownEditorSource is MarkdownEditorSource.SessionMemory or MarkdownEditorSource.SessionContext or MarkdownEditorSource.SessionSkill or MarkdownEditorSource.SessionOutputStyle;

    // Skills/Output Styles now show in their own separate editor controls rather than the shared
    // MarkdownEditor - these let every existing navigate-away/hide call site stay a single call
    // regardless of which of the three controls activeMarkdownEditorSource says is actually
    // showing.
    private bool IsActiveEditorSkill =>
        this.activeMarkdownEditorSource is MarkdownEditorSource.TopLevelSkill or MarkdownEditorSource.SessionSkill;

    private bool IsActiveEditorOutputStyle =>
        this.activeMarkdownEditorSource is MarkdownEditorSource.TopLevelOutputStyle or MarkdownEditorSource.SessionOutputStyle;

    private bool TryNavigateAwayFromActiveEditor() =>
        this.IsActiveEditorSkill ? this.SkillEditor.TryNavigateAway()
        : this.IsActiveEditorOutputStyle ? this.OutputStyleEditor.TryNavigateAway()
        : this.MarkdownEditor.TryNavigateAway();

    private void HideActiveDocumentEditors()
    {
        this.MarkdownEditor.Visibility = Visibility.Collapsed;
        this.SkillEditor.Visibility = Visibility.Collapsed;
        this.OutputStyleEditor.Visibility = Visibility.Collapsed;
    }

    // Recomputes memories/context-skills availability for the newly foreground session, dims the
    // two icons (both orientations) accordingly, and auto-collapses/expands the sub-panel based on
    // whether there's anything at all to show - the one place collapse state changes automatically.
    // A manual expand via SetSessionSubPanelCollapsed(false) (clicking an icon while collapsed, or
    // re-expanding) only holds until the next call to this method.
    private void RefreshSessionSubPanelForActiveTab()
    {
        if (this.TabStrip.SelectedItem is not SessionNodeViewModel session)
        {
            this.SetSessionSubPanelCollapsed(true);
            return;
        }

        var availability = SessionPanelAvailability.Compute(session.WorkingDirectory);
        this.ApplySessionSubPanelIconAvailability(availability);
        this.SetSessionSubPanelCollapsed(!availability.HasMemories && !availability.HasContextSkills && !availability.HasOutputStyles);

        if (this.currentSessionSubSection is { } section)
        {
            this.EnsureSessionSubSectionLoaded(section, session.WorkingDirectory);
        }
    }

    private void ApplySessionSubPanelIconAvailability(SessionPanelAvailability availability)
    {
        var memoriesOpacity = availability.HasMemories ? 1.0 : DimmedIconOpacity;
        var contextSkillsOpacity = availability.HasContextSkills ? 1.0 : DimmedIconOpacity;
        var outputStylesOpacity = availability.HasOutputStyles ? 1.0 : DimmedIconOpacity;

        this.SessionMemoriesIcon.Opacity = memoriesOpacity;
        this.SessionMemoriesIconCollapsed.Opacity = memoriesOpacity;
        this.SessionContextSkillsIcon.Opacity = contextSkillsOpacity;
        this.SessionContextSkillsIconCollapsed.Opacity = contextSkillsOpacity;
        this.SessionOutputStylesIcon.Opacity = outputStylesOpacity;
        this.SessionOutputStylesIconCollapsed.Opacity = outputStylesOpacity;
    }

    private void SetSessionSubPanelCollapsed(bool collapsed)
    {
        if (collapsed == this.isSessionSubPanelCollapsed)
        {
            return;
        }

        if (collapsed)
        {
            // Remember the expanded height so re-expanding (manually or via the next tab switch)
            // restores exactly where the user left it, rather than snapping back to the persisted
            // default every time.
            this.sessionSubPanelExpandedHeight = this.SessionSubPanelRow.Height.Value;
            this.SetSessionSubSection(null);
        }

        this.isSessionSubPanelCollapsed = collapsed;
        this.SessionSubPanelExpanded.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;
        this.SessionSubPanelCollapsedStrip.Visibility = collapsed ? Visibility.Visible : Visibility.Collapsed;
        this.SessionSubPanelRow.Height = new GridLength(collapsed ? CollapsedSessionSubPanelHeight : this.sessionSubPanelExpandedHeight);
        this.UpdateSessionSubPanelSplitterVisibility();
    }

    private void UpdateSessionSubPanelSplitterVisibility()
    {
        this.SessionSubPanelSplitter.Visibility = this.currentRailSection == RailSection.Sessions && !this.isSessionSubPanelCollapsed
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void SetSessionSubSection(SessionSubSection? section)
    {
        // Clicking the already-active icon toggles it off.
        if (section == this.currentSessionSubSection)
        {
            section = null;
        }

        if (this.IsShowingSessionScopedDocument && !this.TryNavigateAwayFromActiveEditor())
        {
            return;
        }

        if (section is not null)
        {
            this.SetSessionSubPanelCollapsed(false);
        }

        this.currentSessionSubSection = section;
        this.SessionMemoriesAccentBar.Visibility = section == SessionSubSection.Memories ? Visibility.Visible : Visibility.Collapsed;
        this.SessionContextSkillsAccentBar.Visibility = section == SessionSubSection.ContextSkills ? Visibility.Visible : Visibility.Collapsed;
        this.SessionOutputStylesAccentBar.Visibility = section == SessionSubSection.OutputStyles ? Visibility.Visible : Visibility.Collapsed;
        this.MemoriesSectionView.Visibility = section == SessionSubSection.Memories ? Visibility.Visible : Visibility.Collapsed;
        this.ContextSkillsSectionView.Visibility = section == SessionSubSection.ContextSkills ? Visibility.Visible : Visibility.Collapsed;
        this.OutputStylesSectionView.Visibility = section == SessionSubSection.OutputStyles ? Visibility.Visible : Visibility.Collapsed;

        if (section is null)
        {
            this.ClearSessionSubSectionSelections();

            if (this.IsShowingSessionScopedDocument)
            {
                this.HideActiveDocumentEditors();
                this.activeMarkdownEditorSource = MarkdownEditorSource.None;
                this.RefreshSessionsCanvas();
            }

            return;
        }

        if (this.TabStrip.SelectedItem is SessionNodeViewModel session)
        {
            this.EnsureSessionSubSectionLoaded(section.Value, session.WorkingDirectory);
        }
    }

    private void EnsureSessionSubSectionLoaded(SessionSubSection section, string workingDirectory)
    {
        var alreadyBuiltForThisSession = section switch
        {
            SessionSubSection.Memories =>
                this.sessionMemoryNodes is not null && string.Equals(this.sessionMemoriesScopedWorkingDirectory, workingDirectory, StringComparison.OrdinalIgnoreCase),
            SessionSubSection.ContextSkills =>
                this.sessionContextRoot is not null && string.Equals(this.sessionContextSkillsScopedWorkingDirectory, workingDirectory, StringComparison.OrdinalIgnoreCase),
            _ =>
                this.sessionOutputStyleNodes is not null && string.Equals(this.sessionOutputStylesScopedWorkingDirectory, workingDirectory, StringComparison.OrdinalIgnoreCase),
        };

        if (alreadyBuiltForThisSession)
        {
            return;
        }

        switch (section)
        {
            case SessionSubSection.Memories:
                this.sessionMemoriesScopedWorkingDirectory = workingDirectory;
                this.RebuildSessionMemories(workingDirectory);
                break;
            case SessionSubSection.ContextSkills:
                this.sessionContextSkillsScopedWorkingDirectory = workingDirectory;
                this.RebuildSessionContextSkills(workingDirectory);
                break;
            default:
                this.sessionOutputStylesScopedWorkingDirectory = workingDirectory;
                this.RebuildSessionOutputStyles(workingDirectory);
                break;
        }

        // A cwd change while this section was already open would otherwise leave the *previous*
        // session's file still selected and rendered - drop it so the shared MarkdownEditor/
        // terminal reflects the session actually in the foreground now. Harmless no-op the first
        // time a section is ever opened, since there's nothing yet to clear.
        this.ClearSessionSubSectionSelections();
        if (this.IsShowingSessionScopedDocument)
        {
            this.HideActiveDocumentEditors();
            this.activeMarkdownEditorSource = MarkdownEditorSource.None;
            this.RefreshSessionsCanvas();
        }
    }

    private void RebuildSessionMemories(string workingDirectory)
    {
        var files = MemoryFileListBuilder.Build(SessionScopedPaths.GetMemoryDirectory(workingDirectory));
        this.sessionMemoryNodes = new ObservableCollection<MemoryFileNodeViewModel>(files);
        this.MemoriesList.ItemsSource = this.sessionMemoryNodes;
        this.MemoriesList.Visibility = this.sessionMemoryNodes.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        this.MemoriesEmptyText.Visibility = this.sessionMemoryNodes.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RebuildSessionContextSkills(string workingDirectory)
    {
        if (!SessionScopedPaths.TryGetRepoRoot(workingDirectory, out var repoRoot))
        {
            // Not a repo at all - showing a permanently-missing CLAUDE.md would misleadingly imply
            // this could still work here, so the tree/skills pair is hidden entirely rather than
            // resolved against workingDirectory itself.
            this.sessionContextRoot = null;
            this.sessionSkillNodes = null;
            this.SessionContextPanel.Visibility = Visibility.Collapsed;
            this.SessionContextSkillsEmptyText.Visibility = Visibility.Visible;
            return;
        }

        this.sessionContextRoot = ContextTreeBuilder.BuildRoot(Path.Combine(repoRoot, "CLAUDE.md"));
        this.SessionContextTree.ItemsSource = new[] { this.sessionContextRoot };

        var skills = new SkillDiscoveryService(Path.Combine(repoRoot, ".claude", "skills")).ScanPersonalSkills();
        this.sessionSkillNodes = new ObservableCollection<SkillNodeViewModel>(skills.Select(s => new SkillNodeViewModel(s)));
        this.SessionSkillsList.ItemsSource = this.sessionSkillNodes;

        this.SessionContextPanel.Visibility = Visibility.Visible;
        this.SessionContextSkillsEmptyText.Visibility = Visibility.Collapsed;
    }

    private void RebuildSessionOutputStyles(string workingDirectory)
    {
        if (!SessionScopedPaths.TryGetRepoRoot(workingDirectory, out var repoRoot))
        {
            // Mirrors RebuildSessionContextSkills's own not-a-repo handling - the list stays
            // hidden entirely rather than resolved against workingDirectory itself.
            this.sessionOutputStyleNodes = null;
            this.SessionOutputStylesList.Visibility = Visibility.Collapsed;
            this.SessionOutputStylesEmptyText.Visibility = Visibility.Visible;
            return;
        }

        var outputStyles = new OutputStyleDiscoveryService(Path.Combine(repoRoot, ".claude", "output-styles")).ScanPersonalOutputStyles();
        this.sessionOutputStyleNodes = new ObservableCollection<OutputStyleNodeViewModel>(outputStyles.Select(s => new OutputStyleNodeViewModel(s)));
        this.SessionOutputStylesList.ItemsSource = this.sessionOutputStyleNodes;
        this.SessionOutputStylesList.Visibility = this.sessionOutputStyleNodes.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        this.SessionOutputStylesEmptyText.Visibility = this.sessionOutputStyleNodes.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    // Rebuilds just the session-scoped CLAUDE.md tree after saving an edit through MarkdownEditor -
    // mirrors RebuildContextTree. Skills are deliberately left alone, matching their own
    // rescan-on-next-launch-only convention (see EnsureSkillsLoaded).
    private void RebuildSessionContextTreeOnly()
    {
        if (this.sessionContextSkillsScopedWorkingDirectory is not { } workingDirectory
            || !SessionScopedPaths.TryGetRepoRoot(workingDirectory, out var repoRoot))
        {
            return;
        }

        var expandedPaths = this.sessionContextRoot is null
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : CollectExpandedPaths(this.sessionContextRoot);

        this.sessionContextRoot = ContextTreeBuilder.BuildRoot(Path.Combine(repoRoot, "CLAUDE.md"));
        ReapplyExpansion(this.sessionContextRoot, expandedPaths);
        this.SessionContextTree.ItemsSource = new[] { this.sessionContextRoot };
    }

    private void ClearSessionSubSectionSelections()
    {
        this.suppressMemoriesSelectionSideEffects = true;
        this.MemoriesList.SelectedItem = null;
        this.suppressMemoriesSelectionSideEffects = false;

        this.suppressSessionSkillsSelectionSideEffects = true;
        this.SessionSkillsList.SelectedItem = null;
        this.suppressSessionSkillsSelectionSideEffects = false;

        if (this.SessionContextTree.SelectedItem is ContextFileNodeViewModel node)
        {
            this.suppressSessionContextPanelSelectionSync = true;
            node.IsSelected = false;
            this.suppressSessionContextPanelSelectionSync = false;
        }

        this.suppressSessionOutputStylesSelectionSideEffects = true;
        this.SessionOutputStylesList.SelectedItem = null;
        this.suppressSessionOutputStylesSelectionSideEffects = false;
    }

    private void OnSessionMemoriesIconClick(object sender, MouseButtonEventArgs e)
    {
        this.SetSessionSubSection(SessionSubSection.Memories);
    }

    private void OnSessionContextSkillsIconClick(object sender, MouseButtonEventArgs e)
    {
        this.SetSessionSubSection(SessionSubSection.ContextSkills);
    }

    private void OnSessionOutputStylesIconClick(object sender, MouseButtonEventArgs e)
    {
        this.SetSessionSubSection(SessionSubSection.OutputStyles);
    }

    private void OnCollapseSessionSubPanelClick(object sender, RoutedEventArgs e)
    {
        this.SetSessionSubPanelCollapsed(true);
    }

    private void OnMemoriesListSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (this.suppressMemoriesSelectionSideEffects)
        {
            return;
        }

        if (e.RemovedItems.Count > 0 && !this.TryNavigateAwayFromActiveEditor())
        {
            this.suppressMemoriesSelectionSideEffects = true;
            this.MemoriesList.SelectedItem = e.RemovedItems[0];
            this.suppressMemoriesSelectionSideEffects = false;
            return;
        }

        this.RefreshSessionSubPanelCanvas();
    }

    private void OnSessionContextTreeSelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (this.suppressSessionContextPanelSelectionSync)
        {
            return;
        }

        if (e.OldValue is ContextFileNodeViewModel oldNode && !this.TryNavigateAwayFromActiveEditor())
        {
            this.suppressSessionContextPanelSelectionSync = true;
            oldNode.IsSelected = true;
            this.suppressSessionContextPanelSelectionSync = false;
            return;
        }

        // Clear SessionSkillsList's selection so only one of the two stacked controls is ever
        // visually "active" - guarded so this side effect doesn't recursively re-enter
        // OnSessionSkillsListSelectionChanged (see that handler's own top-of-method guard).
        if (e.NewValue is ContextFileNodeViewModel && this.SessionSkillsList.SelectedItem is not null)
        {
            this.suppressSessionContextPanelSelectionSync = true;
            this.SessionSkillsList.SelectedItem = null;
            this.suppressSessionContextPanelSelectionSync = false;
        }

        this.RefreshSessionSubPanelCanvas();
    }

    private void OnSessionSkillsListSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (this.suppressSessionSkillsSelectionSideEffects || this.suppressSessionContextPanelSelectionSync)
        {
            return;
        }

        if (e.RemovedItems.Count > 0 && !this.TryNavigateAwayFromActiveEditor())
        {
            this.suppressSessionSkillsSelectionSideEffects = true;
            this.SessionSkillsList.SelectedItem = e.RemovedItems[0];
            this.suppressSessionSkillsSelectionSideEffects = false;
            return;
        }

        // Clear SessionContextTree's selection so only one of the two stacked controls is ever
        // visually "active" - guarded so this side effect doesn't recursively re-enter
        // OnSessionContextTreeSelectedItemChanged (see that handler's own top-of-method guard).
        if (this.SessionSkillsList.SelectedItem is not null && this.SessionContextTree.SelectedItem is ContextFileNodeViewModel contextNode)
        {
            this.suppressSessionContextPanelSelectionSync = true;
            contextNode.IsSelected = false;
            this.suppressSessionContextPanelSelectionSync = false;
        }

        this.RefreshSessionSubPanelCanvas();
    }

    private void OnSessionOutputStylesListSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (this.suppressSessionOutputStylesSelectionSideEffects)
        {
            return;
        }

        if (e.RemovedItems.Count > 0 && !this.TryNavigateAwayFromActiveEditor())
        {
            this.suppressSessionOutputStylesSelectionSideEffects = true;
            this.SessionOutputStylesList.SelectedItem = e.RemovedItems[0];
            this.suppressSessionOutputStylesSelectionSideEffects = false;
            return;
        }

        this.RefreshSessionSubPanelCanvas();
    }

    private void RefreshSessionSubPanelCanvas()
    {
        if (this.MemoriesList.SelectedItem is MemoryFileNodeViewModel memoryNode)
        {
            this.HideActiveSessionPane();
            this.SkillEditor.Visibility = Visibility.Collapsed;
            this.OutputStyleEditor.Visibility = Visibility.Collapsed;
            this.MarkdownEditor.Visibility = Visibility.Visible;
            this.activeMarkdownEditorSource = MarkdownEditorSource.SessionMemory;
            this.MarkdownEditor.LoadDocument(new MarkdownEditorTarget(memoryNode.FilePath, memoryNode.Name));
        }
        else if (this.SessionContextTree.SelectedItem is ContextFileNodeViewModel contextNode)
        {
            this.HideActiveSessionPane();
            this.SkillEditor.Visibility = Visibility.Collapsed;
            this.OutputStyleEditor.Visibility = Visibility.Collapsed;
            this.MarkdownEditor.Visibility = Visibility.Visible;
            this.activeMarkdownEditorSource = MarkdownEditorSource.SessionContext;
            this.MarkdownEditor.LoadDocument(new MarkdownEditorTarget(contextNode.ResolvedPath, contextNode.Name));
        }
        else if (this.SessionSkillsList.SelectedItem is SkillNodeViewModel skillNode)
        {
            this.HideActiveSessionPane();
            this.MarkdownEditor.Visibility = Visibility.Collapsed;
            this.OutputStyleEditor.Visibility = Visibility.Collapsed;
            this.SkillEditor.Visibility = Visibility.Visible;
            this.activeMarkdownEditorSource = MarkdownEditorSource.SessionSkill;
            this.SkillEditor.LoadDocument(skillNode.Info.FilePath);
        }
        else if (this.SessionOutputStylesList.SelectedItem is OutputStyleNodeViewModel outputStyleNode)
        {
            this.HideActiveSessionPane();
            this.SkillEditor.Visibility = Visibility.Collapsed;
            this.MarkdownEditor.Visibility = Visibility.Collapsed;
            this.OutputStyleEditor.Visibility = Visibility.Visible;
            this.activeMarkdownEditorSource = MarkdownEditorSource.SessionOutputStyle;
            this.OutputStyleEditor.LoadDocument(outputStyleNode.Info.FilePath);
        }
        else
        {
            this.HideActiveDocumentEditors();
            this.activeMarkdownEditorSource = MarkdownEditorSource.None;
            this.RefreshSessionsCanvas();
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
            this.SkillsContextMenu.Items.Add(CreateMenuItem("Explore to", () => WindowsExplorer.OpenAndSelect(skill.Info.FilePath)));
        }
    }

    // "Add new" on SkillsGroupHeader (personal skills) - always valid, unlike the project-level
    // equivalent below, since ~/.claude/skills always resolves regardless of session state.
    private void OnAddNewPersonalSkillClick(object sender, RoutedEventArgs e)
    {
        if (!this.TryNavigateAwayFromActiveEditor())
        {
            return;
        }

        // Sentinel that isn't any of ContextTree/SkillsList/OutputStylesList, so every branch of
        // ClearOtherContextSelections runs - a blank new skill shouldn't leave a stale selection
        // highlighted in any of the three stacked controls.
        this.ClearOtherContextSelections(this);

        this.PlaceholderText.Visibility = Visibility.Collapsed;
        this.ErrorText.Visibility = Visibility.Collapsed;
        this.MarkdownEditor.Visibility = Visibility.Collapsed;
        this.SkillEditor.Visibility = Visibility.Visible;
        this.activeMarkdownEditorSource = MarkdownEditorSource.TopLevelSkill;
        this.SkillEditor.LoadNewDocument(ClaudeSkillsDirectory.Root);
    }

    // ContextMenuOpening (not a static XAML menu, unlike the personal-skills header above) since
    // whether "Add new" is even offered depends on the current session's cwd resolving to a repo.
    private void OnSessionSkillsGroupHeaderContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        this.SessionSkillsGroupHeaderContextMenu.Items.Clear();

        if (this.TabStrip.SelectedItem is SessionNodeViewModel session && SessionScopedPaths.TryGetRepoRoot(session.WorkingDirectory, out var repoRoot))
        {
            this.SessionSkillsGroupHeaderContextMenu.Items.Add(CreateMenuItem("Add new", () => this.OnAddNewProjectSkill(repoRoot)));
        }
        else
        {
            this.SessionSkillsGroupHeaderContextMenu.Items.Add(CreateMenuItem("No repository found", () => { }, enabled: false));
        }
    }

    private void OnAddNewProjectSkill(string repoRoot)
    {
        if (!this.TryNavigateAwayFromActiveEditor())
        {
            return;
        }

        this.ClearSessionSubSectionSelections();
        this.HideActiveSessionPane();
        this.MarkdownEditor.Visibility = Visibility.Collapsed;
        this.SkillEditor.Visibility = Visibility.Visible;
        this.activeMarkdownEditorSource = MarkdownEditorSource.SessionSkill;
        this.SkillEditor.LoadNewDocument(Path.Combine(repoRoot, ".claude", "skills"));
    }

    private void OnOutputStylesListMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        var item = FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject);

        if (item is not null && !item.IsSelected)
        {
            item.IsSelected = true;
        }
    }

    private void OnOutputStylesListContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        this.OutputStylesContextMenu.Items.Clear();

        // Mirrors OnSkillsListContextMenuOpening's own remark - selection is forced above in
        // OnOutputStylesListMouseRightButtonDown before this fires.
        if (this.OutputStylesList.SelectedItem is OutputStyleNodeViewModel outputStyle)
        {
            this.OutputStylesContextMenu.Items.Add(CreateMenuItem("Explore to", () => WindowsExplorer.OpenAndSelect(outputStyle.Info.FilePath)));
        }
    }

    // "Add new" on OutputStylesGroupHeader (personal output styles) - always valid, unlike the
    // project-level equivalent below, since ~/.claude/output-styles always resolves regardless of
    // session state. Mirrors OnAddNewPersonalSkillClick.
    private void OnAddNewPersonalOutputStyleClick(object sender, RoutedEventArgs e)
    {
        if (!this.TryNavigateAwayFromActiveEditor())
        {
            return;
        }

        // Sentinel that isn't any of ContextTree/SkillsList/OutputStylesList, so every branch of
        // ClearOtherContextSelections runs - a blank new output style shouldn't leave a stale
        // selection highlighted in any of the three stacked controls.
        this.ClearOtherContextSelections(this);

        this.PlaceholderText.Visibility = Visibility.Collapsed;
        this.ErrorText.Visibility = Visibility.Collapsed;
        this.MarkdownEditor.Visibility = Visibility.Collapsed;
        this.SkillEditor.Visibility = Visibility.Collapsed;
        this.OutputStyleEditor.Visibility = Visibility.Visible;
        this.activeMarkdownEditorSource = MarkdownEditorSource.TopLevelOutputStyle;
        this.OutputStyleEditor.LoadNewDocument(ClaudeOutputStylesDirectory.Root);
    }

    // ContextMenuOpening (not a static XAML menu, unlike the personal-output-styles header above)
    // since whether "Add new" is even offered depends on the current session's cwd resolving to a
    // repo. Mirrors OnSessionSkillsGroupHeaderContextMenuOpening.
    private void OnSessionOutputStylesGroupHeaderContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        this.SessionOutputStylesGroupHeaderContextMenu.Items.Clear();

        if (this.TabStrip.SelectedItem is SessionNodeViewModel session && SessionScopedPaths.TryGetRepoRoot(session.WorkingDirectory, out var repoRoot))
        {
            this.SessionOutputStylesGroupHeaderContextMenu.Items.Add(CreateMenuItem("Add new", () => this.OnAddNewProjectOutputStyle(repoRoot)));
        }
        else
        {
            this.SessionOutputStylesGroupHeaderContextMenu.Items.Add(CreateMenuItem("No repository found", () => { }, enabled: false));
        }
    }

    private void OnAddNewProjectOutputStyle(string repoRoot)
    {
        if (!this.TryNavigateAwayFromActiveEditor())
        {
            return;
        }

        this.ClearSessionSubSectionSelections();
        this.HideActiveSessionPane();
        this.MarkdownEditor.Visibility = Visibility.Collapsed;
        this.SkillEditor.Visibility = Visibility.Collapsed;
        this.OutputStyleEditor.Visibility = Visibility.Visible;
        this.activeMarkdownEditorSource = MarkdownEditorSource.SessionOutputStyle;
        this.OutputStyleEditor.LoadNewDocument(Path.Combine(repoRoot, ".claude", "output-styles"));
    }

    private void OnContextTreeMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        var item = FindAncestor<TreeViewItem>(e.OriginalSource as DependencyObject);
        this.rightClickedContextNode = item?.DataContext as ContextFileNodeViewModel;
    }

    private void OnContextTreeContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        this.ContextTreeContextMenu.Items.Clear();

        if (this.rightClickedContextNode is { } node)
        {
            this.ContextTreeContextMenu.Items.Add(CreateMenuItem("Explore to", () => ExploreToContextNode(node)));
        }
    }

    private static void ExploreToContextNode(ContextFileNodeViewModel node)
    {
        if (node.IsMissing)
        {
            WindowsExplorer.OpenNearestExistingAncestorFolder(node.ResolvedPath);
        }
        else
        {
            WindowsExplorer.OpenAndSelect(node.ResolvedPath);
        }
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

    // Window-level (not OnTreeKeyDown) since undo/redo act on whatever was last done, not on the
    // Tree's current selection - unlike F2/Shift+Delete, they shouldn't require a selected node or
    // the Tree having WPF focus. UndoRedoShortcutHook is the path that matters once a terminal
    // session has real Win32 focus - see its remarks.
    private void OnWindowPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.Control)
        {
            return;
        }

        if (e.Key == Key.Z)
        {
            this.OnUndoShortcut();
            e.Handled = true;
        }
        else if (e.Key == Key.Y)
        {
            this.OnRedoShortcut();
            e.Handled = true;
        }
    }

    private void OnUndoShortcut()
    {
        this.undoManager.TryUndo();
    }

    private void OnRedoShortcut()
    {
        this.undoManager.TryRedo();
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

    // A click that lands on Tree's empty area (below the last row, or in the gutter beside one)
    // hits no TreeViewItem, so neither OnItemPreviewMouseLeftButtonDown nor a selection change
    // fires - Tree still takes default WPF focus for itself, though, same as any other focusable
    // control clicked anywhere within its bounds. Most jarring when this click is also what
    // reactivates the whole window after switching back from another app: the terminal that had
    // focus before switching away loses it to Tree instead of keeping it. Deferred to ContextIdle
    // for the same reason as ActivateTab's own Pane.Focus() call above - Tree hasn't actually taken
    // focus yet while this (tunneling) handler is running, so focusing the pane now would just lose
    // to Tree's own focus-on-click behavior once this dispatch unwinds.
    private void OnTreePreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (FindAncestor<TreeViewItem>(e.OriginalSource as DependencyObject) is not null)
        {
            return;
        }

        if (this.TabStrip.SelectedItem is SessionNodeViewModel { LiveSession: { } session })
        {
            this.Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() => session.Host.Pane.Focus()));
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
        var oldSiblings = oldParent?.Children ?? this.controller.RootNodes;
        var oldIndex = oldSiblings.IndexOf(dragged);

        this.controller.MoveTo(dragged, newParent, index);
        this.controller.RemoveUncategorizedIfEmpty(oldParent);
        this.controller.ScheduleSave();

        this.undoManager.Push(
            undo: () =>
            {
                this.EnsureUncategorizedPresentIfNeeded(oldParent);
                this.controller.MoveTo(dragged, oldParent, oldIndex);
                this.controller.RemoveUncategorizedIfEmpty(newParent);
                this.controller.ScheduleSave();
            },
            redo: () =>
            {
                this.EnsureUncategorizedPresentIfNeeded(newParent);
                this.controller.MoveTo(dragged, newParent, index);
                this.controller.RemoveUncategorizedIfEmpty(oldParent);
                this.controller.ScheduleSave();
            });
    }

    // Undoing/redoing a move that puts a node back under Uncategorized needs that pseudo-project
    // to exist first - it may have been auto-removed by RemoveUncategorizedIfEmpty the moment the
    // move/delete being reversed emptied it. See SessionTreeController.EnsureUncategorizedPresent.
    private void EnsureUncategorizedPresentIfNeeded(TreeNodeViewModel? parent)
    {
        if (parent is ProjectNodeViewModel { IsUncategorized: true } uncategorized)
        {
            this.controller.EnsureUncategorizedPresent(uncategorized);
        }
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
                this.TreeContextMenu.Items.Add(CreateMenuItem("Explore to", () => WindowsExplorer.OpenFolder(session.WorkingDirectory)));
                this.TreeContextMenu.Items.Add(CreateMenuItem("Rename", () => this.OnRename(session), inputGestureText: "F2"));

                // No enabled: guard on either View Session* item - both must work even before the
                // session has ever been launched (the window opens in a "waiting for session to
                // start..." state and switches to live view once the transcript file appears).
                this.TreeContextMenu.Items.Add(CreateMenuItem("View Session Log", () => this.sessionLogWindows.ShowOrFocus(session, this, this.costMonitor)));
                this.TreeContextMenu.Items.Add(CreateMenuItem("View Session Costs", () => this.sessionLogWindows.ShowOrFocus(session, this, this.costMonitor, showCosts: true)));
                this.TreeContextMenu.Items.Add(CreateMenuItem($"Copy Session Id {session.ClaudeSessionId}", () => Clipboard.SetText($"{session.ClaudeSessionId}")));
                this.TreeContextMenu.Items.Add(CreateEnvironmentVariablesMenuItem(session));

                // Plain enable/disable (unlike Delete's error-dialog approach) - "Stop while
                // dormant" needs no explanation, it's just not a currently valid action. Also
                // closes the tab (like OnCloseTabClick/OnDelete) - otherwise a dormant tab lingers
                // in the strip and gets persisted/restored inert on next launch.
                this.TreeContextMenu.Items.Add(CreateMenuItem("Stop", () =>
                {
                    this.CloseTab(session);
                    this.StopSession(session);
                }, enabled: session.IsRunning));
                this.TreeContextMenu.Items.Add(CreateMenuItem("Delete", () => this.OnDelete(session), inputGestureText: "Shift+Del"));
                break;
        }
    }

    // Wraps ShowDialog() so UndoRedoShortcutHook's global Ctrl+Z/Ctrl+Y knows to let the keystroke
    // fall through to whichever TextBox owns real focus inside the dialog (e.g. text-undo while
    // typing a name), rather than firing the tree's own undo/redo. Every ShowDialog() call in this
    // window goes through here.
    private bool? ShowModal(Window dialog)
    {
        this.isModalDialogOpen = true;
        try
        {
            return dialog.ShowDialog();
        }
        finally
        {
            this.isModalDialogOpen = false;
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

        if (this.ShowModal(dialog) == true)
        {
            this.controller.AddProject(dialog.NewName);
        }
    }

    /// <summary>
    /// A session added under an existing session defaults the form's folder field to that
    /// session's own working directory (the old "Same Folder" shortcut's role, just editable now
    /// instead of a separate menu item) - one under a Project defaults to the configured
    /// DefaultRootFolder, falling back to the user's profile folder if none is set.
    /// </summary>
    private void OnAddSession(TreeNodeViewModel parent)
    {
        var defaultFolder = parent is SessionNodeViewModel parentSession
            ? parentSession.WorkingDirectory
            : this.appSettings.DefaultRootFolder is { Length: > 0 } configured
                ? configured
                : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        var dialog = new AddSessionDialog(defaultFolder, this.appSettings.UseWorktreeByDefault) { Owner = this };
        if (this.ShowModal(dialog) != true)
        {
            return;
        }

        var session = this.controller.AddSession(parent, dialog.SessionName, dialog.WorkingDirectory);
        parent.IsExpanded = true;
        session.IsSelected = true;
    }

    private void OnImportSession(TreeNodeViewModel parent)
    {
        var dialog = new ImportSessionWindow { Owner = this };
        if (this.ShowModal(dialog) != true || dialog.SelectedResult is not { } result)
        {
            return;
        }

        var imported = this.controller.ImportSession(parent, result.SummaryOrSessionId, result.WorkingDirectory, result.ParentSessionId);
        this.costMonitor.RegisterSession(imported);
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

        var oldName = node.Name;
        if (this.ShowModal(dialog) == true)
        {
            var newName = dialog.NewName;
            this.controller.Rename(node, newName);

            this.undoManager.Push(
                undo: () => this.controller.Rename(node, oldName),
                redo: () => this.controller.Rename(node, newName));
        }
    }

    private void OnDelete(TreeNodeViewModel node)
    {
        var oldParent = node.Parent;
        var oldSiblings = oldParent?.Children ?? this.controller.RootNodes;
        var oldIndex = oldSiblings.IndexOf(node);

        if (node is SessionNodeViewModel session)
        {
            this.CloseTab(session);

            if (session.IsRunning)
            {
                this.StopSession(session);
            }
        }

        if (!this.controller.TryDelete(node, out var error))
        {
            MessageBox.Show(this, error, "Cannot delete", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (node is SessionNodeViewModel deletedSession)
        {
            this.costMonitor.UnregisterSession(deletedSession);
        }

        this.undoManager.Push(
            undo: () =>
            {
                this.EnsureUncategorizedPresentIfNeeded(oldParent);

                var siblings = oldParent?.Children ?? this.controller.RootNodes;
                siblings.Insert(Math.Clamp(oldIndex, 0, siblings.Count), node);
                node.Parent = oldParent;

                if (node is SessionNodeViewModel restoredSession)
                {
                    this.controller.RevalidateBeforeLaunch(restoredSession);
                    this.costMonitor.RegisterSession(restoredSession);
                }

                this.controller.ScheduleSave();
            },
            redo: () =>
            {
                if (node is SessionNodeViewModel sessionAgain)
                {
                    this.CloseTab(sessionAgain);

                    if (sessionAgain.IsRunning)
                    {
                        this.StopSession(sessionAgain);
                    }
                }

                this.controller.TryDelete(node, out _);

                if (node is SessionNodeViewModel deletedSessionAgain)
                {
                    this.costMonitor.UnregisterSession(deletedSessionAgain);
                }
            });
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        // The app has no explicit ShutdownMode, so it defaults to OnLastWindowClose - a session
        // log window left open would otherwise keep the process alive after this window closes.
        this.sessionLogWindows.CloseAll();
        this.deeplinkWindows.CloseAll();
        this.costMonitor.Dispose();

        this.shiftDeleteHook.Dispose();
        this.terminalKeyRoutingHook.Dispose();
        this.undoRedoHook.Dispose();

        if (Application.Current is App app)
        {
            app.ActivationRequests.Detach();
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

        // Deliberately not pruning "unused" sessions here. Host.Dispose() force-kills the claude
        // process tree (ConPtyConnection.Dispose -> liveProcess.Kill(entireProcessTree: true))
        // rather than letting it exit gracefully, so claude never gets a chance to do whatever
        // exit-time persistence an interactive session relies on - a transcript-file check right
        // after that kill can't reliably tell "never used" apart from "used, but killed before it
        // could flush" (confirmed: real, actively-used sessions were losing their transcript and
        // getting silently deleted here). Better to keep an occasional empty placeholder node than
        // to risk deleting a real conversation.
        this.controller.Flush();

        // RestoreBounds (rather than Left/Top/Width/Height directly) whenever the window isn't
        // Normal - those four properties reflect the maximized/minimized bounds while in that
        // state, which would otherwise overwrite the saved windowed-mode size/position with the
        // maximized (effectively full-screen) ones.
        var bounds = this.WindowState == WindowState.Normal
            ? new Rect(this.Left, this.Top, this.Width, this.Height)
            : this.RestoreBounds;

        // Only persist tabs whose session survived the drop-if-abandoned pass above - an id that
        // no longer exists next launch would just be silently skipped by RestoreOpenTabs anyway,
        // but filtering here keeps window-layout.json honest about what's actually still around.
        var survivingSessionIds = this.controller.AllSessionNodes().Select(n => n.Id).ToHashSet();
        var openTabIds = this.openTabs.Select(s => s.Id).Where(survivingSessionIds.Contains).ToList();
        var runningTabIds = runningSessions.Select(n => n.Id).Where(survivingSessionIds.Contains).ToList();
        var activeTabId = this.TabStrip.SelectedItem is SessionNodeViewModel { } activeSession && survivingSessionIds.Contains(activeSession.Id)
            ? activeSession.Id
            : (Guid?)null;

        this.layoutStore.Save(new WindowLayout
        {
            Left = bounds.Left,
            Top = bounds.Top,
            Width = bounds.Width,
            Height = bounds.Height,
            IsMaximized = this.WindowState == WindowState.Maximized,
            TreeColumnWidth = this.TreeColumn.Width.Value,
            // Row.Height only reflects the true expanded value while actually expanded - while
            // collapsed, the row is sized to the fixed collapsed constant instead, so the
            // remembered pre-collapse value is what's persisted in that case.
            SessionSubPanelHeight = this.isSessionSubPanelCollapsed ? this.sessionSubPanelExpandedHeight : this.SessionSubPanelRow.Height.Value,
            OpenSessionTabIds = openTabIds,
            RunningSessionTabIds = runningTabIds,
            ActiveSessionTabId = activeTabId,
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
    /// Reopens whatever tabs were open when the window last closed, relaunching (in the background)
    /// only the ones that were actually running rather than stopped - see OnClosing. Called from
    /// OnLoaded, same HWND-readiness constraint as HandleFromHereSession below: starting a session
    /// before SessionViewHost's terminal control has a real HWND silently never attaches a client
    /// process to it.
    /// </summary>
    private void RestoreOpenTabs(WindowLayout? layout)
    {
        if (layout is null || layout.OpenSessionTabIds.Count == 0)
        {
            return;
        }

        var sessionsById = this.controller.AllSessionNodes().ToDictionary(n => n.Id);
        var runningTabIds = layout.RunningSessionTabIds.ToHashSet();

        foreach (var id in layout.OpenSessionTabIds)
        {
            if (!sessionsById.TryGetValue(id, out var session) || this.openTabs.Contains(session))
            {
                continue;
            }

            this.controller.RevalidateBeforeLaunch(session);
            if (session.IsInvalid)
            {
                // Drop a tab whose session no longer validates (e.g. its working directory is
                // gone) rather than restoring it just to immediately show an error nobody asked to
                // see yet.
                continue;
            }

            // Only relaunch the claude process for tabs that were actually running (not stopped)
            // when the window last closed - a stopped tab reopens inert, matching how it was left.
            if (runningTabIds.Contains(id))
            {
                this.LaunchSession(session);
            }

            this.openTabs.Add(session);
        }

        if (this.openTabs.Count == 0)
        {
            return;
        }

        var active = layout.ActiveSessionTabId is { } activeId
            ? this.openTabs.FirstOrDefault(s => s.Id == activeId) ?? this.openTabs[0]
            : this.openTabs[0];

        // Selecting the tree node runs the usual OnTreeSelectedItemChanged -> OpenTab path, which
        // makes this the active tab and shows its (already-launched, per the loop above) pane.
        ExpandAncestors(active);
        active.IsSelected = true;
    }

    private static void ExpandAncestors(TreeNodeViewModel node)
    {
        for (var parent = node.Parent; parent is not null; parent = parent.Parent)
        {
            parent.IsExpanded = true;
        }
    }

    /// <summary>
    /// Handles both the pipe-driven hand-off from an already-running instance and this same
    /// process's own startup arguments (--from-here or a multi-clod:// deeplink) - see
    /// App.ActivationRequests. Always runs on the UI thread. A null request (or a plain
    /// double-click's already-consumed args) is still "come to the foreground" for an
    /// already-running instance being handed off to.
    /// </summary>
    private void HandleActivationRequest(ActivationRequest? request)
    {
        if (this.WindowState == WindowState.Minimized)
        {
            this.WindowState = WindowState.Normal;
        }

        this.Show();
        this.Activate();

        switch (request)
        {
            case { Kind: ActivationRequestKind.FromHere } fromHere:
                this.HandleFromHereSession(fromHere.Payload);
                break;
            case { Kind: ActivationRequestKind.Deeplink } deeplink:
                this.HandleDeeplinkRequest(deeplink.Payload);
                break;
        }
    }

    private void HandleFromHereSession(string directory)
    {
        var uncategorized = this.controller.GetOrCreateUncategorized();
        var name = FolderDisplayName.GetName(directory);
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

    /// <summary>
    /// Fetches/extracts/classifies a deeplinked session zip and opens it in a read-only
    /// DeeplinkImportWindow - see Deeplink/DeeplinkImportCoordinator. Re-triggering a source that
    /// already has a window open just focuses it instead of re-running the whole pipeline.
    /// </summary>
    private async void HandleDeeplinkRequest(string source)
    {
        var key = DeeplinkSourceKey.Normalize(source);
        if (this.deeplinkWindows.TryFocus(key))
        {
            return;
        }

        var progressWindow = new DeeplinkProgressWindow(source) { Owner = this };
        progressWindow.Show();

        try
        {
            var importDirectory = DeeplinkImportStorage.GetImportDirectory(key);
            var contents = await DeeplinkImportCoordinator.ImportAsync(source, importDirectory, progressWindow, CancellationToken.None);

            progressWindow.Close();

            var window = new DeeplinkImportWindow(source, contents) { Owner = this };
            this.deeplinkWindows.Register(key, window);
            window.Show();
        }
        catch (DeeplinkImportException ex)
        {
            progressWindow.ShowError(source, ex.Message);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            progressWindow.ShowError(source, ex.Message);
        }
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

    // Reads whatever was captured on this session's last start/resume (SessionNodeViewModel.
    // CapturedEnvironmentVariables) - never re-resolves live, so the menu reflects what this
    // session actually launched with.
    private static MenuItem CreateEnvironmentVariablesMenuItem(SessionNodeViewModel session)
    {
        var variables = session.CapturedEnvironmentVariables;
        var envMenuItem = new MenuItem
        {
            Header = variables.Count > 0 ? "Environment Variables" : "Environment Variables (none)",
            IsEnabled = variables.Count > 0,
        };

        foreach (var variable in variables)
        {
            var displayValue = variable.IsSecretLike ? MaskSecretValue(variable.Value) : variable.Value;
            var child = new MenuItem
            {
                Header = $"{variable.Key}: {displayValue}",
                ToolTip = $"Source: {variable.Source.Describe()}",
            };

            // Always copies the full real value, even for secret-like vars whose display is
            // truncated above - the truncation is only shoulder-surfing/screenshot protection for
            // what's shown, not a restriction on what the menu is for.
            child.Click += (_, _) => Clipboard.SetText($"{variable.Key}: {variable.Value}");
            envMenuItem.Items.Add(child);
        }

        return envMenuItem;
    }

    private static string MaskSecretValue(string value) => value.Length <= 4 ? value : $"{value[..4]}…";

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
