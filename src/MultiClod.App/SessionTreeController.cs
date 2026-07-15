using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using MultiClod.App.Persistence;
using MultiClod.App.Validation;

namespace MultiClod.App;

/// <summary>
/// Pure orchestration over the Project/Session tree: mapping to/from SessionStore's DTOs,
/// mutation (add/rename/delete), project-name uniqueness, and the Uncategorized pseudo-project's
/// lifecycle. Knows nothing about the WPF visual tree (SessionViewHost/Pane.View) - MainWindow
/// owns that, since it's inherently tied to the live ISessionHost/Pane instances this class never
/// touches.
/// </summary>
public sealed class SessionTreeController
{
    private readonly SessionStore store;

    public SessionTreeController(SessionStore store)
    {
        this.store = store;
    }

    public ObservableCollection<TreeNodeViewModel> RootNodes { get; } = new();

    public void Load()
    {
        var file = this.store.Load();
        var recordsById = file.Sessions.ToDictionary(s => s.Id);

        this.RootNodes.Clear();
        foreach (var node in file.Hierarchy)
        {
            var viewModel = BuildViewModel(node, recordsById, parent: null);
            if (viewModel is not null)
            {
                this.RootNodes.Add(viewModel);
            }
        }

        foreach (var session in this.AllSessionNodes())
        {
            session.ValidationProblem = SessionValidator.Validate(session);
        }
    }

    /// <summary>
    /// Re-runs validation for a single node right before an actual launch attempt, in case its
    /// working directory or claude project data changed since the app started (or since it was
    /// last checked).
    /// </summary>
    public void RevalidateBeforeLaunch(SessionNodeViewModel node)
    {
        node.ValidationProblem = SessionValidator.Validate(node);
    }

    public void ScheduleSave()
    {
        this.store.ScheduleSave(this.BuildSnapshot());
    }

    public void Flush()
    {
        this.store.Flush();
    }

    public ProjectNodeViewModel AddProject(string name)
    {
        var project = new ProjectNodeViewModel(Guid.NewGuid(), name);
        this.RootNodes.Add(project);
        this.ScheduleSave();
        return project;
    }

    public SessionNodeViewModel AddSession(TreeNodeViewModel parent, string name, string workingDirectory)
    {
        var session = new SessionNodeViewModel(Guid.NewGuid(), Guid.NewGuid(), name, workingDirectory, hasBeenStarted: false)
        {
            Parent = parent,
        };
        parent.Children.Add(session);
        this.ScheduleSave();
        return session;
    }

    /// <summary>
    /// Adds a session for a Claude conversation that already exists on disk (found via
    /// ImportSessionWindow's search), rather than a brand-new one. Unlike <see cref="AddSession"/>,
    /// the caller supplies the real <paramref name="claudeSessionId"/> and this always sets
    /// <c>hasBeenStarted: true</c>, so the very next launch passes --resume instead of
    /// --session-id (see MainWindow.LaunchSession) and reattaches the existing conversation.
    /// </summary>
    public SessionNodeViewModel ImportSession(TreeNodeViewModel parent, string name, string workingDirectory, Guid claudeSessionId)
    {
        var session = new SessionNodeViewModel(Guid.NewGuid(), claudeSessionId, name, workingDirectory, hasBeenStarted: true)
        {
            Parent = parent,
        };
        parent.Children.Add(session);
        this.ScheduleSave();
        return session;
    }

    public void Rename(TreeNodeViewModel node, string newName)
    {
        node.Name = newName;
        this.ScheduleSave();
    }

    public bool TryDelete(TreeNodeViewModel node, out string? error)
    {
        if (node.Children.Count > 0)
        {
            var kind = node is ProjectNodeViewModel ? "project" : "session";
            error = $"Cannot delete this {kind} - it still contains {node.Children.Count} item(s). Remove them first.";
            return false;
        }

        var siblings = node.Parent?.Children ?? this.RootNodes;
        siblings.Remove(node);

        // Deleting a session may have just emptied Uncategorized.
        this.RemoveUncategorizedIfEmpty(node.Parent);

        this.ScheduleSave();
        error = null;
        return true;
    }

    public string? ValidateProjectName(string candidateName, ProjectNodeViewModel? excludingSelf)
    {
        var trimmed = candidateName.Trim();
        if (trimmed.Length == 0)
        {
            return "Project name cannot be empty.";
        }

        var reserved = string.Equals(trimmed, ProjectNodeViewModel.UncategorizedName, StringComparison.OrdinalIgnoreCase);
        var collides = this.RootNodes.OfType<ProjectNodeViewModel>()
            .Any(p => !ReferenceEquals(p, excludingSelf) && string.Equals(p.Name, trimmed, StringComparison.OrdinalIgnoreCase));

        return reserved || collides ? $"A project named \"{trimmed}\" already exists." : null;
    }

    /// <summary>
    /// Not called from any Phase 01 UI path yet - Phase 01's context menu never offers "add a
    /// session at the tree root", so the only real trigger is Phase 02's drag-drop onto the empty
    /// area below the tree. Implemented now (and exercised defensively by <see cref="TryDelete"/>)
    /// so it's correct and self-healing from day one rather than bolted on later.
    /// </summary>
    public ProjectNodeViewModel GetOrCreateUncategorized()
    {
        var existing = this.RootNodes.OfType<ProjectNodeViewModel>().FirstOrDefault(p => p.IsUncategorized);
        if (existing is not null)
        {
            return existing;
        }

        var uncategorized = new ProjectNodeViewModel(Guid.NewGuid(), ProjectNodeViewModel.UncategorizedName);
        this.RootNodes.Insert(0, uncategorized); // always sorts first when present
        return uncategorized;
    }

