namespace LimbusCalc.ViewModels;

/// <summary>Строка справочника как пункт выпадающего списка.</summary>
public sealed class ExportTargetViewModel
{
    public required TableRowViewModel Row { get; init; }

    /// <summary>Подпись пункта: грешник и название айди.</summary>
    public required string Display { get; init; }

    public override string ToString() => Display;
}

/// <summary>
/// Куда выгружать набор калькулятора: строка справочника и столбец скилла.
/// </summary>
public sealed class ExportToTableViewModel : ObservableObject
{
    private ExportTargetViewModel? _selectedTarget;
    private TableColumn? _selectedSkill;

    public required IReadOnlyList<ExportTargetViewModel> Targets { get; init; }

    public required IReadOnlyList<TableColumn> Skills { get; init; }

    public ExportTargetViewModel? SelectedTarget
    {
        get => _selectedTarget;
        set
        {
            if (SetProperty(ref _selectedTarget, value))
            {
                OnPropertyChanged(nameof(CanSave));
            }
        }
    }

    public TableColumn? SelectedSkill
    {
        get => _selectedSkill;
        set
        {
            if (SetProperty(ref _selectedSkill, value))
            {
                OnPropertyChanged(nameof(CanSave));
            }
        }
    }

    /// <summary>Пока не выбраны и айди, и скилл, сохранять некуда.</summary>
    public bool CanSave => SelectedTarget is not null && SelectedSkill is not null;

    /// <summary>Клетка, в которую пойдёт выгрузка.</summary>
    public TableCell? TargetCell =>
        SelectedTarget is null || SelectedSkill is null
            ? null
            : SelectedTarget.Row.CellOf(SelectedSkill);

    /// <summary>Собирает список из строк справочника; безымянные строки пропускаем.</summary>
    public static ExportToTableViewModel Create(TableViewModel table)
    {
        ArgumentNullException.ThrowIfNull(table);

        List<ExportTargetViewModel> targets = [];

        foreach (TableRowViewModel row in table.Rows)
        {
            string name = row.CellOf("ID Name")?.Value.Trim() ?? string.Empty;

            if (name.Length == 0)
            {
                continue;
            }

            string sinner = row.CellOf("Sinner")?.Value.Trim() ?? string.Empty;

            targets.Add(new ExportTargetViewModel
            {
                Row = row,
                // Одно и то же название встречается у разных грешников, поэтому
                // в подписи оба: иначе LCB Sinner выглядел бы двенадцать раз подряд.
                Display = sinner.Length == 0 ? name : $"{sinner} — {name}",
            });
        }

        return new ExportToTableViewModel
        {
            Targets = targets,
            Skills = [.. table.Columns.Where(column => column.Kind == TableCellKind.Integer)],
        };
    }
}
