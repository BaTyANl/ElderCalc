namespace LimbusCalc.ViewModels;

/// <summary>The whole content of a cell — what undoing an edit brings back.</summary>
internal readonly record struct TableCellState(
    string Value,
    string? Setup,
    ElementOption? SkillType,
    ElementOption? SkillSin);

/// <summary>
/// A history step. Reverting a step returns the opposite step, which is then used for redo.
/// That way undo and redo share one piece of code per kind of edit.
/// </summary>
internal abstract class TableStep
{
    /// <summary>What was done — for the undo button tooltip.</summary>
    public required string Label { get; init; }

    /// <summary>Reverts the step and returns the step that reverts this revert.</summary>
    public abstract TableStep Revert(TableViewModel table);
}

/// <summary>An edit of one cell: number, marks and setup together.</summary>
internal sealed class CellStep : TableStep
{
    public required TableCell Cell { get; init; }

    public required TableCellState Before { get; init; }

    public override TableStep Revert(TableViewModel table)
    {
        TableCellState now = Cell.State;
        Cell.Restore(Before);

        return new CellStep { Cell = Cell, Before = now, Label = Label };
    }
}

/// <summary>
/// One row added or deleted. The row is kept whole, with its cells and setups,
/// so it comes back without loss.
/// </summary>
internal sealed class RowStep : TableStep
{
    public required TableRowViewModel Row { get; init; }

    /// <summary>Where in the table the row goes back.</summary>
    public required int Index { get; init; }

    /// <summary>True if reverting inserts the row; false if it removes it.</summary>
    public required bool Insert { get; init; }

    public override TableStep Revert(TableViewModel table)
    {
        int index = Insert ? Index : table.Rows.IndexOf(Row);

        if (Insert)
        {
            table.InsertRow(Math.Min(index, table.Rows.Count), Row);
        }
        else
        {
            table.DetachRow(Row);
        }

        return new RowStep { Row = Row, Index = index, Insert = !Insert, Label = Label };
    }
}

/// <summary>All rows at once: clearing the table or loading from a file.</summary>
internal sealed class RowsStep : TableStep
{
    public required IReadOnlyList<TableRowViewModel> Rows { get; init; }

    public override TableStep Revert(TableViewModel table)
    {
        List<TableRowViewModel> now = [.. table.Rows];
        table.ReplaceRows(Rows);

        return new RowsStep { Rows = now, Label = Label };
    }
}

/// <summary>
/// The table's edit history: what can be undone and redone. The depth is limited — clearing a
/// large table keeps all its rows in one step, and there's no point hoarding those forever.
/// </summary>
public sealed class TableHistory : ObservableObject
{
    private const int Limit = 100;

    private readonly List<TableStep> _undo = [];
    private readonly List<TableStep> _redo = [];

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    /// <summary>Undo button tooltip: what exactly it brings back.</summary>
    public string UndoHint => CanUndo ? $"Undo: {_undo[^1].Label} (Ctrl+Z)" : "Nothing to undo";

    public string RedoHint => CanRedo ? $"Redo: {_redo[^1].Label} (Ctrl+Y)" : "Nothing to redo";

    /// <summary>
    /// A new edit. There's nothing left to redo after it: the history has branched.
    /// An edit in a cell editor decides this on close, since Escape may still cancel it.
    /// </summary>
    internal void Push(TableStep step, bool keepRedo = false)
    {
        _undo.Add(step);

        if (_undo.Count > Limit)
        {
            _undo.RemoveAt(0);
        }

        if (!keepRedo)
        {
            _redo.Clear();
        }

        Notify();
    }

    internal void ForgetRedo()
    {
        if (_redo.Count > 0)
        {
            _redo.Clear();
            Notify();
        }
    }

    /// <summary>Drops the last step if it is this one — the edit changed nothing.</summary>
    internal void Drop(TableStep step)
    {
        if (_undo.Count > 0 && ReferenceEquals(_undo[^1], step))
        {
            _undo.RemoveAt(_undo.Count - 1);
            Notify();
        }
    }

    internal TableStep? TakeUndo() => Take(_undo);

    internal TableStep? TakeRedo() => Take(_redo);

    /// <summary>A reverted undo goes to redo and vice versa, without clearing the other side.</summary>
    internal void PutUndo(TableStep step)
    {
        _undo.Add(step);
        Notify();
    }

    internal void PutRedo(TableStep step)
    {
        _redo.Add(step);
        Notify();
    }

    private TableStep? Take(List<TableStep> steps)
    {
        if (steps.Count == 0)
        {
            return null;
        }

        TableStep step = steps[^1];
        steps.RemoveAt(steps.Count - 1);
        Notify();
        return step;
    }

    private void Notify()
    {
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
        OnPropertyChanged(nameof(UndoHint));
        OnPropertyChanged(nameof(RedoHint));
    }
}

/// <summary>
/// An open edit of one cell. Closing it leaves everything done as one history step;
/// <see cref="Cancel"/> puts the cell back as it was.
/// </summary>
public sealed class CellEdit : IDisposable
{
    private readonly TableViewModel _table;

    internal CellEdit(TableViewModel table, TableCell cell)
    {
        _table = table;
        Cell = cell;
    }

    public TableCell Cell { get; }

    /// <summary>Reverts everything done to the cell since the edit began and closes it.</summary>
    public void Cancel() => _table.CancelCellEdit(Cell);

    public void Dispose() => _table.EndCellEdit(Cell);
}