    public void RemoveUncategorizedIfEmpty(TreeNodeViewModel? node)
    {
        if (node is ProjectNodeViewModel { IsUncategorized: true, Children.Count: 0 })
        {
            this.RootNodes.Remove(node);
        }
    }

    /// <summary>
    /// Removes <paramref name="node"/> from wherever it currently sits and inserts it as a child
    /// of <paramref name="newParent"/> (or at the tree's root if null) at <paramref name="index"/>,
    /// clamped to the resulting collection's bounds. Used for both reparenting and same-parent
    /// reordering (drag-drop's own caller resolves the target index before removal, since removing
    /// first shifts indices within the same collection). Callers are responsible for checking
    /// <see cref="WouldCreateCycle"/> first - this method does not defend against producing one.
    /// </summary>
    public void MoveTo(TreeNodeViewModel node, TreeNodeViewModel? newParent, int index)
    {
        var oldSiblings = node.Parent?.Children ?? this.RootNodes;
        oldSiblings.Remove(node);

        var newSiblings = newParent?.Children ?? this.RootNodes;
        newSiblings.Insert(Math.Clamp(index, 0, newSiblings.Count), node);
        node.Parent = newParent;
    }

    /// <summary>
    /// True if moving <paramref name="dragged"/> to become a descendant of <paramref name="dropTarget"/>
    /// would create a cycle - i.e. dropTarget is dragged itself, or already sits somewhere beneath
    /// it. Walks up from dropTarget through Parent pointers, which also naturally covers "drop onto
    /// self" (found on the very first step).
    /// </summary>
    public static bool WouldCreateCycle(TreeNodeViewModel dragged, TreeNodeViewModel dropTarget)
    {
        for (var current = dropTarget; current is not null; current = current.Parent)
        {
            if (ReferenceEquals(current, dragged))
            {
                return true;
            }
        }

        return false;
    }

    public IEnumerable<SessionNodeViewModel> AllSessionNodes()
    {
        return this.RootNodes.SelectMany(Descendants).OfType<SessionNodeViewModel>();
    }

    private static IEnumerable<TreeNodeViewModel> Descendants(TreeNodeViewModel node)
    {
        yield return node;
        foreach (var child in node.Children)
        {
            foreach (var descendant in Descendants(child))
            {
                yield return descendant;
            }
        }
    }

    private static TreeNodeViewModel? BuildViewModel(HierarchyNode node, Dictionary<Guid, SessionRecord> recordsById, TreeNodeViewModel? parent)
    {
        TreeNodeViewModel? viewModel = node switch
        {
            ProjectHierarchyNode project => new ProjectNodeViewModel(project.Id, project.Name),
            SessionHierarchyNode session when recordsById.TryGetValue(session.SessionId, out var record) =>
                new SessionNodeViewModel(record.Id, record.ClaudeSessionId, record.Name, record.WorkingDirectory, record.HasBeenStarted, record.DetectedTitle),

            // A session hierarchy entry with no matching record means hand-edited/corrupt JSON -
            // drop just that entry rather than fail the whole load.
            _ => null,
        };

        if (viewModel is null)
        {
            return null;
        }

        // Every project/session starts expanded on load so the user sees the full tree immediately
        // instead of having to click each node open.
        viewModel.IsExpanded = true;
        viewModel.Parent = parent;
        foreach (var childNode in node.Children)
        {
            var child = BuildViewModel(childNode, recordsById, viewModel);
            if (child is not null)
            {
                viewModel.Children.Add(child);
            }
        }

        return viewModel;
    }

    private SessionsFile BuildSnapshot()
    {
        var file = new SessionsFile();
        foreach (var root in this.RootNodes)
        {
            file.Hierarchy.Add(BuildHierarchyNode(root, file.Sessions));
        }

        return file;
    }

    private static HierarchyNode BuildHierarchyNode(TreeNodeViewModel node, List<SessionRecord> sessions)
    {
        HierarchyNode hierarchyNode = node switch
        {
            ProjectNodeViewModel project => new ProjectHierarchyNode { Id = project.Id, Name = project.Name },
            SessionNodeViewModel session => BuildSessionHierarchyNode(session, sessions),
            _ => throw new InvalidOperationException($"Unknown tree node type: {node.GetType()}"),
        };

        foreach (var child in node.Children)
        {
            hierarchyNode.Children.Add(BuildHierarchyNode(child, sessions));
        }

        return hierarchyNode;
    }

    private static SessionHierarchyNode BuildSessionHierarchyNode(SessionNodeViewModel session, List<SessionRecord> sessions)
    {
        sessions.Add(new SessionRecord
        {
            Id = session.Id,
            ClaudeSessionId = session.ClaudeSessionId,
            Name = session.Name,
            WorkingDirectory = session.WorkingDirectory,
            HasBeenStarted = session.HasBeenStarted,
            DetectedTitle = session.DetectedTitle,
        });

        return new SessionHierarchyNode { SessionId = session.Id };
    }
}
