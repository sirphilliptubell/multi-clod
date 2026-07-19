namespace MultiClod.App.Undo;

/// <summary>
/// Bounded undo/redo history for the session tree (move, delete, rename). Each entry is a pair of
/// closures the call site builds from the state it captured before mutating; this class only
/// knows how to stack and replay them - it has no idea what a "move" or "delete" is.
/// </summary>
public sealed class UndoManager
{
    private const int MaxDepth = 50;

    private readonly LinkedList<UndoEntry> undoStack = new();
    private readonly Stack<UndoEntry> redoStack = new();

    public void Push(Action undo, Action redo)
    {
        this.undoStack.AddLast(new UndoEntry(undo, redo));
        if (this.undoStack.Count > MaxDepth)
        {
            this.undoStack.RemoveFirst();
        }

        this.redoStack.Clear();
    }

    public bool TryUndo()
    {
        if (this.undoStack.Last is null)
        {
            return false;
        }

        var entry = this.undoStack.Last.Value;
        this.undoStack.RemoveLast();
        entry.Undo();
        this.redoStack.Push(entry);
        return true;
    }

    public bool TryRedo()
    {
        if (this.redoStack.Count == 0)
        {
            return false;
        }

        var entry = this.redoStack.Pop();
        entry.Redo();
        this.undoStack.AddLast(entry);
        return true;
    }

    private readonly record struct UndoEntry(Action Undo, Action Redo);
}
