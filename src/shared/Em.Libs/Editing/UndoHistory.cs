namespace Em.Editing;

/// <summary>Records completed changes. A failed callback leaves the change on its original stack.</summary>
internal sealed class UndoHistory
{
    private readonly Stack<Change> undo = new();
    private readonly Stack<Change> redo = new();

    public bool CanUndo => undo.Count != 0;
    public bool CanRedo => redo.Count != 0;

    public void Record(Action revert, Action apply)
    {
        undo.Push(new(revert, apply));
        redo.Clear();
    }

    public bool Undo()
    {
        if (!undo.TryPeek(out var change))
            return false;

        change.Revert();
        redo.Push(undo.Pop());
        return true;
    }

    public bool Redo()
    {
        if (!redo.TryPeek(out var change))
            return false;

        change.Apply();
        undo.Push(redo.Pop());
        return true;
    }

    private sealed record Change(Action Revert, Action Apply);
}
